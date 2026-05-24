using BuzzKeepr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Infrastructure.Users;

// Users soft-delete by setting DeletedAtUtc and stay recoverable for 72 hours — signing back in
// or calling cancelAccountDeletion clears the flag. Past that window this sweeper hard-deletes
// them. Cascade rules wipe ExternalAccounts + Sessions; VerificationTokens.UserId is set NULL.
public sealed class AccountDeletionPurgeBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<AccountDeletionPurgeBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(72);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Account deletion purge pass failed.");
            }

            try
            {
                await Task.Delay(RunInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var cutoffUtc = DateTime.UtcNow.Subtract(GracePeriod);

        var purged = await dbContext.Users
            .IgnoreQueryFilters()
            .Where(user => user.DeletedAtUtc != null && user.DeletedAtUtc < cutoffUtc)
            .OrderBy(user => user.DeletedAtUtc)
            .Take(BatchSize)
            .ExecuteDeleteAsync(cancellationToken);

        if (purged > 0)
            logger.LogInformation("Account deletion purge hard-deleted {Purged} user(s) past the 72-hour grace period.", purged);
    }
}
