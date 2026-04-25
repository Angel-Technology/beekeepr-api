using System.Text.Json;
using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Application.IdentityVerification;

public sealed class IdentityVerificationService(
    IIdentityVerificationRepository identityVerificationRepository,
    IPersonaClient personaClient,
    ICheckrTrustClient checkrTrustClient,
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

        if (!TryExtractInquiryPayload(document.RootElement, out var inquiryId, out var inquiryStatus, out var inquiryUpdatedAtUtc))
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

        if (inquiryUpdatedAtUtc.HasValue
            && user.PersonaInquiryUpdatedAtUtc.HasValue
            && inquiryUpdatedAtUtc.Value <= user.PersonaInquiryUpdatedAtUtc.Value)
        {
            logger.LogInformation(
                "Skipping stale Persona webhook for inquiry {InquiryId}: event @{IncomingUpdatedAt:o} is not newer than stored @{StoredUpdatedAt:o}.",
                inquiryId,
                inquiryUpdatedAtUtc.Value,
                user.PersonaInquiryUpdatedAtUtc.Value);
            return;
        }

        var newIdentityStatus = MapIdentityVerificationStatus(inquiryStatus);
        var verifiedDataAlreadyPresent = !string.IsNullOrWhiteSpace(user.VerifiedFirstName);

        user.PersonaInquiryStatus = inquiryStatus;
        user.IdentityVerificationStatus = newIdentityStatus;

        if (inquiryUpdatedAtUtc.HasValue)
            user.PersonaInquiryUpdatedAtUtc = inquiryUpdatedAtUtc.Value;

        var shouldFetchVerifiedData = newIdentityStatus
                is IdentityVerificationStatus.Approved
                or IdentityVerificationStatus.Completed
            && !verifiedDataAlreadyPresent;

        if (shouldFetchVerifiedData)
        {
            var governmentIdData = await personaClient.GetGovernmentIdDataAsync(inquiryId, cancellationToken);

            if (governmentIdData.Success)
            {
                user.VerifiedFirstName = governmentIdData.FirstName;
                user.VerifiedMiddleName = governmentIdData.MiddleName;
                user.VerifiedLastName = governmentIdData.LastName;
                user.VerifiedBirthdate = governmentIdData.Birthdate;
                user.VerifiedLicenseState = NormalizeStateCode(governmentIdData.LicenseState);
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

    public async Task<CreateInstantCriminalCheckResult> CreateInstantCriminalCheckAsync(
        Guid userId,
        StartInstantCriminalCheckInput input,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting Checkr Trust instant criminal check for user {UserId}.", userId);

        var user = await identityVerificationRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            logger.LogWarning("Checkr Trust instant criminal check requested for missing user {UserId}.", userId);
            return new CreateInstantCriminalCheckResult
            {
                Error = "Authenticated user was not found."
            };
        }

        var hasExistingProfile = !string.IsNullOrWhiteSpace(user.CheckrProfileId);
        var trimmedPhone = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
        var effectivePhone = trimmedPhone ?? user.PhoneNumber;

        if (!hasExistingProfile
            && (string.IsNullOrWhiteSpace(user.VerifiedFirstName) || string.IsNullOrWhiteSpace(user.VerifiedLastName)))
        {
            logger.LogWarning(
                "Checkr Trust instant criminal check requested for user {UserId} without verified identity.",
                userId);

            return new CreateInstantCriminalCheckResult
            {
                Error = "Identity verification must be completed before running a background check."
            };
        }

        var clientInput = hasExistingProfile
            ? new CreateInstantCriminalCheckInput
            {
                ProfileId = user.CheckrProfileId
            }
            : new CreateInstantCriminalCheckInput
            {
                FirstName = user.VerifiedFirstName!,
                MiddleName = user.VerifiedMiddleName,
                LastName = user.VerifiedLastName!,
                Birthdate = user.VerifiedBirthdate,
                State = user.VerifiedLicenseState,
                PhoneNumber = effectivePhone
            };

        var result = await checkrTrustClient.CreateInstantCriminalCheckAsync(clientInput, cancellationToken);

        if (!result.Success)
            return result;

        if (!string.IsNullOrWhiteSpace(result.ProfileId))
            user.CheckrProfileId = result.ProfileId;

        if (!string.IsNullOrWhiteSpace(result.CheckId))
            user.CheckrLastCheckId = result.CheckId;

        user.CheckrLastCheckAtUtc = DateTime.UtcNow;
        user.CheckrLastCheckHasPossibleMatches = result.HasPossibleMatches;

        if (!string.IsNullOrWhiteSpace(trimmedPhone))
            user.PhoneNumber = trimmedPhone;

        await identityVerificationRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Checkr Trust instant criminal check completed for user {UserId}. CheckId={CheckId} ProfileId={ProfileId} HasPossibleMatches={HasPossibleMatches}",
            user.Id,
            result.CheckId,
            result.ProfileId,
            result.HasPossibleMatches);

        return result;
    }

    private static bool TryExtractInquiryPayload(
        JsonElement root,
        out string inquiryId,
        out PersonaInquiryStatus inquiryStatus,
        out DateTime? inquiryUpdatedAtUtc)
    {
        inquiryId = string.Empty;
        inquiryStatus = default;
        inquiryUpdatedAtUtc = null;

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
        inquiryUpdatedAtUtc = TryReadInquiryUpdatedAt(inquiryAttributes, eventAttributes);
        return true;
    }

    private static DateTime? TryReadInquiryUpdatedAt(JsonElement inquiryAttributes, JsonElement eventAttributes)
    {
        // Persona's inquiry attributes carry an "updated-at" ISO8601 string; fall back to the event's
        // own "created-at" if for some reason the inquiry timestamp isn't present.
        foreach (var propertyName in new[] { "updated-at", "updated_at" })
        {
            if (inquiryAttributes.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                && DateTime.TryParse(value.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }

        foreach (var propertyName in new[] { "created-at", "created_at" })
        {
            if (eventAttributes.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                && DateTime.TryParse(value.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }

        return null;
    }

    private static string? NormalizeStateCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        return trimmed.Length == 2 ? trimmed.ToUpperInvariant() : trimmed;
    }

    private static PersonaInquiryStatus MapPersonaInquiryStatus(string inquiryStatus)
    {
        return inquiryStatus.Trim().ToLowerInvariant() switch
        {
            "created" => PersonaInquiryStatus.Created,
            "started" => PersonaInquiryStatus.Pending,
            "pending" => PersonaInquiryStatus.Pending,
            "completed" => PersonaInquiryStatus.Completed,
            "needs-review" => PersonaInquiryStatus.NeedsReview,
            "needs_review" => PersonaInquiryStatus.NeedsReview,
            "marked-for-review" => PersonaInquiryStatus.NeedsReview,
            "marked_for_review" => PersonaInquiryStatus.NeedsReview,
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
