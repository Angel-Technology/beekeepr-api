using System.Collections.Concurrent;
using BuzzKeepr.Application.Billing;
using BuzzKeepr.Application.Billing.Models;

namespace BuzzKeepr.IntegrationTests.Common.Fakes;

public sealed class FakeRevenueCatClient : IRevenueCatClient
{
    private readonly ConcurrentDictionary<string, RevenueCatSubscriberSnapshot?> snapshotsByAppUserId = new();

    public List<string> GetSubscriberCalls { get; } = new();

    public void RegisterSubscriber(string appUserId, RevenueCatSubscriberSnapshot snapshot)
        => snapshotsByAppUserId[appUserId] = snapshot;

    public Task<RevenueCatSubscriberSnapshot?> GetSubscriberAsync(string appUserId, CancellationToken cancellationToken)
    {
        GetSubscriberCalls.Add(appUserId);
        snapshotsByAppUserId.TryGetValue(appUserId, out var snapshot);
        return Task.FromResult(snapshot);
    }
}
