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
    private const int DisplayNameMaxLength = 200;
    private const int PhoneMaxLength = 32;
    private const int SocialHandleMaxLength = 64;

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

        await userRepository.AddAsync(user, cancellationToken);

        // Welcome email is deferred — we have no DisplayName at signup. The sweeper picks the
        // user up once they complete their profile (or, for users who go through Persona, when
        // VerifiedFirstName lands via the webhook).

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

        if (!TryNormalizeOptional(input.DisplayName, DisplayNameMaxLength, out var normalizedDisplayName))
            return new UpdateProfileResult { DisplayNameTooLong = true };

        if (!TryNormalizeOptional(input.PhoneNumber, PhoneMaxLength, out var normalizedPhone)
            || !TryNormalizeOptional(input.GoogleVoicePhone, PhoneMaxLength, out var normalizedGoogleVoice)
            || !TryNormalizeOptional(input.WhatsAppPhone, PhoneMaxLength, out var normalizedWhatsApp)
            || !TryNormalizeOptional(input.SignalPhone, PhoneMaxLength, out var normalizedSignal))
        {
            return new UpdateProfileResult { PhoneNumberInvalid = true };
        }

        if (!TryNormalizeOptional(input.InstagramHandle, SocialHandleMaxLength, out var normalizedInstagram)
            || !TryNormalizeOptional(input.TelegramHandle, SocialHandleMaxLength, out var normalizedTelegram))
        {
            return new UpdateProfileResult { ContactFieldTooLong = true };
        }

        // The frontend allows users to type "@handle" or "handle" interchangeably; canonicalize
        // to no-leading-@ so storage is consistent and the display layer owns the prefix.
        normalizedInstagram = normalizedInstagram?.TrimStart('@');
        normalizedTelegram = normalizedTelegram?.TrimStart('@');

        var user = await userRepository.GetByIdForUpdateAsync(userId, cancellationToken);

        if (user is null)
        {
            return new UpdateProfileResult { UserNotFound = true };
        }

        var profile = user.EnsureProfile();

        if (input.Nickname is not null)
            profile.Nickname = normalizedNickname;

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

        if (input.DisplayName is not null)
            profile.DisplayName = normalizedDisplayName;

        if (input.PhoneNumber is not null)
            profile.PhoneNumber = normalizedPhone;

        if (input.GoogleVoicePhone is not null)
            profile.GoogleVoicePhone = normalizedGoogleVoice;

        if (input.WhatsAppPhone is not null)
            profile.WhatsAppPhone = normalizedWhatsApp;

        if (input.InstagramHandle is not null)
            profile.InstagramHandle = normalizedInstagram;

        if (input.TelegramHandle is not null)
            profile.TelegramHandle = normalizedTelegram;

        if (input.SignalPhone is not null)
            profile.SignalPhone = normalizedSignal;

        if (input.ProfileVisibility.HasValue)
            profile.ProfileVisibility = input.ProfileVisibility.Value;

        if (input.ContactVisibility.HasValue)
            profile.ContactVisibility = input.ContactVisibility.Value;

        profile.UpdatedAtUtc = DateTime.UtcNow;

        await userRepository.SaveChangesAsync(cancellationToken);

        return new UpdateProfileResult
        {
            Success = true,
            User = MapUser(user)
        };
    }

    // Generic length-bounded trim. Empty string after trim clears the field; null input means
    // "don't touch." Returns false only when the input would exceed the cap.
    private static bool TryNormalizeOptional(string? raw, int maxLength, out string? normalized)
    {
        normalized = null;
        if (raw is null) return true;

        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return true;
        if (trimmed.Length > maxLength) return false;

        normalized = trimmed;
        return true;
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
            GoogleVoicePhone = profile?.GoogleVoicePhone,
            WhatsAppPhone = profile?.WhatsAppPhone,
            InstagramHandle = profile?.InstagramHandle,
            TelegramHandle = profile?.TelegramHandle,
            SignalPhone = profile?.SignalPhone,
            ProfileVisibility = profile?.ProfileVisibility ?? ProfileVisibility.Public,
            ContactVisibility = profile?.ContactVisibility ?? ContactVisibility.Private,
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
