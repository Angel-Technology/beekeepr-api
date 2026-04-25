using System.Security.Cryptography;
using System.Text;
using BuzzKeepr.Application.Auth.Models;
using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.Users;
using BuzzKeepr.Application.Users.Models;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Application.Auth;

public sealed class AuthService(
    IAuthRepository authRepository,
    IEmailSignInSender emailSignInSender,
    IGoogleTokenVerifier googleTokenVerifier,
    IWelcomeEmailSender welcomeEmailSender,
    ILogger<AuthService> logger) : IAuthService
{
    private const int MaxVerificationAttempts = 5;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan SessionTouchInterval = TimeSpan.FromHours(24);

    public async Task<CurrentUserResult> GetCurrentUserAsync(string? sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return new CurrentUserResult();

        var nowUtc = DateTime.UtcNow;
        var session = await authRepository.GetActiveSessionByTokenHashAsync(
            HashToken(sessionToken),
            nowUtc,
            cancellationToken);

        if (session?.User is null)
            return new CurrentUserResult();

        DateTime? refreshedExpiresAtUtc = null;

        var lastSeenAtUtc = session.LastSeenAtUtc ?? session.CreatedAtUtc;
        if (nowUtc - lastSeenAtUtc >= SessionTouchInterval)
        {
            var newExpiresAtUtc = nowUtc.Add(SessionLifetime);
            await authRepository.TouchSessionAsync(session.Id, nowUtc, newExpiresAtUtc, cancellationToken);
            refreshedExpiresAtUtc = newExpiresAtUtc;
        }

        return new CurrentUserResult
        {
            User = MapUser(session.User),
            RefreshedSessionExpiresAtUtc = refreshedExpiresAtUtc
        };
    }

    public async Task<SignOutResult> SignOutAsync(string? sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return new SignOutResult
            {
                Success = true
            };

        await authRepository.RevokeSessionAsync(HashToken(sessionToken), DateTime.UtcNow, cancellationToken);

        return new SignOutResult
        {
            Success = true
        };
    }

    public async Task<RequestEmailSignInResult> RequestEmailSignInAsync(
        RequestEmailSignInInput input,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(input.Email);

        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return new RequestEmailSignInResult
            {
                EmailRequired = true
            };

        var existingUser = await authRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
        var rawCode = CreateFiveDigitCode();
        var nowUtc = DateTime.UtcNow;
        var existingVerificationToken = await authRepository.GetLatestUnconsumedVerificationTokenAsync(
            normalizedEmail,
            VerificationTokenPurpose.EmailSignIn,
            cancellationToken);

        var verificationToken = existingVerificationToken ?? new VerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = existingUser?.Id,
            Email = normalizedEmail,
            Purpose = VerificationTokenPurpose.EmailSignIn
        };

        verificationToken.TokenHash = HashToken(rawCode);
        verificationToken.FailedAttempts = 0;
        verificationToken.CreatedAtUtc = nowUtc;
        verificationToken.ExpiresAtUtc = nowUtc.AddMinutes(15);
        verificationToken.ConsumedAtUtc = null;
        verificationToken.UserId ??= existingUser?.Id;

        try
        {
            await emailSignInSender.SendSignInCodeAsync(
                normalizedEmail,
                rawCode,
                verificationToken.ExpiresAtUtc,
                cancellationToken);
        }
        catch
        {
            return new RequestEmailSignInResult
            {
                EmailDeliveryFailed = true
            };
        }

        if (existingVerificationToken is null)
            await authRepository.AddVerificationTokenAsync(verificationToken, cancellationToken);
        else
            await authRepository.SaveChangesAsync(cancellationToken);

        return new RequestEmailSignInResult
        {
            Success = true,
            Email = normalizedEmail,
            ExpiresAtUtc = verificationToken.ExpiresAtUtc
        };
    }

    public async Task<VerifyEmailSignInResult> VerifyEmailSignInAsync(
        VerifyEmailSignInInput input,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(input.Email);

        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(input.Code))
            return new VerifyEmailSignInResult
            {
                InvalidToken = true
            };

        var nowUtc = DateTime.UtcNow;

        var verificationToken = await authRepository.GetValidVerificationTokenAsync(
            normalizedEmail,
            VerificationTokenPurpose.EmailSignIn,
            nowUtc,
            cancellationToken);

        if (verificationToken is null)
            return new VerifyEmailSignInResult
            {
                InvalidToken = true
            };

        if (!string.Equals(verificationToken.TokenHash, HashToken(input.Code), StringComparison.Ordinal))
        {
            if (verificationToken.FailedAttempts < MaxVerificationAttempts)
                await authRepository.IncrementVerificationTokenFailedAttemptsAsync(verificationToken.Id, cancellationToken);

            return new VerifyEmailSignInResult
            {
                InvalidToken = true
            };
        }

        var user = await authRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
        var isNewUser = user is null;

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                EmailVerified = true,
                CreatedAtUtc = nowUtc
            };

            await authRepository.AddUserAsync(user, cancellationToken);
        }
        else
        {
            user.EmailVerified = true;
        }

        verificationToken.ConsumedAtUtc = nowUtc;

        var rawSessionToken = CreateOpaqueToken();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawSessionToken),
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(SessionLifetime),
            LastSeenAtUtc = nowUtc,
            IpAddress = TrimToMax(input.IpAddress, 64),
            UserAgent = TrimToMax(input.UserAgent, 512)
        };

        await authRepository.AddSessionAsync(session, cancellationToken);
        await authRepository.SaveChangesAsync(cancellationToken);

        if (isNewUser)
            await TrySendWelcomeAsync(user, cancellationToken);

        return new VerifyEmailSignInResult
        {
            Success = true,
            SessionToken = rawSessionToken,
            ExpiresAtUtc = session.ExpiresAtUtc,
            User = MapUser(user)
        };
    }

    public async Task<SignInWithGoogleResult> SignInWithGoogleAsync(
        SignInWithGoogleInput input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.IdToken))
            return new SignInWithGoogleResult
            {
                InvalidInput = true
            };

        var identity = await googleTokenVerifier.VerifyIdTokenAsync(input.IdToken, cancellationToken);

        if (identity is null)
            return new SignInWithGoogleResult
            {
                InvalidToken = true
            };

        var normalizedEmail = NormalizeEmail(identity.Email);
        var nowUtc = DateTime.UtcNow;
        var externalAccount = await authRepository.GetExternalAccountAsync(
            AuthProvider.Google,
            identity.ProviderAccountId,
            cancellationToken);

        User user;
        var isNewUser = false;

        if (externalAccount is not null)
        {
            user = externalAccount.User;
            externalAccount.ProviderEmail = normalizedEmail;
            externalAccount.LastSignInAtUtc = nowUtc;
        }
        else
        {
            var existingUser = await authRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
            isNewUser = existingUser is null;

            user = existingUser ?? new User
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                DisplayName = string.IsNullOrWhiteSpace(identity.DisplayName) ? null : identity.DisplayName.Trim(),
                EmailVerified = true,
                CreatedAtUtc = nowUtc
            };

            user.EmailVerified = true;
            user.DisplayName ??= string.IsNullOrWhiteSpace(identity.DisplayName) ? null : identity.DisplayName.Trim();

            if (isNewUser) await authRepository.AddUserAsync(user, cancellationToken);

            externalAccount = new ExternalAccount
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Provider = AuthProvider.Google,
                ProviderAccountId = identity.ProviderAccountId,
                ProviderEmail = normalizedEmail,
                CreatedAtUtc = nowUtc,
                LastSignInAtUtc = nowUtc,
                User = user
            };

            await authRepository.AddExternalAccountAsync(externalAccount, cancellationToken);
        }

        var rawSessionToken = CreateOpaqueToken();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawSessionToken),
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(SessionLifetime),
            LastSeenAtUtc = nowUtc,
            IpAddress = TrimToMax(input.IpAddress, 64),
            UserAgent = TrimToMax(input.UserAgent, 512)
        };

        await authRepository.AddSessionAsync(session, cancellationToken);
        await authRepository.SaveChangesAsync(cancellationToken);

        if (isNewUser)
            await TrySendWelcomeAsync(user, cancellationToken);

        return new SignInWithGoogleResult
        {
            Success = true,
            SessionToken = rawSessionToken,
            ExpiresAtUtc = session.ExpiresAtUtc,
            User = MapUser(user)
        };
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string CreateOpaqueToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private static string CreateFiveDigitCode()
    {
        var value = RandomNumberGenerator.GetInt32(0, 100000);
        return value.ToString("D5");
    }

    private async Task TrySendWelcomeAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            await welcomeEmailSender.SendWelcomeAsync(user.Email, user.DisplayName, cancellationToken);
            user.WelcomeEmailSentAtUtc = DateTime.UtcNow;
            await authRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Welcome email failed to send for user {UserId}; sweeper will retry.",
                user.Id);
        }
    }

    private static string? TrimToMax(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }

    private static UserDto MapUser(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            EmailVerified = user.EmailVerified,
            IdentityVerificationStatus = user.IdentityVerificationStatus,
            PersonaInquiryId = user.PersonaInquiryId,
            PersonaInquiryStatus = user.PersonaInquiryStatus,
            TermsAcceptedAtUtc = user.TermsAcceptedAtUtc,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}
