using System.Text.RegularExpressions;
using BuzzKeepr.Application.Billing.Models;
using BuzzKeepr.Application.Users.Models;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Application.Users;

public sealed class UserService(
    IUserRepository userRepository,
    IWelcomeEmailSender welcomeEmailSender,
    ILogger<UserService> logger) : IUserService
{
    private const int NicknameMaxLength = 50;

    // Two chars is enough for prefix lookups (e.g. "sa") while still blocking single-char queries
    // that would behave like a full enumeration of the user table.
    private const int SearchMinQueryLength = 2;

    private const int HandleMinLength = 3;
    private const int HandleMaxLength = 20;

    private static readonly Regex HandleFormat = new($"^[a-zA-Z0-9_]{{{HandleMinLength},{HandleMaxLength}}}$", RegexOptions.Compiled);
    private static readonly Regex HandleAllowedChars = new("^[a-zA-Z0-9_]+$", RegexOptions.Compiled);


    public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);

        return user is null ? null : MapUser(user);
    }

    public async Task<CreateUserResult> CreateAsync(CreateUserInput input, CancellationToken cancellationToken)
    {
        var normalizedEmail = input.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return new CreateUserResult
            {
                EmailRequired = true
            };
        }

        var emailAlreadyExists = await userRepository.EmailExistsAsync(normalizedEmail, cancellationToken);

        if (emailAlreadyExists)
        {
            return new CreateUserResult
            {
                EmailAlreadyExists = true
            };
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            EmailVerified = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(input.DisplayName))
        {
            user.EnsureProfile().DisplayName = input.DisplayName.Trim();
        }

        await userRepository.AddAsync(user, cancellationToken);

        try
        {
            await welcomeEmailSender.SendWelcomeAsync(user.Email, user.Profile?.DisplayName, cancellationToken);
            user.WelcomeEmailSentAtUtc = DateTime.UtcNow;
            await userRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Welcome email failed to send for user {UserId}; sweeper will retry.",
                user.Id);
        }

        return new CreateUserResult
        {
            Success = true,
            User = MapUser(user)
        };
    }

    public async Task<AcceptTermsResult> AcceptTermsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdForUpdateAsync(userId, cancellationToken);

        if (user is null)
        {
            return new AcceptTermsResult
            {
                UserNotFound = true
            };
        }

        user.TermsAcceptedAtUtc = DateTime.UtcNow;

        await userRepository.SaveChangesAsync(cancellationToken);

        return new AcceptTermsResult
        {
            Success = true,
            User = MapUser(user)
        };
    }

    public async Task<UpdateProfileResult> UpdateProfileAsync(
        Guid userId,
        UpdateProfileInput input,
        CancellationToken cancellationToken)
    {
        string? normalizedNickname = null;
        if (input.Nickname is not null)
        {
            var trimmed = input.Nickname.Trim();
            if (trimmed.Length == 0)
            {
                normalizedNickname = null;
            }
            else if (trimmed.Length > NicknameMaxLength)
            {
                return new UpdateProfileResult { NicknameTooLong = true };
            }
            else
            {
                normalizedNickname = trimmed;
            }
        }

        string? normalizedHandle = null;
        if (input.Handle is not null)
        {
            var trimmed = input.Handle.Trim();
            if (trimmed.Length == 0)
            {
                normalizedHandle = null;
            }
            else
            {
                var candidate = trimmed.ToLowerInvariant();
                if (!HandleFormat.IsMatch(candidate))
                {
                    return new UpdateProfileResult { HandleInvalid = true };
                }
                normalizedHandle = candidate;
            }
        }

        var user = await userRepository.GetByIdForUpdateAsync(userId, cancellationToken);

        if (user is null)
        {
            return new UpdateProfileResult { UserNotFound = true };
        }

        var profile = user.EnsureProfile();

        if (input.Nickname is not null)
        {
            profile.Nickname = normalizedNickname;
        }

        if (input.Handle is not null)
        {
            if (normalizedHandle is not null
                && !string.Equals(profile.Handle, normalizedHandle, StringComparison.Ordinal)
                && await userRepository.HandleExistsAsync(normalizedHandle, userId, cancellationToken))
            {
                return new UpdateProfileResult { HandleAlreadyTaken = true };
            }
            profile.Handle = normalizedHandle;
        }

        profile.UpdatedAtUtc = DateTime.UtcNow;

        await userRepository.SaveChangesAsync(cancellationToken);

        return new UpdateProfileResult
        {
            Success = true,
            User = MapUser(user)
        };
    }

    public async Task<RequestAccountDeletionResult> RequestAccountDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdForUpdateIncludingDeletedAsync(userId, cancellationToken);

        if (user is null)
        {
            return new RequestAccountDeletionResult { UserNotFound = true };
        }

        if (user.DeletedAtUtc is null)
        {
            user.DeletedAtUtc = DateTime.UtcNow;
            await userRepository.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "User {UserId} requested account deletion; hard-delete will run after the 72-hour grace period.",
                user.Id);
        }

        return new RequestAccountDeletionResult
        {
            Success = true,
            User = MapUser(user)
        };
    }

    public async Task<HandleAvailabilityResult> CheckHandleAvailabilityAsync(
        string handle,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var normalized = (handle ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized.Length < HandleMinLength)
            return HandleAvailabilityResult.Unavailable(HandleAvailabilityReasons.TooShort);

        if (normalized.Length > HandleMaxLength)
            return HandleAvailabilityResult.Unavailable(HandleAvailabilityReasons.TooLong);

        if (!HandleAllowedChars.IsMatch(normalized))
            return HandleAvailabilityResult.Unavailable(HandleAvailabilityReasons.InvalidFormat);

        // Pass currentUserId so the caller's own current handle is reported as available,
        // not "taken" — matches the behavior of UpdateProfileAsync's uniqueness check.
        if (await userRepository.HandleExistsAsync(normalized, currentUserId, cancellationToken))
            return HandleAvailabilityResult.Unavailable(HandleAvailabilityReasons.Taken);

        return HandleAvailabilityResult.Ok();
    }

    public IQueryable<UserSearchResultDto> SearchUsers(string query, Guid? excludeUserId)
    {
        var normalized = (query ?? string.Empty).Trim().ToLowerInvariant();

        // Short queries would match nearly every row through the trigram OR-branch; return empty
        // rather than letting it hit the DB.
        if (normalized.Length < SearchMinQueryLength)
        {
            return Enumerable.Empty<UserSearchResultDto>().AsQueryable();
        }

        return userRepository.Search(normalized, excludeUserId);
    }

    public async Task<CancelAccountDeletionResult> CancelAccountDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdForUpdateIncludingDeletedAsync(userId, cancellationToken);

        if (user is null)
        {
            return new CancelAccountDeletionResult { UserNotFound = true };
        }

        if (user.DeletedAtUtc is not null)
        {
            user.DeletedAtUtc = null;
            await userRepository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("User {UserId} cancelled pending account deletion.", user.Id);
        }

        return new CancelAccountDeletionResult
        {
            Success = true,
            User = MapUser(user)
        };
    }

    internal static UserDto MapUser(User user)
    {
        var profile = user.Profile;
        var iv = user.IdentityVerification;
        var bc = user.BackgroundCheck;

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = profile?.DisplayName,
            Nickname = profile?.Nickname,
            Handle = profile?.Handle,
            ImageUrl = profile?.ImageUrl,
            PhoneNumber = profile?.PhoneNumber,
            EmailVerified = user.EmailVerified,
            IdentityVerificationStatus = iv?.Status ?? IdentityVerificationStatus.NotStarted,
            PersonaInquiryId = iv?.PersonaInquiryId,
            PersonaInquiryStatus = iv?.PersonaInquiryStatus,
            VerifiedFirstName = iv?.VerifiedFirstName,
            VerifiedMiddleName = iv?.VerifiedMiddleName,
            VerifiedLastName = iv?.VerifiedLastName,
            VerifiedBirthdate = iv?.VerifiedBirthdate,
            VerifiedLicenseState = iv?.VerifiedLicenseState,
            PersonaVerifiedAtUtc = iv?.PersonaVerifiedAtUtc,
            BackgroundCheckBadge = bc?.Badge ?? BackgroundCheckBadge.None,
            BackgroundCheckBadgeExpiresAtUtc = bc?.BadgeExpiresAtUtc,
            TermsAcceptedAtUtc = user.TermsAcceptedAtUtc,
            Subscription = SubscriptionDto.FromUser(user),
            CreatedAtUtc = user.CreatedAtUtc,
            DeletedAtUtc = user.DeletedAtUtc
        };
    }
}
