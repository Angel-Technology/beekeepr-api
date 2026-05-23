using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Domain.Enums;
using BuzzKeepr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Infrastructure.IdentityVerification;

// The Checkr badge is supposed to stay continuously valid for any user who has ever earned one:
// as soon as it tips past `BackgroundCheckBadgeExpiresAtUtc`, this sweeper picks them up,
// re-runs the instant criminal check (re-uses the existing CheckrProfileId so Checkr's billed
// call is the same flat rate), and stamps a fresh expiry. No subscription requirement — the
// only gates are "has a Checkr profile" (i.e. already ran at least one check) and "badge is
// past its expiry."
public sealed class BackgroundCheckRenewalBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundCheckRenewalBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(6);
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
                logger.LogError(exception, "Background check renewal sweep pass failed.");
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
        var identityVerificationService = scope.ServiceProvider.GetRequiredService<IIdentityVerificationService>();
        var nowUtc = DateTime.UtcNow;

        var dueForRenewal = await dbContext.Users
            .Where(user => user.BackgroundCheckBadge != BackgroundCheckBadge.None
                && user.BackgroundCheckBadgeExpiresAtUtc != null
                && user.BackgroundCheckBadgeExpiresAtUtc < nowUtc
                && user.CheckrProfileId != null)
            .OrderBy(user => user.BackgroundCheckBadgeExpiresAtUtc)
            .Take(BatchSize)
            .Select(user => new { user.Id, user.BackgroundCheckBadgeExpiresAtUtc })
            .ToListAsync(cancellationToken);

        if (dueForRenewal.Count == 0)
            return;

        var renewed = 0;
        var failed = 0;
        foreach (var entry in dueForRenewal)
        {
            try
            {
                logger.LogInformation(
                    "Renewing Checkr badge for user {UserId} (badge expired @ {ExpiresAt:o}).",
                    entry.Id,
                    entry.BackgroundCheckBadgeExpiresAtUtc);

                var result = await identityVerificationService.CreateInstantCriminalCheckAsync(
                    entry.Id,
                    new StartInstantCriminalCheckInput(),
                    cancellationToken);

                if (result.Success)
                    renewed++;
                else
                {
                    failed++;
                    logger.LogWarning(
                        "Background check renewal for user {UserId} did not succeed: {Error}",
                        entry.Id,
                        result.Error);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                logger.LogWarning(
                    exception,
                    "Background check renewal threw for user {UserId}; will retry next pass.",
                    entry.Id);
            }
        }

        logger.LogInformation(
            "Background check renewal sweep: {Renewed}/{Total} renewed, {Failed} failed.",
            renewed,
            dueForRenewal.Count,
            failed);
    }
}
