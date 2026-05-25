using BuzzKeepr.Application.Billing;
using BuzzKeepr.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BuzzKeepr.Infrastructure.Persistence.Repositories;

public sealed class PromoCodeRepository(
    BuzzKeeprDbContext dbContext,
    ILogger<PromoCodeRepository> logger) : IPromoCodeRepository
{
    // Postgres SQLSTATE 23505 = unique_violation. Used to detect a duplicate redemption attempt
    // by the same user on the same code; this is the "once per user" rule firing.
    private const string UniqueViolationSqlState = "23505";

    public Task<PromoCode?> FindByCodeAsync(string code, CancellationToken cancellationToken)
    {
        return dbContext.PromoCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(promo => promo.Code == code, cancellationToken);
    }

    public async Task<PromoRedemptionOutcome> TryRedeemAsync(
        Guid promoCodeId,
        Guid userId,
        Func<CancellationToken, Task<bool>> grantCallback,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // INSERT first: if the unique (PromoCodeId, UserId) constraint trips we know this user has
        // already redeemed this code before — bail out before we touch the cap count.
        var redemption = new PromoRedemption
        {
            Id = Guid.NewGuid(),
            PromoCodeId = promoCodeId,
            UserId = userId,
            RedeemedAtUtc = DateTime.UtcNow
        };
        dbContext.PromoRedemptions.Add(redemption);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                                           && pg.SqlState == UniqueViolationSqlState)
        {
            await transaction.RollbackAsync(cancellationToken);
            // Detach the failed insert so the DbContext isn't left in a broken state if it's
            // reused later in the request scope.
            dbContext.Entry(redemption).State = EntityState.Detached;
            return PromoRedemptionOutcome.AlreadyRedeemed;
        }

        // Atomic conditional increment. If 0 rows are affected the code became inactive, expired,
        // or hit its cap between the service's pre-check and this UPDATE — treat as cap reached.
        // Doing this here (instead of SELECT FOR UPDATE earlier) keeps the lock window tiny:
        // Postgres only locks the single PromoCodes row for the duration of the UPDATE.
        var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE ""PromoCodes""
               SET ""RedemptionsUsed"" = ""RedemptionsUsed"" + 1
               WHERE ""Id"" = {promoCodeId}
                 AND ""IsActive"" = TRUE
                 AND (""ExpiresAtUtc"" IS NULL OR ""ExpiresAtUtc"" > NOW())
                 AND (""MaxRedemptions"" IS NULL OR ""RedemptionsUsed"" < ""MaxRedemptions"")",
            cancellationToken);

        if (rowsAffected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PromoRedemptionOutcome.CapReached;
        }

        // Grant via the callback while the transaction is still open. A non-2xx response or thrown
        // exception rolls back the redemption row + cap increment together, so a failed RC call
        // never leaves a phantom redemption recorded.
        bool granted;
        try
        {
            granted = await grantCallback(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Promo grant callback threw for code {PromoCodeId} user {UserId}.", promoCodeId, userId);
            await transaction.RollbackAsync(cancellationToken);
            return PromoRedemptionOutcome.GrantFailed;
        }

        if (!granted)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PromoRedemptionOutcome.GrantFailed;
        }

        await transaction.CommitAsync(cancellationToken);
        return PromoRedemptionOutcome.Success;
    }
}
