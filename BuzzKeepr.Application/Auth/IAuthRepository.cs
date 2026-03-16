using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.Application.Auth;

public interface IAuthRepository
{
    Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken);

    Task<User?> GetUserBySessionTokenHashAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken);

    Task RevokeSessionAsync(string tokenHash, DateTime revokedAtUtc, CancellationToken cancellationToken);

    Task<ExternalAccount?> GetExternalAccountAsync(AuthProvider provider, string providerAccountId,
        CancellationToken cancellationToken);

    Task<VerificationToken?> GetValidVerificationTokenAsync(
        string email,
        VerificationTokenPurpose purpose,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task IncrementVerificationTokenFailedAttemptsAsync(Guid verificationTokenId, CancellationToken cancellationToken);

    Task AddVerificationTokenAsync(VerificationToken verificationToken, CancellationToken cancellationToken);

    Task AddSessionAsync(Session session, CancellationToken cancellationToken);

    Task AddExternalAccountAsync(ExternalAccount externalAccount, CancellationToken cancellationToken);

    Task AddUserAsync(User user, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}