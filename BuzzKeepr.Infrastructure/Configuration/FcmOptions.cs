namespace BuzzKeepr.Infrastructure.Configuration;

// Firebase Cloud Messaging credentials for Android push notifications.
// Both fields come from the Firebase Console's service-account JSON:
// Project settings → Service accounts → Generate new private key.
public sealed class FcmOptions
{
    public const string SectionName = "Fcm";

    // Full JSON contents of the service-account key file (the whole { "type": "service_account", ... }
    // object). FirebaseAdmin's GoogleCredential.FromJson consumes this directly.
    public string ServiceAccountJsonContents { get; init; } = string.Empty;

    // Firebase project id (e.g. "buzzkeepr"). Must match the `project_id` field inside
    // ServiceAccountJsonContents — kept as a separate config value so we can spot a
    // credential/project mismatch at boot instead of at first-send.
    public string ProjectId { get; init; } = string.Empty;
}
