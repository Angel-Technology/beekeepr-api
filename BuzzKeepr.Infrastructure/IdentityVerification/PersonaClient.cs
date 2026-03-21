using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure.IdentityVerification;

public sealed class PersonaClient(
    HttpClient httpClient,
    IOptions<PersonaOptions> personaOptions) : IPersonaClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly PersonaOptions options = personaOptions.Value;

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

        var requestBody = new Dictionary<string, object?>
        {
            ["data"] = new Dictionary<string, object?>
            {
                ["type"] = "inquiry",
                ["attributes"] = new Dictionary<string, object?>
                {
                    ["inquiry-template-id"] = options.InquiryTemplateId
                }
            },
            ["meta"] = new Dictionary<string, object?>
            {
                ["auto-create-account-reference-id"] = input.ReferenceId
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inquiries");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new CreatePersonaInquiryResult
            {
                Error = $"Persona inquiry creation failed with status {(int)response.StatusCode}."
            };
        }

        using var document = JsonDocument.Parse(responseBody);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("id", out var idElement)
            || !data.TryGetProperty("attributes", out var attributes)
            || !attributes.TryGetProperty("status", out var statusElement))
        {
            return new CreatePersonaInquiryResult
            {
                Error = "Persona inquiry creation response was missing expected fields."
            };
        }

        return new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = idElement.GetString(),
            InquiryStatus = statusElement.GetString()?.Trim().ToLowerInvariant()
        };
    }

    public async Task<PersonaGovernmentIdDataResult> GetGovernmentIdDataAsync(
        string inquiryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(inquiryId))
            return new PersonaGovernmentIdDataResult();

        using var inquiryRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/inquiries/{Uri.EscapeDataString(inquiryId)}?include=verifications");
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
            $"/api/v1/verifications/government-id/{Uri.EscapeDataString(governmentIdVerificationId)}");
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

        var licenseNumber = GetString(attributes, "identification-number");
        return new PersonaGovernmentIdDataResult
        {
            Success = true,
            FirstName = GetString(attributes, "name-first"),
            LastName = GetString(attributes, "name-last"),
            Birthdate = GetString(attributes, "birthdate"),
            AddressStreet1 = GetString(attributes, "address-street-1"),
            AddressStreet2 = GetString(attributes, "address-street-2"),
            AddressCity = GetString(attributes, "address-city"),
            AddressSubdivision = GetString(attributes, "address-subdivision"),
            AddressPostalCode = GetString(attributes, "address-postal-code"),
            CountryCode = GetString(attributes, "country-code"),
            LicenseNumberLast4 = GetLast4(licenseNumber),
            LicenseExpirationDate = GetString(attributes, "expiration-date")
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

    private static string? GetLast4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Length <= 4 ? value : value[^4..];
    }

}
