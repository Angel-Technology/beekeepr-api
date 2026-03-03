using System.Security.Cryptography;
using System.Text;
using BuzzKeepr.Application.Auth.Models;
using BuzzKeepr.Application.Users.Models;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Auth;

public sealed class AuthService(IAuthRepository authRepository) : IAuthService
{
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
        var rawToken = CreateOpaqueToken();

        var verificationToken = new VerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = existingUser?.Id,
            Email = normalizedEmail,
            Purpose = VerificationTokenPurpose.EmailSignIn,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
        };

        await authRepository.AddVerificationTokenAsync(verificationToken, cancellationToken);

        return new RequestEmailSignInResult
        {
            Success = true,
            Email = normalizedEmail,
            ExpiresAtUtc = verificationToken.ExpiresAtUtc,
            Token = rawToken
        };
    }

    public async Task<VerifyEmailSignInResult> VerifyEmailSignInAsync(
        VerifyEmailSignInInput input,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(input.Email);

        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(input.Token))
            return new VerifyEmailSignInResult
            {
                InvalidToken = true
            };

        var tokenHash = HashToken(input.Token);
        var nowUtc = DateTime.UtcNow;

        var verificationToken = await authRepository.GetValidVerificationTokenAsync(
            normalizedEmail,
            VerificationTokenPurpose.EmailSignIn,
            tokenHash,
            nowUtc,
            cancellationToken);

        if (verificationToken is null)
            return new VerifyEmailSignInResult
            {
                InvalidToken = true
            };

        var user = await authRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);

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
            ExpiresAtUtc = nowUtc.AddDays(30),
            LastSeenAtUtc = nowUtc
        };

        await authRepository.AddSessionAsync(session, cancellationToken);
        await authRepository.SaveChangesAsync(cancellationToken);

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
        var normalizedEmail = NormalizeEmail(input.Email);

        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(input.ProviderAccountId))
            return new SignInWithGoogleResult
            {
                InvalidInput = true
            };

        var nowUtc = DateTime.UtcNow;
        var externalAccount = await authRepository.GetExternalAccountAsync(
            AuthProvider.Google,
            input.ProviderAccountId,
            cancellationToken);

        User user;

        if (externalAccount is not null)
        {
            user = externalAccount.User;
            externalAccount.ProviderEmail = normalizedEmail;
            externalAccount.LastSignInAtUtc = nowUtc;
        }
        else
        {
            var existingUser = await authRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
            var isNewUser = existingUser is null;

            user = existingUser ?? new User
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim(),
                EmailVerified = true,
                CreatedAtUtc = nowUtc
            };

            user.EmailVerified = true;
            user.DisplayName ??= string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim();

            if (isNewUser) await authRepository.AddUserAsync(user, cancellationToken);

            externalAccount = new ExternalAccount
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Provider = AuthProvider.Google,
                ProviderAccountId = input.ProviderAccountId,
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
            ExpiresAtUtc = nowUtc.AddDays(30),
            LastSeenAtUtc = nowUtc
        };

        await authRepository.AddSessionAsync(session, cancellationToken);
        await authRepository.SaveChangesAsync(cancellationToken);

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
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}