using BuzzKeepr.Application.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Infrastructure.Auth;

public sealed class SessionCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<SessionCleanupBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetentionGracePeriod = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var authRepository = scope.ServiceProvider.GetRequiredService<IAuthRepository>();
                var cutoffUtc = DateTime.UtcNow.Subtract(RetentionGracePeriod);
                var deleted = await authRepository.DeleteAgedSessionsAsync(cutoffUtc, stoppingToken);

                if (deleted > 0)
                    logger.LogInformation("Deleted {Count} aged sessions older than {CutoffUtc:o}.", deleted, cutoffUtc);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Session cleanup pass failed.");
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
}
