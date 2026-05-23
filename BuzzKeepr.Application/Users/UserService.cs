using System.Text.RegularExpressions;
using BuzzKeepr.Application.Billing.Models;
using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.Users.Models;
using BuzzKeepr.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Application.Users;

public sealed class UserService(
    IUserRepository userRepository,
    IWelcomeEmailSender welcomeEmailSender,
    ILogger<UserService> logger) : IUserService
{
    private const int NicknameMaxLength = 50;

    private static readonly Regex HandleFormat = new("^@[a-zA-Z0-9_]{3,20}$", RegexOptions.Compiled);


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
            DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim(),
            EmailVerified = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        await userRepository.AddAsync(user, cancellationToken);

        try
        {
            await welcomeEmailSender.SendWelcomeAsync(user.Email, user.DisplayName, cancellationToken);
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

        if (input.Nickname is not null)
        {
            user.Nickname = normalizedNickname;
        }

        if (input.Handle is not null)
        {
            if (normalizedHandle is not null
                && !string.Equals(user.Handle, normalizedHandle, StringComparison.Ordinal)
                && await userRepository.HandleExistsAsync(normalizedHandle, userId, cancellationToken))
            {
                return new UpdateProfileResult { HandleAlreadyTaken = true };
            }
            user.Handle = normalizedHandle;
        }

        await userRepository.SaveChangesAsync(cancellationToken);

        return new UpdateProfileResult
        {
            Success = true,
            User = MapUser(user)
        };
    }

    private static UserDto MapUser(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Nickname = user.Nickname,
            Handle = user.Handle,
            ImageUrl = user.ImageUrl,
            EmailVerified = user.EmailVerified,
            IdentityVerificationStatus = user.IdentityVerificationStatus,
            PersonaInquiryId = user.PersonaInquiryId,
            PersonaInquiryStatus = user.PersonaInquiryStatus,
            VerifiedFirstName = user.VerifiedFirstName,
            VerifiedMiddleName = user.VerifiedMiddleName,
            VerifiedLastName = user.VerifiedLastName,
            VerifiedBirthdate = user.VerifiedBirthdate,
            VerifiedLicenseState = user.VerifiedLicenseState,
            PhoneNumber = user.PhoneNumber,
            PersonaVerifiedAtUtc = user.PersonaVerifiedAtUtc,
            BackgroundCheckBadge = user.BackgroundCheckBadge,
            BackgroundCheckBadgeExpiresAtUtc = user.BackgroundCheckBadgeExpiresAtUtc,
            TermsAcceptedAtUtc = user.TermsAcceptedAtUtc,
            Subscription = SubscriptionDto.FromUser(user),
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}
