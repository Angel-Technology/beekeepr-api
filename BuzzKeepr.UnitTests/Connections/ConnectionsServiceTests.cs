using BuzzKeepr.Application.Connections;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using NSubstitute;

namespace BuzzKeepr.UnitTests.Connections;

public sealed class ConnectionsServiceTests
{
    private readonly IConnectionsRepository repository = Substitute.For<IConnectionsRepository>();
    private readonly ConnectionsService sut;

    private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Target = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public ConnectionsServiceTests()
    {
        sut = new ConnectionsService(repository);

        // Defaults: target exists, no blocks in either direction. Individual tests override these.
        repository.UserExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        repository.CheckBlockedEitherDirectionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((false, false));
    }

    [Fact]
    public async Task SendFriendRequest_Self_ReturnsSelfTarget()
    {
        var result = await sut.SendFriendRequestAsync(Caller, Caller, default);

        Assert.True(result.SelfTarget);
        Assert.False(result.Success);
        await repository.DidNotReceiveWithAnyArgs().UserExistsAsync(default, default);
    }

    [Fact]
    public async Task SendFriendRequest_TargetMissing_ReturnsTargetNotFound()
    {
        repository.UserExistsAsync(Target, Arg.Any<CancellationToken>()).Returns(false);

        var result = await sut.SendFriendRequestAsync(Caller, Target, default);

        Assert.True(result.TargetNotFound);
    }

    [Fact]
    public async Task SendFriendRequest_CallerBlocksTarget_ReturnsBlockedByCaller()
    {
        repository.CheckBlockedEitherDirectionAsync(Caller, Target, Arg.Any<CancellationToken>())
            .Returns((true, false));

        var result = await sut.SendFriendRequestAsync(Caller, Target, default);

        Assert.True(result.BlockedByCaller);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task SendFriendRequest_TargetBlocksCaller_ReturnsBlockedByTarget()
    {
        repository.CheckBlockedEitherDirectionAsync(Caller, Target, Arg.Any<CancellationToken>())
            .Returns((false, true));

        var result = await sut.SendFriendRequestAsync(Caller, Target, default);

        // The GraphQL layer collapses this with TargetNotFound to avoid leaking blocking state —
        // the service surfaces the distinct flag and lets the mutation decide.
        Assert.True(result.BlockedByTarget);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task SendFriendRequest_NoExisting_CreatesNewPendingRow()
    {
        repository.FindFriendshipBetweenAsync(Caller, Target, Arg.Any<CancellationToken>())
            .Returns((Friendship?)null);

        var result = await sut.SendFriendRequestAsync(Caller, Target, default);

        Assert.True(result.Success);
        Assert.NotNull(result.Friendship);
        Assert.Equal(FriendshipStatus.Pending, result.Friendship!.Status);
        repository.Received(1).AddFriendship(Arg.Is<Friendship>(f =>
            f.RequesterId == Caller && f.AddresseeId == Target && f.Status == FriendshipStatus.Pending));
    }

    [Fact]
    public async Task SendFriendRequest_AlreadyAccepted_IsIdempotentSuccess()
    {
        var existing = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = Caller,
            AddresseeId = Target,
            Status = FriendshipStatus.Accepted,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        repository.FindFriendshipBetweenAsync(Caller, Target, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await sut.SendFriendRequestAsync(Caller, Target, default);

        Assert.True(result.Success);
        Assert.Equal(FriendshipStatus.Accepted, result.Friendship!.Status);
        repository.DidNotReceiveWithAnyArgs().AddFriendship(default!);
    }

    [Fact]
    public async Task SendFriendRequest_ReversePending_AutoAccepts()
    {
        var reverse = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = Target,
            AddresseeId = Caller,
            Status = FriendshipStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        repository.FindFriendshipBetweenAsync(Caller, Target, Arg.Any<CancellationToken>()).Returns(reverse);

        var result = await sut.SendFriendRequestAsync(Caller, Target, default);

        // Both sides expressed interest — flip to Accepted instead of creating a duplicate row.
        Assert.True(result.Success);
        Assert.Equal(FriendshipStatus.Accepted, result.Friendship!.Status);
        Assert.NotNull(reverse.RespondedAtUtc);
        repository.DidNotReceiveWithAnyArgs().AddFriendship(default!);
    }

    [Fact]
    public async Task AcceptFriendRequest_NoPending_ReturnsRequestNotFound()
    {
        repository.FindPendingRequestAsync(Target, Caller, Arg.Any<CancellationToken>()).Returns((Friendship?)null);

        var result = await sut.AcceptFriendRequestAsync(Caller, Target, default);

        Assert.True(result.RequestNotFound);
    }

    [Fact]
    public async Task AcceptFriendRequest_FlipsStatusAndStampsRespondedAt()
    {
        var pending = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = Target,
            AddresseeId = Caller,
            Status = FriendshipStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        repository.FindPendingRequestAsync(Target, Caller, Arg.Any<CancellationToken>()).Returns(pending);

        var result = await sut.AcceptFriendRequestAsync(Caller, Target, default);

        Assert.True(result.Success);
        Assert.Equal(FriendshipStatus.Accepted, pending.Status);
        Assert.NotNull(pending.RespondedAtUtc);
    }

    [Fact]
    public async Task BlockUser_Self_ReturnsSelfTarget()
    {
        var result = await sut.BlockUserAsync(Caller, Caller, default);

        Assert.True(result.SelfTarget);
        await repository.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync(default!, default);
    }

    [Fact]
    public async Task BlockUser_HappyPath_RemovesAnyFriendshipAndAddsBlock()
    {
        var existingFriendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = Caller,
            AddresseeId = Target,
            Status = FriendshipStatus.Accepted
        };
        repository.FindFriendshipBetweenAsync(Caller, Target, Arg.Any<CancellationToken>())
            .Returns(existingFriendship);
        repository.FindBlockAsync(Caller, Target, Arg.Any<CancellationToken>()).Returns((UserBlock?)null);

        // Just run the work delegate inline so we can assert on what the service did inside the
        // transaction without standing up a real DbContext.
        repository.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var work = call.Arg<Func<CancellationToken, Task<bool>>>();
                return work(default);
            });

        var result = await sut.BlockUserAsync(Caller, Target, default);

        Assert.True(result.Success);
        repository.Received(1).RemoveFriendship(existingFriendship);
        repository.Received(1).AddBlock(Arg.Is<UserBlock>(b => b.BlockerId == Caller && b.BlockedId == Target));
    }

