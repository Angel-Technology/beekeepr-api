using BuzzKeepr.Application.Users;
using BuzzKeepr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Infrastructure.Auth;

public sealed class WelcomeEmailSweeperBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<WelcomeEmailSweeperBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan InlineSendGracePeriod = TimeSpan.FromMinutes(5);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Welcome email sweep pass failed.");
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

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var welcomeSender = scope.ServiceProvider.GetRequiredService<IWelcomeEmailSender>();
        var cutoffUtc = DateTime.UtcNow.Subtract(InlineSendGracePeriod);

        var pending = await dbContext.Users
            .Where(user => user.WelcomeEmailSentAtUtc == null
                && user.CreatedAtUtc < cutoffUtc
                && (user.DisplayName != null || user.VerifiedFirstName != null))
            .OrderBy(user => user.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return;

        var sent = 0;
        foreach (var user in pending)
        {
            try
            {
                await welcomeSender.SendWelcomeAsync(user.Email, user.DisplayName ?? user.VerifiedFirstName, cancellationToken);
                user.WelcomeEmailSentAtUtc = DateTime.UtcNow;
                sent++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Welcome email sweep failed for user {UserId}; will retry next pass.",
                    user.Id);
            }
        }

        if (sent > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Welcome email sweep delivered {Sent}/{Pending} pending welcomes.", sent, pending.Count);
        }
    }
}
