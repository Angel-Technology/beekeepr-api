using BuzzKeepr.Application.Notifications;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// FirebaseAdmin.Messaging ships its own FcmOptions type — alias our config class to sidestep
// the name clash without renaming it (config sections would need to move too).
using FcmOptions = BuzzKeepr.Infrastructure.Configuration.FcmOptions;

namespace BuzzKeepr.Infrastructure.Notifications;

// Direct-to-FCM push client for Android. Uses the FirebaseAdmin SDK to sign requests
// with a service-account credential and POST to the FCM v1 HTTP API.
//
// Registered as a singleton because FirebaseApp is a process-wide named singleton and
// initialization is expensive (parses the JSON key, sets up an OAuth token cache). Doing
// this per-scope would create/destroy a FirebaseApp on every request.
public sealed class FcmPushClient : IFcmPushClient
{
    // Dead-token error codes exposed to the notifier's prune set. Mirrors the APNs
    // vocabulary (`Unregistered` / `BadDeviceToken`) so FriendRequestNotifier's
    // DeadTokenErrorCodes set works uniformly across both providers.
    private const string DeadTokenUnregistered = "Unregistered";
    private const string DeadTokenBadFormat = "BadDeviceToken";

    private readonly FcmOptions options;
    private readonly ILogger<FcmPushClient> logger;
    private readonly FirebaseMessaging? messaging;

    public FcmPushClient(IOptions<FcmOptions> optionsAccessor, ILogger<FcmPushClient> logger)
    {
        options = optionsAccessor.Value;
        this.logger = logger;

        if (string.IsNullOrWhiteSpace(options.ServiceAccountJsonContents))
        {
            // Allow boot without credentials — mirrors ApnsPushClient's behavior. Every send
            // then becomes a no-op with a warning, which is preferable to a startup crash
            // that would block iOS notifications too.
            logger.LogWarning(
                "FCM credentials not configured (Fcm:ServiceAccountJsonContents missing); Android push notifications will be skipped.");
            return;
        }

        try
        {
            // FromJson is marked obsolete over a filesystem-key-handling concern that doesn't
            // apply here — we already accept the credential contents as a config value, not a
            // path — so the deprecation guidance (CredentialFactory) has no practical benefit
            // and would just add ceremony.
#pragma warning disable CS0618
            var credential = GoogleCredential.FromJson(options.ServiceAccountJsonContents);
#pragma warning restore CS0618
            // Named FirebaseApp so re-initialization is safe (idempotent lookup) if this ctor
            // ever runs twice. Uses the configured project id as the stable name.
            var appName = string.IsNullOrWhiteSpace(options.ProjectId) ? "buzzkeepr-fcm" : $"buzzkeepr-{options.ProjectId}";
            var existing = FirebaseApp.GetInstance(appName);
            var app = existing ?? FirebaseApp.Create(new AppOptions
            {
                Credential = credential,
                ProjectId = options.ProjectId
            }, appName);
            messaging = FirebaseMessaging.GetMessaging(app);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to initialize Firebase Admin SDK; Android push notifications will be skipped.");
            messaging = null;
        }
    }

    public async Task<IReadOnlyList<PushReceipt>> SendAsync(
        IReadOnlyList<string> deviceTokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken cancellationToken)
    {
        if (deviceTokens.Count == 0)
            return [];

        if (messaging is null)
        {
            return deviceTokens
                .Select(token => new PushReceipt { Token = token, Ok = false, ErrorMessage = "FCM not configured" })
                .ToList();
        }

        // Build one Message per token. FirebaseAdmin's SendEachAsync returns a BatchResponse
        // whose Responses[i] pairs with messages[i] positionally, so we can zip receipts back
        // to the source token by index. Individual token failures don't fail the batch.
        var messages = deviceTokens.Select(token => new Message
        {
            Token = token,
            Notification = new Notification { Title = title, Body = body },
            // FCM requires string-valued data payloads. The notifier only ever passes strings
            // (type, actorUserId) so a straight ToDictionary is safe.
            Data = data?.ToDictionary(pair => pair.Key, pair => pair.Value),
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                Notification = new AndroidNotification { Sound = "default" }
            }
        }).ToList();

        BatchResponse batch;
        try
        {
            batch = await messaging.SendEachAsync(messages, cancellationToken);
        }
        catch (Exception exception)
        {
            // Batch-level failure means the request never got to per-token evaluation — network
            // error, auth failure, etc. Return per-token receipts marking all as transient.
            logger.LogWarning(exception, "FCM SendEachAsync failed for {Count} token(s).", deviceTokens.Count);
            return deviceTokens
                .Select(token => new PushReceipt { Token = token, Ok = false, ErrorMessage = exception.Message })
                .ToList();
        }

        var receipts = new List<PushReceipt>(deviceTokens.Count);
        for (var i = 0; i < deviceTokens.Count; i++)
        {
            var token = deviceTokens[i];
            var response = batch.Responses[i];

            if (response.IsSuccess)
            {
                receipts.Add(new PushReceipt { Token = token, Ok = true });
                continue;
            }

            var errorCode = MapErrorCode(response.Exception);
            if (errorCode is DeadTokenUnregistered or DeadTokenBadFormat)
            {
                logger.LogInformation("FCM reported dead token {Token}: {Reason}", Mask(token), errorCode);
            }
            else
            {
                logger.LogWarning(response.Exception, "FCM error for token {Token}: {Reason}", Mask(token), errorCode ?? "(unmapped)");
            }

            receipts.Add(new PushReceipt
            {
                Token = token,
                Ok = false,
                ErrorCode = errorCode,
                ErrorMessage = response.Exception?.Message
            });
        }
        return receipts;
    }

    // Translate FCM error codes into the notifier's dead-token vocabulary. UNREGISTERED means
    // the app was uninstalled or the token was invalidated by the client SDK; SENDER_ID_MISMATCH
    // means the token was issued for a different Firebase project. Both are prunable.
    // INVALID_ARGUMENT covers malformed tokens (equivalent to BadDeviceToken on APNs).
    private static string? MapErrorCode(FirebaseMessagingException? exception)
    {
        if (exception is null)
            return null;

        return exception.MessagingErrorCode switch
        {
            MessagingErrorCode.Unregistered => DeadTokenUnregistered,
            MessagingErrorCode.SenderIdMismatch => DeadTokenUnregistered,
            MessagingErrorCode.InvalidArgument => DeadTokenBadFormat,
            // Fall back to the coarser Firebase-wide ErrorCode enum (Unavailable, Internal, etc.)
            // when the messaging-specific code wasn't set.
            null => exception.ErrorCode.ToString(),
            _ => exception.MessagingErrorCode.ToString()
        };
    }

    // Never log full device tokens — same rationale as ApnsPushClient.Mask.
    private static string Mask(string token)
        => token.Length <= 8 ? "***" : $"***{token[^8..]}";
}
