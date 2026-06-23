using BuzzKeepr.Application.Connections.Models;

namespace BuzzKeepr.Application.Connections;

public interface IConnectionsService
{
    Task<SendFriendRequestResult> SendFriendRequestAsync(
        Guid currentUserId,
        Guid targetUserId,
        CancellationToken cancellationToken);

    Task<RespondToFriendRequestResult> AcceptFriendRequestAsync(
        Guid currentUserId,
        Guid requesterId,
        CancellationToken cancellationToken);

    Task<RespondToFriendRequestResult> DeclineFriendRequestAsync(
        Guid currentUserId,
        Guid requesterId,
        CancellationToken cancellationToken);

    Task<RespondToFriendRequestResult> CancelFriendRequestAsync(
        Guid currentUserId,
        Guid addresseeId,
        CancellationToken cancellationToken);

    Task<RemoveFriendResult> RemoveFriendAsync(
        Guid currentUserId,
        Guid otherUserId,
        CancellationToken cancellationToken);

    Task<BlockUserResult> BlockUserAsync(
        Guid currentUserId,
        Guid targetUserId,
        CancellationToken cancellationToken);

    Task<UnblockUserResult> UnblockUserAsync(
        Guid currentUserId,
        Guid targetUserId,
        CancellationToken cancellationToken);

    Task<FlagUserResult> FlagUserAsync(
        Guid currentUserId,
        Guid targetUserId,
        CancellationToken cancellationToken);

    IQueryable<UserConnectionDto> ListFriends(Guid currentUserId);

    IQueryable<UserConnectionDto> ListIncomingFriendRequests(Guid currentUserId);

    IQueryable<UserConnectionDto> ListOutgoingFriendRequests(Guid currentUserId);

    IQueryable<UserConnectionDto> ListBlockedUsers(Guid currentUserId);
}
