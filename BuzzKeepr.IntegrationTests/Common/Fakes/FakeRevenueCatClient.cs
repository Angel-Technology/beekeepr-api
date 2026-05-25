using System.Collections.Concurrent;
using BuzzKeepr.Application.Billing;
using BuzzKeepr.Application.Billing.Models;
using BuzzKeepr.Domain.Enums;

namespace BuzzKeepr.IntegrationTests.Common.Fakes;

public sealed class FakeRevenueCatClient : IRevenueCatClient
{
    private readonly ConcurrentDictionary<string, RevenueCatSubscriberSnapshot?> snapshotsByAppUserId = new();

    public List<string> GetSubscriberCalls { get; } = new();

    public List<(string AppUserId, string EntitlementId, PromoCodeDuration Duration)> PromotionalGrantCalls { get; } = new();

    public bool PromotionalGrantSucceeds { get; set; } = true;

    public void RegisterSubscriber(string appUserId, RevenueCatSubscriberSnapshot snapshot)
        => snapshotsByAppUserId[appUserId] = snapshot;

    public Task<RevenueCatSubscriberSnapshot?> GetSubscriberAsync(string appUserId, CancellationToken cancellationToken)
    {
        GetSubscriberCalls.Add(appUserId);
        snapshotsByAppUserId.TryGetValue(appUserId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<bool> GrantPromotionalEntitlementAsync(
        string appUserId,
        string entitlementId,
        PromoCodeDuration duration,
        CancellationToken cancellationToken)
    {
        PromotionalGrantCalls.Add((appUserId, entitlementId, duration));
        return Task.FromResult(PromotionalGrantSucceeds);
    }
}
