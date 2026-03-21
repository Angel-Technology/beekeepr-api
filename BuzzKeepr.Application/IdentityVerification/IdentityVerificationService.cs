using System.Text.Json;
using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Application.IdentityVerification;

public sealed class IdentityVerificationService(
    IIdentityVerificationRepository identityVerificationRepository,
    IPersonaClient personaClient,
    ILogger<IdentityVerificationService> logger) : IIdentityVerificationService
{
    private static readonly HashSet<IdentityVerificationStatus> RetryableStatuses =
    [
        IdentityVerificationStatus.Declined,
        IdentityVerificationStatus.Expired,
        IdentityVerificationStatus.Failed
    ];

    public async Task<StartPersonaInquiryResult> StartPersonaInquiryAsync(Guid userId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting Persona inquiry for user {UserId}.", userId);

        var user = await identityVerificationRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            logger.LogWarning("Persona inquiry requested for missing user {UserId}.", userId);
            return new StartPersonaInquiryResult
            {
                Error = "Authenticated user was not found."
            };
        }

        var shouldCreateNewInquiry = string.IsNullOrWhiteSpace(user.PersonaInquiryId)
            || user.PersonaInquiryStatus is null
            || RetryableStatuses.Contains(user.IdentityVerificationStatus);

        if (!shouldCreateNewInquiry)
        {
            logger.LogInformation(
                "Reusing Persona inquiry {InquiryId} for user {UserId} with status {PersonaInquiryStatus}.",
                user.PersonaInquiryId,
                user.Id,
                user.PersonaInquiryStatus);

            return new StartPersonaInquiryResult
            {
                Success = true,
                CreatedNewInquiry = false,
                InquiryId = user.PersonaInquiryId,
                IdentityVerificationStatus = user.IdentityVerificationStatus,
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
            logger.LogWarning(
                "Persona inquiry creation failed for user {UserId}. Error: {Error}",
                user.Id,
                createInquiryResult.Error);

            return new StartPersonaInquiryResult
            {
                Error = createInquiryResult.Error ?? "Persona inquiry creation failed.",
                IdentityVerificationStatus = user.IdentityVerificationStatus,
                PersonaInquiryStatus = user.PersonaInquiryStatus
            };
        }

        var inquiryStatus = MapPersonaInquiryStatus(createInquiryResult.InquiryStatus);
        user.PersonaInquiryId = createInquiryResult.InquiryId;
        user.PersonaInquiryStatus = inquiryStatus;
        user.IdentityVerificationStatus = MapIdentityVerificationStatus(inquiryStatus);

        await identityVerificationRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created Persona inquiry {InquiryId} for user {UserId} with status {PersonaInquiryStatus}.",
            user.PersonaInquiryId,
            user.Id,
            user.PersonaInquiryStatus);

        return new StartPersonaInquiryResult
        {
            Success = true,
            CreatedNewInquiry = true,
            InquiryId = user.PersonaInquiryId,
            IdentityVerificationStatus = user.IdentityVerificationStatus,
            PersonaInquiryStatus = user.PersonaInquiryStatus
        };
    }

    public async Task ProcessPersonaWebhookAsync(string rawRequestBody, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(rawRequestBody);

        if (!TryExtractInquiryPayload(document.RootElement, out var inquiryId, out var inquiryStatus))
        {
            logger.LogWarning("Persona webhook payload did not contain an inquiry id and status.");
            return;
        }

        var user = await identityVerificationRepository.GetByPersonaInquiryIdAsync(inquiryId, cancellationToken);

        if (user is null)
        {
            logger.LogWarning("Persona webhook received for unknown inquiry {InquiryId}.", inquiryId);
            return;
        }

        user.PersonaInquiryStatus = inquiryStatus;
        user.IdentityVerificationStatus = MapIdentityVerificationStatus(inquiryStatus);

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

        logger.LogInformation(
            "Processed Persona webhook for inquiry {InquiryId}. User {UserId} now has identity status {IdentityVerificationStatus}.",
            inquiryId,
            user.Id,
            user.IdentityVerificationStatus);
    }

    private static bool TryExtractInquiryPayload(
        JsonElement root,
        out string inquiryId,
        out PersonaInquiryStatus inquiryStatus)
    {
        inquiryId = string.Empty;
        inquiryStatus = default;

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
        var rawInquiryStatus = inquiryStatusElement.GetString()?.Trim();

        if (string.IsNullOrWhiteSpace(inquiryId) || string.IsNullOrWhiteSpace(rawInquiryStatus))
            return false;

        inquiryStatus = MapPersonaInquiryStatus(rawInquiryStatus);
        return true;
    }

    private static PersonaInquiryStatus MapPersonaInquiryStatus(string inquiryStatus)
    {
        return inquiryStatus.Trim().ToLowerInvariant() switch
        {
            "created" => PersonaInquiryStatus.Created,
            "pending" => PersonaInquiryStatus.Pending,
            "completed" => PersonaInquiryStatus.Completed,
            "needs-review" => PersonaInquiryStatus.NeedsReview,
            "approved" => PersonaInquiryStatus.Approved,
            "declined" => PersonaInquiryStatus.Declined,
            "failed" => PersonaInquiryStatus.Failed,
            "expired" => PersonaInquiryStatus.Expired,
            _ => PersonaInquiryStatus.Pending
        };
    }

    private static IdentityVerificationStatus MapIdentityVerificationStatus(PersonaInquiryStatus status)
    {
        return status switch
        {
            PersonaInquiryStatus.Created => IdentityVerificationStatus.Created,
            PersonaInquiryStatus.Pending => IdentityVerificationStatus.Pending,
            PersonaInquiryStatus.Completed => IdentityVerificationStatus.Completed,
            PersonaInquiryStatus.NeedsReview => IdentityVerificationStatus.NeedsReview,
            PersonaInquiryStatus.Approved => IdentityVerificationStatus.Approved,
            PersonaInquiryStatus.Declined => IdentityVerificationStatus.Declined,
            PersonaInquiryStatus.Failed => IdentityVerificationStatus.Failed,
            PersonaInquiryStatus.Expired => IdentityVerificationStatus.Expired,
            _ => IdentityVerificationStatus.Pending
        };
    }
}
