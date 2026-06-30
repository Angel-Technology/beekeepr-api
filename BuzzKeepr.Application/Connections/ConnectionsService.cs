using BuzzKeepr.Application.Connections.Models;
using BuzzKeepr.Application.Notifications;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Connections;

public sealed class ConnectionsService(
    IConnectionsRepository repository,
    IFriendRequestNotifier notifier) : IConnectionsService
{
    public async Task<SendFriendRequestResult> SendFriendRequestAsync(
        Guid currentUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (currentUserId == targetUserId)
            return new SendFriendRequestResult { SelfTarget = true };

        if (!await repository.UserExistsAsync(targetUserId, cancellationToken))
            return new SendFriendRequestResult { TargetNotFound = true };

        var (callerBlocksTarget, targetBlocksCaller) =
            await repository.CheckBlockedEitherDirectionAsync(currentUserId, targetUserId, cancellationToken);

        // Order matters: caller-blocked-target wins so the user gets the actionable error
        // ("unblock first") instead of the opaque target-blocks-caller one.
        if (callerBlocksTarget)
            return new SendFriendRequestResult { BlockedByCaller = true };

        if (targetBlocksCaller)
            return new SendFriendRequestResult { BlockedByTarget = true };

        var existing = await repository.FindFriendshipBetweenAsync(currentUserId, targetUserId, cancellationToken);

        if (existing is not null)
        {
            // Auto-accept the reverse pending request: both sides clearly want this connection.
            if (existing.Status == FriendshipStatus.Pending && existing.RequesterId == targetUserId)
            {
                existing.Status = FriendshipStatus.Accepted;
                existing.RespondedAtUtc = DateTime.UtcNow;
                await repository.SaveChangesAsync(cancellationToken);

                // Auto-accept: notify the OTHER user (the original requester) that their
                // outgoing request was accepted. They were waiting on it; the current user
                // already knows what they just did.
                await notifier.NotifyRequestAcceptedAsync(targetUserId, currentUserId, cancellationToken);

                return new SendFriendRequestResult { Success = true, Friendship = MapFriendship(existing) };
            }

            // Already-pending-by-caller or already-accepted: idempotent no-op — don't notify.
            return new SendFriendRequestResult { Success = true, Friendship = MapFriendship(existing) };
        }

        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = currentUserId,
            AddresseeId = targetUserId,
            Status = FriendshipStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        repository.AddFriendship(friendship);
        await repository.SaveChangesAsync(cancellationToken);

        // Fresh request — tell the addressee.
        await notifier.NotifyRequestReceivedAsync(targetUserId, currentUserId, cancellationToken);

        return new SendFriendRequestResult { Success = true, Friendship = MapFriendship(friendship) };
    }

    public async Task<RespondToFriendRequestResult> AcceptFriendRequestAsync(
        Guid currentUserId,
        Guid requesterId,
        CancellationToken cancellationToken)
    {
        var pending = await repository.FindPendingRequestAsync(requesterId, currentUserId, cancellationToken);

        if (pending is null)
            return new RespondToFriendRequestResult { RequestNotFound = true };

        pending.Status = FriendshipStatus.Accepted;
        pending.RespondedAtUtc = DateTime.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);

        // Tell the original requester their request was accepted. Current user just performed
        // the accept so they already know — only notify the other side.
        await notifier.NotifyRequestAcceptedAsync(requesterId, currentUserId, cancellationToken);

        return new RespondToFriendRequestResult { Success = true, Friendship = MapFriendship(pending) };
    }

    public async Task<RespondToFriendRequestResult> DeclineFriendRequestAsync(
        Guid currentUserId,
        Guid requesterId,
        CancellationToken cancellationToken)
    {
        var pending = await repository.FindPendingRequestAsync(requesterId, currentUserId, cancellationToken);

        if (pending is null)
            return new RespondToFriendRequestResult { RequestNotFound = true };

        repository.RemoveFriendship(pending);
        await repository.SaveChangesAsync(cancellationToken);

        return new RespondToFriendRequestResult { Success = true };
    }

    public async Task<RespondToFriendRequestResult> CancelFriendRequestAsync(
        Guid currentUserId,
        Guid addresseeId,
        CancellationToken cancellationToken)
    {
        var pending = await repository.FindPendingRequestAsync(currentUserId, addresseeId, cancellationToken);

        if (pending is null)
            return new RespondToFriendRequestResult { RequestNotFound = true };

        repository.RemoveFriendship(pending);
        await repository.SaveChangesAsync(cancellationToken);

        return new RespondToFriendRequestResult { Success = true };
    }

    public async Task<RemoveFriendResult> RemoveFriendAsync(
        Guid currentUserId,
        Guid otherUserId,
        CancellationToken cancellationToken)
    {
        var friendship = await repository.FindFriendshipBetweenAsync(currentUserId, otherUserId, cancellationToken);

        // Idempotent: removing a non-existent friendship is a success no-op so the frontend doesn't
        // have to special-case "already not friends."
        if (friendship is null || friendship.Status != FriendshipStatus.Accepted)
            return new RemoveFriendResult { Success = true };

        repository.RemoveFriendship(friendship);
        await repository.SaveChangesAsync(cancellationToken);

        return new RemoveFriendResult { Success = true };
    }

    public async Task<BlockUserResult> BlockUserAsync(
        Guid currentUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (currentUserId == targetUserId)
            return new BlockUserResult { SelfTarget = true };

        if (!await repository.UserExistsAsync(targetUserId, cancellationToken))
            return new BlockUserResult { TargetNotFound = true };

        var committed = await repository.ExecuteInTransactionAsync(async ct =>
        {
            var existingFriendship = await repository.FindFriendshipBetweenAsync(currentUserId, targetUserId, ct);
            if (existingFriendship is not null)
                repository.RemoveFriendship(existingFriendship);

            var existingBlock = await repository.FindBlockAsync(currentUserId, targetUserId, ct);
            if (existingBlock is null)
            {
                repository.AddBlock(new UserBlock
                {
                    Id = Guid.NewGuid(),
                    BlockerId = currentUserId,
                    BlockedId = targetUserId,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await repository.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);

        return new BlockUserResult { Success = committed };
    }

    public async Task<UnblockUserResult> UnblockUserAsync(
        Guid currentUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var block = await repository.FindBlockAsync(currentUserId, targetUserId, cancellationToken);

        if (block is null)
            return new UnblockUserResult { Success = true };

        repository.RemoveBlock(block);
        await repository.SaveChangesAsync(cancellationToken);

        return new UnblockUserResult { Success = true };
    }

    public async Task<FlagUserResult> FlagUserAsync(
        Guid currentUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (currentUserId == targetUserId)
            return new FlagUserResult { SelfTarget = true };

        if (!await repository.UserExistsAsync(targetUserId, cancellationToken))
            return new FlagUserResult { TargetNotFound = true };

        var committed = await repository.ExecuteInTransactionAsync(async ct =>
        {
            var existingFlag = await repository.FindFlagAsync(currentUserId, targetUserId, ct);
            if (existingFlag is null)
            {
                repository.AddFlag(new UserFlag
                {
                    Id = Guid.NewGuid(),
                    FlaggerId = currentUserId,
                    FlaggedUserId = targetUserId,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            // Flag also unfriends and blocks. Idempotent on re-flag — already-blocked stays blocked,
            // already-not-friends stays not-friends.
            var existingFriendship = await repository.FindFriendshipBetweenAsync(currentUserId, targetUserId, ct);
            if (existingFriendship is not null)
                repository.RemoveFriendship(existingFriendship);

            var existingBlock = await repository.FindBlockAsync(currentUserId, targetUserId, ct);
            if (existingBlock is null)
            {
                repository.AddBlock(new UserBlock
                {
                    Id = Guid.NewGuid(),
                    BlockerId = currentUserId,
                    BlockedId = targetUserId,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await repository.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);

        return new FlagUserResult { Success = committed };
    }

    public IQueryable<UserConnectionDto> ListFriends(Guid currentUserId)
        => repository.QueryFriends(currentUserId);

    public IQueryable<UserConnectionDto> ListIncomingFriendRequests(Guid currentUserId)
        => repository.QueryIncomingFriendRequests(currentUserId);

    public IQueryable<UserConnectionDto> ListOutgoingFriendRequests(Guid currentUserId)
        => repository.QueryOutgoingFriendRequests(currentUserId);

    public IQueryable<UserConnectionDto> ListBlockedUsers(Guid currentUserId)
        => repository.QueryBlockedUsers(currentUserId);

    private static FriendshipDto MapFriendship(Friendship friendship) => new()
    {
        Id = friendship.Id,
        RequesterId = friendship.RequesterId,
        AddresseeId = friendship.AddresseeId,
        Status = friendship.Status,
        CreatedAtUtc = friendship.CreatedAtUtc,
        RespondedAtUtc = friendship.RespondedAtUtc
    };
}
