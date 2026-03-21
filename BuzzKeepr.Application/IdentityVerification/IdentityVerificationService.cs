using System.Text.Json;
using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.IdentityVerification;

public sealed class IdentityVerificationService(
    IIdentityVerificationRepository identityVerificationRepository,
    IPersonaClient personaClient) : IIdentityVerificationService
{
    private static readonly HashSet<IdentityVerificationStatus> RetryableStatuses =
    [
        IdentityVerificationStatus.Declined,
        IdentityVerificationStatus.Expired,
        IdentityVerificationStatus.Failed
    ];

    public async Task<StartPersonaInquiryResult> StartPersonaInquiryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await identityVerificationRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return new StartPersonaInquiryResult
            {
                Error = "Authenticated user was not found."
            };
        }

        var shouldCreateNewInquiry = string.IsNullOrWhiteSpace(user.PersonaInquiryId)
            || string.IsNullOrWhiteSpace(user.PersonaInquiryStatus)
            || RetryableStatuses.Contains(user.IdentityVerificationStatus);

        if (!shouldCreateNewInquiry)
        {
            return new StartPersonaInquiryResult
            {
                Success = true,
                CreatedNewInquiry = false,
                InquiryId = user.PersonaInquiryId,
                IdentityVerificationStatus = ToApiStatus(user.IdentityVerificationStatus),
                PersonaInquiryStatus = user.PersonaInquiryStatus
            };
        }

        var createInquiryResult = await personaClient.CreateInquiryAsync(
            new CreatePersonaInquiryInput
            {
                ReferenceId = user.Id.ToString(),
                DisplayName = user.DisplayName,
                EmailAddress = user.Email
            },
            cancellationToken);

        if (!createInquiryResult.Success
            || string.IsNullOrWhiteSpace(createInquiryResult.InquiryId)
            || string.IsNullOrWhiteSpace(createInquiryResult.InquiryStatus))
        {
            return new StartPersonaInquiryResult
            {
                Error = createInquiryResult.Error ?? "Persona inquiry creation failed.",
                IdentityVerificationStatus = ToApiStatus(user.IdentityVerificationStatus),
                PersonaInquiryStatus = user.PersonaInquiryStatus
            };
        }

        var inquiryStatus = createInquiryResult.InquiryStatus.Trim().ToLowerInvariant();
        user.PersonaInquiryId = createInquiryResult.InquiryId;
        user.PersonaInquiryStatus = inquiryStatus;
        user.IdentityVerificationStatus = MapInquiryStatus(inquiryStatus);

        await identityVerificationRepository.SaveChangesAsync(cancellationToken);

        return new StartPersonaInquiryResult
        {
            Success = true,
            CreatedNewInquiry = true,
            InquiryId = user.PersonaInquiryId,
            IdentityVerificationStatus = ToApiStatus(user.IdentityVerificationStatus),
            PersonaInquiryStatus = user.PersonaInquiryStatus
        };
    }

    public async Task ProcessPersonaWebhookAsync(string rawRequestBody, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(rawRequestBody);

        if (!TryExtractInquiryPayload(document.RootElement, out var inquiryId, out var inquiryStatus))
            return;

        var user = await identityVerificationRepository.GetByPersonaInquiryIdAsync(inquiryId, cancellationToken);

        if (user is null)
            return;

        user.PersonaInquiryStatus = inquiryStatus;
        user.IdentityVerificationStatus = MapInquiryStatus(inquiryStatus);

        if (user.IdentityVerificationStatus is IdentityVerificationStatus.Approved
            or IdentityVerificationStatus.Completed)
        {
            var governmentIdData = await personaClient.GetGovernmentIdDataAsync(inquiryId, cancellationToken);

            if (governmentIdData.Success)
            {
                user.VerifiedFirstName = governmentIdData.FirstName;
                user.VerifiedLastName = governmentIdData.LastName;
                user.VerifiedBirthdate = governmentIdData.Birthdate;
                user.VerifiedAddressStreet1 = governmentIdData.AddressStreet1;
                user.VerifiedAddressStreet2 = governmentIdData.AddressStreet2;
                user.VerifiedAddressCity = governmentIdData.AddressCity;
                user.VerifiedAddressSubdivision = governmentIdData.AddressSubdivision;
                user.VerifiedAddressPostalCode = governmentIdData.AddressPostalCode;
                user.VerifiedCountryCode = governmentIdData.CountryCode;
                user.VerifiedLicenseLast4 = governmentIdData.LicenseNumberLast4;
                user.VerifiedLicenseExpirationDate = governmentIdData.LicenseExpirationDate;
                user.PersonaVerifiedAtUtc = DateTime.UtcNow;
            }
        }

        await identityVerificationRepository.SaveChangesAsync(cancellationToken);
    }

    private static bool TryExtractInquiryPayload(
        JsonElement root,
        out string inquiryId,
        out string inquiryStatus)
    {
        inquiryId = string.Empty;
        inquiryStatus = string.Empty;

        if (!root.TryGetProperty("data", out var eventData)
            || !eventData.TryGetProperty("attributes", out var eventAttributes)
            || !eventAttributes.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("data", out var payloadData))
        {
            return false;
        }

        if (!payloadData.TryGetProperty("type", out var payloadType)
            || !string.Equals(payloadType.GetString(), "inquiry", StringComparison.Ordinal))
        {
            return false;
        }

        if (!payloadData.TryGetProperty("id", out var inquiryIdElement)
            || !payloadData.TryGetProperty("attributes", out var inquiryAttributes)
            || !inquiryAttributes.TryGetProperty("status", out var inquiryStatusElement))
        {
            return false;
        }

        inquiryId = inquiryIdElement.GetString()?.Trim() ?? string.Empty;
        inquiryStatus = inquiryStatusElement.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;

        return !string.IsNullOrWhiteSpace(inquiryId) && !string.IsNullOrWhiteSpace(inquiryStatus);
    }

    private static IdentityVerificationStatus MapInquiryStatus(string inquiryStatus)
    {
        return inquiryStatus switch
        {
            "created" => IdentityVerificationStatus.Created,
            "pending" => IdentityVerificationStatus.Pending,
            "completed" => IdentityVerificationStatus.Completed,
            "needs-review" => IdentityVerificationStatus.NeedsReview,
            "approved" => IdentityVerificationStatus.Approved,
            "declined" => IdentityVerificationStatus.Declined,
            "failed" => IdentityVerificationStatus.Failed,
            "expired" => IdentityVerificationStatus.Expired,
            _ => IdentityVerificationStatus.Pending
        };
    }

    public static string ToApiStatus(IdentityVerificationStatus status)
    {
        return status switch
        {
            IdentityVerificationStatus.NotStarted => "not_started",
            IdentityVerificationStatus.Created => "created",
            IdentityVerificationStatus.Pending => "pending",
            IdentityVerificationStatus.Completed => "completed",
            IdentityVerificationStatus.NeedsReview => "needs_review",
            IdentityVerificationStatus.Approved => "approved",
            IdentityVerificationStatus.Declined => "declined",
            IdentityVerificationStatus.Failed => "failed",
            IdentityVerificationStatus.Expired => "expired",
            _ => "pending"
        };
    }
}
