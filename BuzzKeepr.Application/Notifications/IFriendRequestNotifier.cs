namespace BuzzKeepr.Application.Notifications;

// Two-method surface: one per event the social graph emits notifications for. Both are
// fire-and-forget from the caller's perspective — implementations catch + log failures so
// a flaky push transport never breaks the underlying mutation.
public interface IFriendRequestNotifier
{
    Task NotifyRequestReceivedAsync(Guid recipientUserId, Guid requesterUserId, CancellationToken cancellationToken);

    Task NotifyRequestAcceptedAsync(Guid originalRequesterUserId, Guid accepterUserId, CancellationToken cancellationToken);
}
