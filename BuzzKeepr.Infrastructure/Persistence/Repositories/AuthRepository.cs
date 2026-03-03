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
        string tokenHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        return await dbContext.VerificationTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(
                token => token.Email == email
                    && token.Purpose == purpose
                    && token.TokenHash == tokenHash
                    && token.ConsumedAtUtc == null
                    && token.ExpiresAtUtc > nowUtc,
                cancellationToken);
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
