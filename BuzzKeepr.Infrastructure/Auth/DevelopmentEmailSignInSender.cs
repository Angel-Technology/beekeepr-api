using BuzzKeepr.Application.Auth;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Infrastructure.Auth;

public sealed class DevelopmentEmailSignInSender(ILogger<DevelopmentEmailSignInSender> logger) : IEmailSignInSender
{
    public Task SendSignInCodeAsync(string email, string code, DateTime expiresAtUtc, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Development sign-in code for {Email}: {Code}. Expires at {ExpiresAtUtc}.",
            email,
            code,
            expiresAtUtc);

        return Task.CompletedTask;
    }
}
