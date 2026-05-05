using System.Collections.Concurrent;
using BuzzKeepr.Application.Auth;

namespace BuzzKeepr.IntegrationTests.Common.Fakes;

public sealed class FakeEmailSignInSender : IEmailSignInSender
{
    private readonly ConcurrentDictionary<string, SentCode> latestByEmail = new(StringComparer.OrdinalIgnoreCase);

    public Task SendSignInCodeAsync(string email, string code, DateTime expiresAtUtc, CancellationToken cancellationToken)
    {
        latestByEmail[email] = new SentCode(email, code, expiresAtUtc, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public SentCode RequireLatestFor(string email)
    {
        if (!latestByEmail.TryGetValue(email, out var sent))
            throw new InvalidOperationException($"No sign-in code captured for {email}.");

        return sent;
    }

    public sealed record SentCode(string Email, string Code, DateTime ExpiresAtUtc, DateTime SentAtUtc);
}
