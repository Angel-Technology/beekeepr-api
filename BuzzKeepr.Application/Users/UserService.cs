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

    private static UserDto MapUser(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
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
            TermsAcceptedAtUtc = user.TermsAcceptedAtUtc,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}
