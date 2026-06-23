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

        // Eligibility: user has *some* name we can greet them by — either a self-supplied
        // DisplayName (UserProfile) or a verified first name from Persona (UserIdentityVerification).
        // Without that we'd render "Welcome to BuzzKeepr, there." — skip and let the sweeper
        // pick them up on a future pass once they fill in a profile or finish verification.
        var pending = await dbContext.Users
            .Where(user => user.WelcomeEmailSentAtUtc == null
                && user.CreatedAtUtc < cutoffUtc
                && ((user.Profile != null && user.Profile.DisplayName != null)
                    || (user.IdentityVerification != null && user.IdentityVerification.VerifiedFirstName != null)))
            .Include(user => user.Profile)
            .Include(user => user.IdentityVerification)
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
                var displayName = user.Profile?.DisplayName ?? user.IdentityVerification?.VerifiedFirstName;
                await welcomeSender.SendWelcomeAsync(user.Email, displayName, cancellationToken);
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