    [Fact]
    public async Task FlagUser_Self_ReturnsSelfTarget()
    {
        var result = await sut.FlagUserAsync(Caller, Caller, default);

        Assert.True(result.SelfTarget);
        await repository.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync(default!, default);
    }

    [Fact]
    public async Task FlagUser_HappyPath_InsertsFlagRemovesFriendshipAndBlocks()
    {
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = Caller,
            AddresseeId = Target,
            Status = FriendshipStatus.Accepted
        };
        repository.FindFlagAsync(Caller, Target, Arg.Any<CancellationToken>()).Returns((UserFlag?)null);
        repository.FindFriendshipBetweenAsync(Caller, Target, Arg.Any<CancellationToken>()).Returns(friendship);
        repository.FindBlockAsync(Caller, Target, Arg.Any<CancellationToken>()).Returns((UserBlock?)null);
        repository.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var work = call.Arg<Func<CancellationToken, Task<bool>>>();
                return work(default);
            });

        var result = await sut.FlagUserAsync(Caller, Target, default);

        Assert.True(result.Success);
        repository.Received(1).AddFlag(Arg.Is<UserFlag>(f => f.FlaggerId == Caller && f.FlaggedUserId == Target));
        repository.Received(1).RemoveFriendship(friendship);
        repository.Received(1).AddBlock(Arg.Is<UserBlock>(b => b.BlockerId == Caller && b.BlockedId == Target));
    }

    [Fact]
    public async Task FlagUser_AlreadyFlaggedAndBlocked_IsIdempotent()
    {
        repository.FindFlagAsync(Caller, Target, Arg.Any<CancellationToken>())
            .Returns(new UserFlag { Id = Guid.NewGuid(), FlaggerId = Caller, FlaggedUserId = Target });
        repository.FindFriendshipBetweenAsync(Caller, Target, Arg.Any<CancellationToken>()).Returns((Friendship?)null);
        repository.FindBlockAsync(Caller, Target, Arg.Any<CancellationToken>())
            .Returns(new UserBlock { Id = Guid.NewGuid(), BlockerId = Caller, BlockedId = Target });
        repository.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var work = call.Arg<Func<CancellationToken, Task<bool>>>();
                return work(default);
            });

        var result = await sut.FlagUserAsync(Caller, Target, default);

        Assert.True(result.Success);
        repository.DidNotReceiveWithAnyArgs().AddFlag(default!);
        repository.DidNotReceiveWithAnyArgs().AddBlock(default!);
        repository.DidNotReceiveWithAnyArgs().RemoveFriendship(default!);
    }

    [Fact]
    public async Task RemoveFriend_NotFriends_IsSuccessNoOp()
    {
        repository.FindFriendshipBetweenAsync(Caller, Target, Arg.Any<CancellationToken>()).Returns((Friendship?)null);

        var result = await sut.RemoveFriendAsync(Caller, Target, default);

        Assert.True(result.Success);
        repository.DidNotReceiveWithAnyArgs().RemoveFriendship(default!);
    }

    [Fact]
    public async Task RemoveFriend_AcceptedFriendship_RemovesIt()
    {
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = Caller,
            AddresseeId = Target,
            Status = FriendshipStatus.Accepted
        };
        repository.FindFriendshipBetweenAsync(Caller, Target, Arg.Any<CancellationToken>()).Returns(friendship);

        var result = await sut.RemoveFriendAsync(Caller, Target, default);

        Assert.True(result.Success);
        repository.Received(1).RemoveFriendship(friendship);
    }

    [Fact]
    public async Task RemoveFriend_PendingRequest_DoesNotRemoveIt()
    {
        // Pending requests are removed via decline/cancel; removeFriend is for accepted only.
        var pending = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = Caller,
            AddresseeId = Target,
            Status = FriendshipStatus.Pending
        };
        repository.FindFriendshipBetweenAsync(Caller, Target, Arg.Any<CancellationToken>()).Returns(pending);

        var result = await sut.RemoveFriendAsync(Caller, Target, default);

        Assert.True(result.Success);
        repository.DidNotReceiveWithAnyArgs().RemoveFriendship(default!);
    }
}
