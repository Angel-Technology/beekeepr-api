using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure.IdentityVerification;

public sealed class CheckrTrustClient(
    HttpClient httpClient,
    IMemoryCache memoryCache,
    IOptions<CheckrTrustOptions> checkrTrustOptions,
    ILogger<CheckrTrustClient> logger) : ICheckrTrustClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri DefaultApiBaseUri = new("https://api.checkrtrust.com");
    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(5);
    private readonly CheckrTrustOptions options = checkrTrustOptions.Value;
    private readonly Uri apiBaseUri = CreateApiBaseUri(checkrTrustOptions.Value.ApiBaseUrl, logger);

    public async Task<CreateInstantCriminalCheckResult> CreateInstantCriminalCheckAsync(
        CreateInstantCriminalCheckInput input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            return new CreateInstantCriminalCheckResult
            {
                Error = "CheckrTrust:ClientId is not configured."
            };
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            return new CreateInstantCriminalCheckResult
            {
                Error = "CheckrTrust:ClientSecret is not configured."
            };
        }

        var accessToken = await GetAccessTokenAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new CreateInstantCriminalCheckResult
            {
                Error = "Checkr Trust access token could not be acquired."
            };
        }

        var requestBody = new Dictionary<string, object?>
        {
            ["first_name"] = input.FirstName,
            ["last_name"] = input.LastName
        };

        // Checkr Trust's public instant-criminal docs explicitly document first_name,
        // last_name, and optional dob. Additional collected fields stay on our contract
        // until their profile/check schema is confirmed for this integration.
        if (!string.IsNullOrWhiteSpace(input.Birthdate))
            requestBody["dob"] = NormalizeBirthdate(input.Birthdate);

        try
        {
            logger.LogInformation(
                "Creating Checkr Trust instant criminal check for {FirstName} {LastName}.",
                input.FirstName,
                input.LastName);

            var (response, responseBody) = await SendCreateCheckAsync(accessToken, requestBody, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                logger.LogInformation("Checkr Trust access token was rejected. Refreshing token and retrying once.");
                accessToken = await GetAccessTokenAsync(cancellationToken, forceRefresh: true);

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    return new CreateInstantCriminalCheckResult
                    {
                        Error = "Checkr Trust access token could not be refreshed."
                    };
                }

                (response, responseBody) = await SendCreateCheckAsync(accessToken, requestBody, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Checkr Trust instant criminal check failed with status {StatusCode}. Response: {ResponseBody}",
                    (int)response.StatusCode,
                    responseBody);

                return new CreateInstantCriminalCheckResult
                {
                    Error = $"Checkr Trust instant criminal check failed with status {(int)response.StatusCode}: {Truncate(responseBody)}"
                };
            }

            using var document = JsonDocument.Parse(responseBody);
            var resultCount = TryGetResultCount(document.RootElement);

            return new CreateInstantCriminalCheckResult
            {
                Success = true,
                CheckId = TryGetString(document.RootElement, "id"),
                ResultCount = resultCount,
                HasPossibleMatches = resultCount.HasValue ? resultCount.Value > 0 : null
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Checkr Trust instant criminal check threw an exception.");

            return new CreateInstantCriminalCheckResult
            {
                Error = $"Checkr Trust instant criminal check threw an exception: {exception.Message}"
            };
        }
    }

    private async Task<string?> GetAccessTokenAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        var cacheKey = GetTokenCacheKey();

        if (!forceRefresh
            && memoryCache.TryGetValue<CheckrTrustAccessToken>(cacheKey, out var cachedToken)
            && !string.IsNullOrWhiteSpace(cachedToken?.AccessToken))
        {
            return cachedToken.AccessToken;
        }

        var requestBody = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/accounts/token"));
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, SerializerOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Checkr Trust token request failed with status {StatusCode}. Response: {ResponseBody}",
                    (int)response.StatusCode,
                    responseBody);
                return null;
            }

            using var document = JsonDocument.Parse(responseBody);
            var accessToken = TryGetString(document.RootElement, "access_token");
            var expiresInSeconds = TryGetInt32(document.RootElement, "expires_in");

            if (string.IsNullOrWhiteSpace(accessToken))
                return null;

            CacheAccessToken(cacheKey, accessToken, expiresInSeconds);
            return accessToken;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Checkr Trust token request threw an exception.");
            return null;
        }
    }

    private async Task<(HttpResponseMessage Response, string ResponseBody)> SendCreateCheckAsync(
        string accessToken,
        Dictionary<string, object?> requestBody,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/checks"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, SerializerOptions),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response, responseBody);
    }

    private static int? TryGetResultCount(JsonElement root)
    {
        if (root.TryGetProperty("results", out var resultsElement)
            && resultsElement.ValueKind == JsonValueKind.Array)
        {
            return resultsElement.GetArrayLength();
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var valueElement)
            ? valueElement.GetString()?.Trim()
            : null;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement))
            return null;

        if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetInt32(out var intValue))
            return intValue;

        return valueElement.ValueKind == JsonValueKind.String
            && int.TryParse(valueElement.GetString(), out var parsedValue)
            ? parsedValue
            : null;
    }

    private static string NormalizeBirthdate(string birthdate)
    {
        var digitsOnly = new string(birthdate.Where(char.IsDigit).ToArray());
        return digitsOnly.Length == 8 ? digitsOnly : birthdate.Trim();
    }

    private static string Truncate(string value)
    {
        const int maxLength = 300;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private void CacheAccessToken(string cacheKey, string accessToken, int? expiresInSeconds)
    {
        var ttl = expiresInSeconds.HasValue && expiresInSeconds.Value > 0
            ? TimeSpan.FromSeconds(expiresInSeconds.Value)
            : TimeSpan.FromHours(1);

        var cacheLifetime = ttl > TokenRefreshBuffer
            ? ttl - TokenRefreshBuffer
            : TimeSpan.FromMinutes(1);

        memoryCache.Set(
            cacheKey,
            new CheckrTrustAccessToken
            {
                AccessToken = accessToken
            },
            cacheLifetime);
    }

    private string GetTokenCacheKey()
    {
        return $"checkrtrust:access-token:{options.ClientId}";
    }

    private Uri BuildUri(string relativeOrAbsolutePath)
    {
        if (Uri.TryCreate(relativeOrAbsolutePath, UriKind.Absolute, out var absoluteUri)
            && absoluteUri.Scheme is "http" or "https")
            return absoluteUri;

        var normalizedPath = relativeOrAbsolutePath.TrimStart('/');
        return new Uri(apiBaseUri, normalizedPath);
    }

    private static Uri CreateApiBaseUri(string? apiBaseUrl, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return DefaultApiBaseUri;

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri))
        {
            logger.LogWarning(
                "CheckrTrust ApiBaseUrl '{ApiBaseUrl}' is invalid. Falling back to {FallbackApiBaseUrl}.",
                apiBaseUrl,
                DefaultApiBaseUri);
            return DefaultApiBaseUri;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            logger.LogWarning(
                "CheckrTrust ApiBaseUrl '{ApiBaseUrl}' used unsupported scheme '{Scheme}'. Falling back to {FallbackApiBaseUrl}.",
                apiBaseUrl,
                uri.Scheme,
                DefaultApiBaseUri);
            return DefaultApiBaseUri;
        }

        return uri;
    }

    private sealed class CheckrTrustAccessToken
    {
        public string AccessToken { get; init; } = string.Empty;
    }
}
