using System.Collections.Concurrent;
using BuzzKeepr.Application.Users;

namespace BuzzKeepr.IntegrationTests.Common.Fakes;

public sealed class FakeWelcomeEmailSender : IWelcomeEmailSender
{
    private readonly ConcurrentBag<SentWelcome> sent = new();
    private Func<string, string?, Task>? failureBehavior;

    public IReadOnlyCollection<SentWelcome> Sent => sent;

    public Task SendWelcomeAsync(string email, string? displayName, CancellationToken cancellationToken)
    {
        if (failureBehavior is not null)
            return failureBehavior(email, displayName);

        sent.Add(new SentWelcome(email, displayName, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public void FailNextSendsWith(Exception exception)
        => failureBehavior = (_, _) => Task.FromException(exception);

    public void StopFailing() => failureBehavior = null;

    public sealed record SentWelcome(string Email, string? DisplayName, DateTime SentAtUtc);
}
