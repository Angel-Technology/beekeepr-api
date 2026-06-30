using BuzzKeepr.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Application.Notifications;

public sealed class FriendRequestNotifier(
    IPushTokenRepository pushTokenRepository,
    INotificationProfileLookup profileLookup,
    IApnsPushClient apnsPushClient,
    ILogger<FriendRequestNotifier> logger) : IFriendRequestNotifier
{
    // APNs error reasons that mean "this token is dead, never send to it again." Apple uses
    // these interchangeably depending on transport state; the prune behavior is the same.
    private static readonly HashSet<string> DeadTokenErrorCodes = new(StringComparer.Ordinal)
    {
        "Unregistered",     // 410 Gone — canonical "token has been invalidated"
        "BadDeviceToken",   // 400 — token wasn't valid for this APNs environment / topic
    };

    public Task NotifyRequestReceivedAsync(Guid recipientUserId, Guid requesterUserId, CancellationToken cancellationToken)
    {
        return SendAsync(
            recipientUserId,
            requesterUserId,
            type: "FRIEND_REQUEST_RECEIVED",
            buildTitle: _ => "New friend request",
            buildBody: requesterDisplay => $"{requesterDisplay} wants to be friends",
            cancellationToken);
    }

    public Task NotifyRequestAcceptedAsync(Guid originalRequesterUserId, Guid accepterUserId, CancellationToken cancellationToken)
    {
        return SendAsync(
            originalRequesterUserId,
            accepterUserId,
            type: "FRIEND_REQUEST_ACCEPTED",
            buildTitle: _ => "Friend request accepted",
            buildBody: accepterDisplay => $"{accepterDisplay} accepted your friend request",
            cancellationToken);
    }

    private async Task SendAsync(
        Guid recipientUserId,
        Guid actorUserId,
        string type,
        Func<string, string> buildTitle,
        Func<string, string> buildBody,
        CancellationToken cancellationToken)
    {
        try
        {
            var allTokens = await pushTokenRepository.GetTokensForUserAsync(recipientUserId, cancellationToken);
            if (allTokens.Count == 0)
            {
                logger.LogDebug(
                    "Skipping {Type} push for user {RecipientUserId}: no registered tokens.",
                    type,
                    recipientUserId);
                return;
            }

            // Branch by platform. iOS goes through APNs; Android will go through FCM once we
            // wire it up, but for now we log + skip so the call site stays correct.
            var iosTokens = allTokens.Where(t => t.Platform == PushPlatform.iOS).Select(t => t.Token).ToList();
            var androidCount = allTokens.Count(t => t.Platform == PushPlatform.Android);
            if (androidCount > 0)
            {
                logger.LogInformation(
                    "Skipping {AndroidCount} Android token(s) for user {RecipientUserId}: FCM not yet wired (TODO).",
                    androidCount,
                    recipientUserId);
            }

            if (iosTokens.Count == 0)
                return;

            // Display-name fallback chain: DisplayName → Nickname → Handle → generic "Someone".
            // Actor may not have a UserProfile row yet if they haven't completed onboarding;
            // the lookup returns null in that case, and we fall through to "Someone."
            var actorProfile = await profileLookup.GetDisplayNameAsync(actorUserId, cancellationToken);
            var displayName = actorProfile ?? "Someone";

            var receipts = await apnsPushClient.SendAsync(
                iosTokens,
                title: buildTitle(displayName),
                body: buildBody(displayName),
                data: new Dictionary<string, string>
                {
                    ["type"] = type,
                    // Frontend deep-link payload — tap routes to the right screen based on type
                    // and (where applicable) the actor's user id so we can highlight the row.
                    ["actorUserId"] = actorUserId.ToString()
                },
                cancellationToken);

            // Prune tokens APNs reports as dead. Same-direction recursion isn't a concern — we
            // delete by string match, so even if the user has been re-issued a new token since,
            // it would have a different string.
            var deadTokens = receipts
                .Where(r => r.ErrorCode is not null && DeadTokenErrorCodes.Contains(r.ErrorCode))
                .Select(r => r.Token)
                .ToList();

            if (deadTokens.Count > 0)
            {
                var deleted = await pushTokenRepository.DeleteByTokensAsync(deadTokens, cancellationToken);
                logger.LogInformation(
                    "Pruned {Deleted} dead push token(s) reported by APNs for user {RecipientUserId}.",
                    deleted,
                    recipientUserId);
            }

            var deliveredCount = receipts.Count(r => r.Ok);
            logger.LogInformation(
                "{Type} push delivered to {Delivered}/{Total} APNs token(s) for user {RecipientUserId}.",
                type,
                deliveredCount,
                receipts.Count,
                recipientUserId);
        }
        catch (Exception exception)
        {
            // Notifier failures must never bubble — the mutation already committed; the user's
            // friend request was created/accepted successfully. Push is a side channel.
            logger.LogWarning(
                exception,
                "Failed to send {Type} push notification to user {RecipientUserId}.",
                type,
                recipientUserId);
        }
    }
}

// Tiny abstraction so the notifier doesn't have to take a dependency on UserService /
// UserRepository (which would create a tangle — ConnectionsService → notifier → UserService).
// The infrastructure implementation hits UserProfiles directly with one indexed lookup.
public interface INotificationProfileLookup
{
    Task<string?> GetDisplayNameAsync(Guid userId, CancellationToken cancellationToken);
}
