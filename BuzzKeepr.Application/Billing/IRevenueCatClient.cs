using BuzzKeepr.Application.Billing.Models;

namespace BuzzKeepr.Application.Billing;

public interface IRevenueCatClient
{
    Task<RevenueCatSubscriberSnapshot?> GetSubscriberAsync(string appUserId, CancellationToken cancellationToken);
}
