using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuzzKeepr.Application.Notifications;
using BuzzKeepr.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure.Notifications;

// Direct-to-APNs push client. Replaces the earlier Expo relay — we hold the .p8 key on the
// backend, sign our own provider tokens, and POST one HTTP/2 request per recipient device.
//
// Auth: APNs accepts a JWT signed with ES256 using the .p8 private key. The JWT must be
// re-signed at least once per hour (Apple drops anything older). We cache for 55 minutes;
// concurrent requests share the same cached value via a small lock.
//
// Wire format reference: https://developer.apple.com/documentation/usernotifications/sending_notification_requests_to_apns
public sealed class ApnsPushClient : IApnsPushClient, IDisposable
{
    // Apple's documented JWT lifetime is ~1 hour; refresh slightly inside that to avoid races.
    private static readonly TimeSpan JwtLifetime = TimeSpan.FromMinutes(55);

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;
    private readonly ApnsOptions options;
    private readonly ILogger<ApnsPushClient> logger;
    private readonly ECDsa? signingKey;
    private readonly Lock jwtLock = new();
    private string? cachedJwt;
    private DateTime cachedJwtExpiresAtUtc;

    public ApnsPushClient(
        HttpClient httpClient,
        IOptions<ApnsOptions> optionsAccessor,
        ILogger<ApnsPushClient> logger)
    {
        this.httpClient = httpClient;
        options = optionsAccessor.Value;
        this.logger = logger;

        if (string.IsNullOrWhiteSpace(options.KeyContents))
        {
            // Allow boot without credentials — useful in tests and pre-config dev. Every send
            // is then a no-op with a warning log. This is preferable to a startup crash that
            // would block unrelated features.
            logger.LogWarning(
                "APNs credentials not configured (Apns:KeyContents missing); push notifications will be skipped.");
            return;
        }

        try
        {
            signingKey = ECDsa.Create();
            signingKey.ImportFromPem(options.KeyContents);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to load APNs signing key; push notifications will be skipped.");
            signingKey?.Dispose();
            signingKey = null;
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

        if (signingKey is null)
        {
            return deviceTokens
                .Select(token => new PushReceipt { Token = token, Ok = false, ErrorMessage = "APNs not configured" })
                .ToList();
        }

        string jwt;
        try
        {
            jwt = GetOrCreateJwt();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to sign APNs provider token.");
            return deviceTokens
                .Select(token => new PushReceipt { Token = token, Ok = false, ErrorMessage = "JWT signing failed" })
                .ToList();
        }

        // APNs HTTP/2 requires one stream per device — no batching. Fan out concurrently.
        var sendTasks = deviceTokens
            .Select(token => SendOneAsync(token, jwt, title, body, data, cancellationToken))
            .ToList();
        return await Task.WhenAll(sendTasks);
    }

    private async Task<PushReceipt> SendOneAsync(
        string deviceToken,
        string jwt,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken cancellationToken)
    {
        var payload = BuildAlertPayload(title, body, data);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/3/device/{deviceToken}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("authorization", $"bearer {jwt}");
        request.Headers.TryAddWithoutValidation("apns-topic", options.BundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            // Network or HTTP/2-level failure. Don't flag the token as dead; transient.
            logger.LogWarning(exception, "APNs HTTP failure for token {Token}.", Mask(deviceToken));
            return new PushReceipt { Token = deviceToken, Ok = false, ErrorMessage = exception.Message };
        }

        if (response.IsSuccessStatusCode)
            return new PushReceipt { Token = deviceToken, Ok = true };

        // Per Apple's docs, the error reason ships in the JSON body. 410 Gone is the canonical
        // "token is dead" signal but Apple sometimes also returns 400 BadDeviceToken — both
        // belong to the prunable set.
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        string? reason = null;
        try
        {
            var error = JsonSerializer.Deserialize<ApnsErrorBody>(responseBody, ResponseJsonOptions);
            reason = error?.Reason;
        }
        catch
        {
            // Body not JSON, e.g. an upstream proxy responding. Leave reason null.
        }

        // Treat 410 as Unregistered even if the body parse failed.
        if (response.StatusCode == HttpStatusCode.Gone && string.IsNullOrEmpty(reason))
            reason = "Unregistered";

        logger.LogWarning(
            "APNs returned {StatusCode} for token {Token}: {Reason} ({Body})",
            (int)response.StatusCode,
            Mask(deviceToken),
            reason,
            responseBody);

        return new PushReceipt
        {
            Token = deviceToken,
            Ok = false,
            ErrorCode = reason,
            ErrorMessage = responseBody
        };
    }

    private string GetOrCreateJwt()
    {
        lock (jwtLock)
        {
            if (cachedJwt is not null && DateTime.UtcNow < cachedJwtExpiresAtUtc)
                return cachedJwt;

            var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var header = $"{{\"alg\":\"ES256\",\"kid\":\"{options.KeyId}\"}}";
            var payload = $"{{\"iss\":\"{options.TeamId}\",\"iat\":{nowSeconds}}}";

            var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
            var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            var signingInput = $"{headerB64}.{payloadB64}";

            // ES256 in JWT requires IEEE P1363 (raw concatenation) signature format, not the
            // ASN.1/DER format ECDsa.SignData returns by default.
            var signature = signingKey!.SignData(
                Encoding.UTF8.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            var signatureB64 = Base64UrlEncode(signature);

            cachedJwt = $"{signingInput}.{signatureB64}";
            cachedJwtExpiresAtUtc = DateTime.UtcNow.Add(JwtLifetime);
            return cachedJwt;
        }
    }

    private static object BuildAlertPayload(string title, string body, IReadOnlyDictionary<string, string>? data)
    {
        // APNs expects custom keys at the root, alongside the `aps` object. The frontend reads
        // `type` + any actor ids from there to drive deep-linking.
        var root = new Dictionary<string, object?>
        {
            ["aps"] = new Dictionary<string, object?>
            {
                ["alert"] = new Dictionary<string, object?>
                {
                    ["title"] = title,
                    ["body"] = body
                },
                ["sound"] = "default"
            }
        };

        if (data is not null)
        {
            foreach (var (key, value) in data)
                root[key] = value;
        }

        return root;
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // Avoid logging full APNs device tokens — they're not super sensitive but they let anyone
    // with our credentials send pushes to that user. Mask all but the last 8 chars.
    private static string Mask(string token)
        => token.Length <= 8 ? "***" : $"***{token[^8..]}";

    public void Dispose() => signingKey?.Dispose();

    private sealed class ApnsErrorBody
    {
        public string? Reason { get; init; }
    }
}
