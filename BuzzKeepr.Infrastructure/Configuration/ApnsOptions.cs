namespace BuzzKeepr.Infrastructure.Configuration;

// Apple Push Notification service credentials. Loaded from the `Apns:` config section. In dev,
// keep these in user-secrets (not appsettings) — the .p8 contents are a private key, anyone with
// them can send pushes signed as your Apple team. In prod, set as env vars on Render.
//
// Setup (one-time per machine):
//   dotnet user-secrets set "Apns:KeyContents" "$(cat ~/Downloads/AuthKey_UKBU4495CF.p8)" \
//     --project BuzzKeepr.Presentation
//   dotnet user-secrets set "Apns:KeyId"     "UKBU4495CF"
//   dotnet user-secrets set "Apns:TeamId"    "37PSB2TKJ2"
//   dotnet user-secrets set "Apns:BundleId"  "com.buzzkeepr.app"
//   dotnet user-secrets set "Apns:UseSandbox" "true"   # only when testing with a dev-signed build
//
// In Render, paste the same values as env vars Apns__KeyContents, Apns__KeyId, Apns__TeamId,
// Apns__BundleId. Render preserves newlines in multi-line env vars so the PEM round-trips fine.
public sealed class ApnsOptions
{
    public const string SectionName = "Apns";

    // The full PEM-encoded contents of the .p8 file Apple issued. Includes the
    // -----BEGIN PRIVATE KEY----- header/footer lines. ECDsa.ImportFromPem reads this directly.
    public string KeyContents { get; init; } = string.Empty;

    // Key ID Apple assigned when you generated the key (10-char alphanumeric).
    public string KeyId { get; init; } = string.Empty;

    // Your Apple Developer team ID (10-char alphanumeric). Used as the `iss` claim in the JWT.
    public string TeamId { get; init; } = string.Empty;

    // The iOS app's bundle ID (e.g. com.buzzkeepr.app). Sent as the `apns-topic` header on
    // every push and tells APNs which app to deliver to.
    public string BundleId { get; init; } = string.Empty;

    // Use api.sandbox.push.apple.com (TestFlight / dev builds) instead of api.push.apple.com
    // (production / App Store builds). Tokens issued by a dev-signed build only work with
    // sandbox, tokens issued by a prod-signed build only work with production — mixing them
    // gets BadDeviceToken / DeviceNotRegistered.
    public bool UseSandbox { get; init; }
}
