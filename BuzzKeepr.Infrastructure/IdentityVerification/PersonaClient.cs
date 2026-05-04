using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure.IdentityVerification;

public sealed class PersonaClient(
    HttpClient httpClient,
    IOptions<PersonaOptions> personaOptions,
    ILogger<PersonaClient> logger) : IPersonaClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri DefaultApiBaseUri = new("https://api.withpersona.com");
    private readonly PersonaOptions options = personaOptions.Value;
    private readonly Uri apiBaseUri = CreateApiBaseUri(personaOptions.Value.ApiBaseUrl, logger);

    public async Task<CreatePersonaInquiryResult> CreateInquiryAsync(
        CreatePersonaInquiryInput input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return new CreatePersonaInquiryResult
            {
                Error = "Persona:ApiKey is not configured."
            };
        }

        if (string.IsNullOrWhiteSpace(options.InquiryTemplateId))
        {
            return new CreatePersonaInquiryResult
            {
                Error = "Persona:InquiryTemplateId is not configured."
            };
        }

        var attributes = new Dictionary<string, object?>
        {
            ["inquiry-template-id"] = options.InquiryTemplateId
        };

        if (!string.IsNullOrWhiteSpace(options.ThemeSetId))
        {
            attributes["theme-set-id"] = options.ThemeSetId;
        }

        var requestBody = new Dictionary<string, object?>
        {
            ["data"] = new Dictionary<string, object?>
            {
                ["type"] = "inquiry",
                ["attributes"] = attributes
            },
            ["meta"] = new Dictionary<string, object?>
            {
                ["auto-create-account-reference-id"] = input.ReferenceId
            }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/api/v1/inquiries"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, SerializerOptions),
                Encoding.UTF8,
                "application/json");

            logger.LogInformation(
                "Creating Persona inquiry for reference id {ReferenceId} using template {TemplateId}.",
                input.ReferenceId,
                options.InquiryTemplateId);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Persona inquiry creation failed with status {StatusCode}. Response: {ResponseBody}",
                    (int)response.StatusCode,
                    responseBody);

                return new CreatePersonaInquiryResult
                {
                    Error = $"Persona inquiry creation failed with status {(int)response.StatusCode}: {Truncate(responseBody)}"
                };
            }

            using var document = JsonDocument.Parse(responseBody);

            if (!document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("id", out var idElement)
                || !data.TryGetProperty("attributes", out var attributesElement)
                || !attributesElement.TryGetProperty("status", out var statusElement))
            {
                logger.LogWarning(
                    "Persona inquiry creation response was missing expected fields. Response: {ResponseBody}",
                    responseBody);

                return new CreatePersonaInquiryResult
                {
                    Error = $"Persona inquiry creation response was missing expected fields: {Truncate(responseBody)}"
                };
            }

            return new CreatePersonaInquiryResult
            {
                Success = true,
                InquiryId = idElement.GetString(),
                InquiryStatus = statusElement.GetString()?.Trim().ToLowerInvariant()
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Persona inquiry creation threw an exception.");

            return new CreatePersonaInquiryResult
            {
                Error = $"Persona inquiry creation threw an exception: {exception.Message}"
            };
        }
    }

    public async Task<CreatePersonaSessionTokenResult> CreateInquirySessionTokenAsync(
        string inquiryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return new CreatePersonaSessionTokenResult
            {
                Error = "Persona:ApiKey is not configured."
            };
        }

        if (string.IsNullOrWhiteSpace(inquiryId))
        {
            return new CreatePersonaSessionTokenResult
            {
                Error = "Inquiry id is required to mint a session token."
            };
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildUri($"/api/v1/inquiries/{Uri.EscapeDataString(inquiryId)}/resume"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Persona inquiry resume failed for inquiry {InquiryId} with status {StatusCode}. Response: {ResponseBody}",
                    inquiryId,
                    (int)response.StatusCode,
                    responseBody);

                return new CreatePersonaSessionTokenResult
                {
                    Error = $"Persona inquiry resume failed with status {(int)response.StatusCode}: {Truncate(responseBody)}"
                };
            }

            using var document = JsonDocument.Parse(responseBody);

            // Persona's `/resume` endpoint returns the session token on
            // `meta.session-token`. Some past response shapes have used the
            // snake_case spelling; check both to be robust.
            if (!document.RootElement.TryGetProperty("meta", out var meta))
            {
                logger.LogWarning(
                    "Persona inquiry resume response had no meta object for inquiry {InquiryId}. Response: {ResponseBody}",
                    inquiryId,
                    responseBody);

                return new CreatePersonaSessionTokenResult
                {
                    Error = $"Persona inquiry resume response was missing the meta object: {Truncate(responseBody)}"
                };
            }

            var sessionToken = GetString(meta, "session-token") ?? GetString(meta, "session_token");

            if (string.IsNullOrWhiteSpace(sessionToken))
            {
                logger.LogWarning(
                    "Persona inquiry resume response had no session token for inquiry {InquiryId}. Response: {ResponseBody}",
                    inquiryId,
                    responseBody);

                return new CreatePersonaSessionTokenResult
                {
                    Error = $"Persona inquiry resume response was missing the session token: {Truncate(responseBody)}"
                };
            }

            return new CreatePersonaSessionTokenResult
            {
                Success = true,
                SessionToken = sessionToken
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Persona inquiry resume threw an exception for inquiry {InquiryId}.", inquiryId);

            return new CreatePersonaSessionTokenResult
            {
                Error = $"Persona inquiry resume threw an exception: {exception.Message}"
            };
        }
    }

    public async Task<PersonaGovernmentIdDataResult> GetGovernmentIdDataAsync(
        string inquiryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(inquiryId))
            return new PersonaGovernmentIdDataResult();

        using var inquiryRequest = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri($"/api/v1/inquiries/{Uri.EscapeDataString(inquiryId)}?include=verifications"));
        inquiryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var inquiryResponse = await httpClient.SendAsync(inquiryRequest, cancellationToken);

        if (!inquiryResponse.IsSuccessStatusCode)
            return new PersonaGovernmentIdDataResult();

        var inquiryResponseBody = await inquiryResponse.Content.ReadAsStringAsync(cancellationToken);
        using var inquiryDocument = JsonDocument.Parse(inquiryResponseBody);

        var governmentIdVerificationId = FindPassedGovernmentIdVerificationId(inquiryDocument.RootElement);

        if (string.IsNullOrWhiteSpace(governmentIdVerificationId))
            return new PersonaGovernmentIdDataResult();

        using var governmentIdRequest = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri($"/api/v1/verifications/government-id/{Uri.EscapeDataString(governmentIdVerificationId)}"));
        governmentIdRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var governmentIdResponse = await httpClient.SendAsync(governmentIdRequest, cancellationToken);

        if (!governmentIdResponse.IsSuccessStatusCode)
            return new PersonaGovernmentIdDataResult();

        var governmentIdResponseBody = await governmentIdResponse.Content.ReadAsStringAsync(cancellationToken);
        using var governmentIdDocument = JsonDocument.Parse(governmentIdResponseBody);

        if (!governmentIdDocument.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("attributes", out var attributes))
        {
            return new PersonaGovernmentIdDataResult();
        }

        return new PersonaGovernmentIdDataResult
        {
            Success = true,
            FirstName = GetString(attributes, "name-first"),
            MiddleName = GetString(attributes, "name-middle"),
            LastName = GetString(attributes, "name-last"),
            Birthdate = GetString(attributes, "birthdate"),
            LicenseState = GetString(attributes, "issuing-subdivision")
        };
    }

    private static string? FindPassedGovernmentIdVerificationId(JsonElement root)
    {
        if (!root.TryGetProperty("included", out var included)
            || included.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in included.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeElement)
                || !string.Equals(typeElement.GetString(), "verification/government-id", StringComparison.Ordinal))
            {
                continue;
            }

            if (!item.TryGetProperty("attributes", out var attributes))
                continue;

            var status = GetString(attributes, "status");

            if (!string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!item.TryGetProperty("id", out var idElement))
                continue;

            return idElement.GetString();
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var valueElement)
            ? valueElement.GetString()?.Trim()
            : null;
    }

    private static string Truncate(string value)
    {
        const int maxLength = 300;
        return value.Length <= maxLength ? value : value[..maxLength];
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
                "Persona ApiBaseUrl '{ApiBaseUrl}' is invalid. Falling back to {FallbackApiBaseUrl}.",
                apiBaseUrl,
                DefaultApiBaseUri);
            return DefaultApiBaseUri;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            logger.LogWarning(
                "Persona ApiBaseUrl '{ApiBaseUrl}' used unsupported scheme '{Scheme}'. Falling back to {FallbackApiBaseUrl}.",
                apiBaseUrl,
                uri.Scheme,
                DefaultApiBaseUri);
            return DefaultApiBaseUri;
        }

        return uri;
    }
}
