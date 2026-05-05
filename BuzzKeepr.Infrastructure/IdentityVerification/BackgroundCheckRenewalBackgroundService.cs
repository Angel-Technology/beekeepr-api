using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Domain.Enums;
using BuzzKeepr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Infrastructure.IdentityVerification;

// For active subscribers, the Checkr badge is supposed to stay continuously valid: as soon as
// it tips past `BackgroundCheckBadgeExpiresAtUtc`, this sweeper picks them up, re-runs the
// instant criminal check (free re-use of the existing CheckrProfileId — Checkr bills the call,
// we eat it as part of the subscription), and stamps a fresh expiry. Lapsed subscribers are
// skipped — their badge timestamp simply ages past now() and the frontend treats it as expired
// until they resubscribe (which lets the sweeper pick them back up).
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

        // We mirror SubscriptionDto.IsLocallyActive's logic here as an EF-translatable predicate
        // so we don't pull every user into memory just to filter. Active = status is not None or
        // Expired AND the current period hasn't ended (or is unbounded).
        var dueForRenewal = await dbContext.Users
            .Where(user => user.BackgroundCheckBadge != BackgroundCheckBadge.None
                && user.BackgroundCheckBadgeExpiresAtUtc != null
                && user.BackgroundCheckBadgeExpiresAtUtc < nowUtc
                && user.SubscriptionStatus != SubscriptionStatus.None
                && user.SubscriptionStatus != SubscriptionStatus.Expired
                && (user.SubscriptionCurrentPeriodEndUtc == null
                    || user.SubscriptionCurrentPeriodEndUtc > nowUtc)
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
                    "Renewing Checkr badge for user {UserId} (sub active, badge expired @ {ExpiresAt:o}).",
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
