using BuzzKeepr.Application.Auth;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class AuthRepository(BuzzKeeprDbContext dbContext) : IAuthRepository
{
    public async Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .Include(user => user.ExternalAccounts)
            .FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
    }

    public async Task<Session?> GetActiveSessionByTokenHashAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken)
    {
        return await dbContext.Sessions
            .AsNoTracking()
            .Include(session => session.User)
            .FirstOrDefaultAsync(session => session.TokenHash == tokenHash
                && session.RevokedAtUtc == null
                && session.ExpiresAtUtc > nowUtc, cancellationToken);
    }

    public async Task TouchSessionAsync(Guid sessionId, DateTime lastSeenAtUtc, DateTime expiresAtUtc, CancellationToken cancellationToken)
    {
        await dbContext.Sessions
            .Where(session => session.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.LastSeenAtUtc, lastSeenAtUtc)
                .SetProperty(session => session.ExpiresAtUtc, expiresAtUtc), cancellationToken);
    }

    public async Task<int> DeleteAgedSessionsAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        return await dbContext.Sessions
            .Where(session => session.ExpiresAtUtc < cutoffUtc
                || (session.RevokedAtUtc != null && session.RevokedAtUtc < cutoffUtc))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task RevokeSessionAsync(string tokenHash, DateTime revokedAtUtc, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .FirstOrDefaultAsync(session => session.TokenHash == tokenHash && session.RevokedAtUtc == null, cancellationToken);

        if (session is null)
            return;

        session.RevokedAtUtc = revokedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ExternalAccount?> GetExternalAccountAsync(
        AuthProvider provider,
        string providerAccountId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ExternalAccounts
            .Include(account => account.User)
            .FirstOrDefaultAsync(
                account => account.Provider == provider && account.ProviderAccountId == providerAccountId,
                cancellationToken);
    }

    public async Task<VerificationToken?> GetValidVerificationTokenAsync(
        string email,
        VerificationTokenPurpose purpose,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        return await dbContext.VerificationTokens
            .Include(token => token.User)
            .OrderByDescending(token => token.CreatedAtUtc)
            .FirstOrDefaultAsync(
                token => token.Email == email
                    && token.Purpose == purpose
                    && token.ConsumedAtUtc == null
                    && token.ExpiresAtUtc > nowUtc
                    && token.FailedAttempts < 5,
                cancellationToken);
    }

    public async Task<VerificationToken?> GetLatestUnconsumedVerificationTokenAsync(
        string email,
        VerificationTokenPurpose purpose,
        CancellationToken cancellationToken)
    {
        return await dbContext.VerificationTokens
            .Where(token => token.Email == email
                            && token.Purpose == purpose
                            && token.ConsumedAtUtc == null)
            .OrderByDescending(token => token.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task IncrementVerificationTokenFailedAttemptsAsync(Guid verificationTokenId, CancellationToken cancellationToken)
    {
        var token = await dbContext.VerificationTokens
            .FirstOrDefaultAsync(currentToken => currentToken.Id == verificationTokenId, cancellationToken);

        if (token is null)
            return;

        token.FailedAttempts += 1;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddVerificationTokenAsync(VerificationToken verificationToken, CancellationToken cancellationToken)
    {
        await dbContext.VerificationTokens.AddAsync(verificationToken, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddSessionAsync(Session session, CancellationToken cancellationToken)
    {
        await dbContext.Sessions.AddAsync(session, cancellationToken);
    }

    public async Task AddExternalAccountAsync(ExternalAccount externalAccount, CancellationToken cancellationToken)
    {
        await dbContext.ExternalAccounts.AddAsync(externalAccount, cancellationToken);
    }

    public async Task AddUserAsync(User user, CancellationToken cancellationToken)
    {
        await dbContext.Users.AddAsync(user, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
