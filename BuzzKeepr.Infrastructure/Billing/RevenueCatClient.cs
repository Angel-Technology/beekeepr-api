using System.Net.Http.Headers;
using System.Text.Json;
using BuzzKeepr.Application.Billing;
using BuzzKeepr.Application.Billing.Models;
using BuzzKeepr.Domain.Enums;
using BuzzKeepr.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure.Billing;

public sealed class RevenueCatClient(
    HttpClient httpClient,
    IOptions<RevenueCatOptions> revenueCatOptions,
    ILogger<RevenueCatClient> logger) : IRevenueCatClient
{
    private readonly RevenueCatOptions options = revenueCatOptions.Value;

    public async Task<RevenueCatSubscriberSnapshot?> GetSubscriberAsync(string appUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.SecretApiKey) || string.IsNullOrWhiteSpace(appUserId))
            return null;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(options.ApiBaseUrl), $"/v1/subscribers/{Uri.EscapeDataString(appUserId)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SecretApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "RevenueCat GET /v1/subscribers/{AppUserId} failed with {StatusCode}: {Body}",
                    appUserId,
                    (int)response.StatusCode,
                    errorBody);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(appUserId, body);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "RevenueCat subscriber lookup for {AppUserId} threw.", appUserId);
            return null;
        }
    }

    private static RevenueCatSubscriberSnapshot? Parse(string appUserId, string body)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("subscriber", out var subscriber))
            return null;

        var (entitlementId, entitlement) = FindActiveEntitlement(subscriber);
        var (productId, subscription) = FindMatchingSubscription(subscriber, entitlement);

        if (entitlement is null && subscription is null)
        {
            return new RevenueCatSubscriberSnapshot
            {
                AppUserId = appUserId,
                Status = SubscriptionStatus.None
            };
        }

        var expiresUtc = ParseUtc(entitlement, "expires_date") ?? ParseUtc(subscription, "expires_date");
        var willRenew = ReadBool(subscription, "auto_renew_status");
        var unsubscribed = ParseUtc(subscription, "unsubscribe_detected_at");
        var billingIssue = ParseUtc(subscription, "billing_issues_detected_at");
        var periodType = ReadString(subscription, "period_type");
        var store = MapStore(ReadString(subscription, "store"));

        var status = ResolveStatus(expiresUtc, periodType, unsubscribed, billingIssue);

        return new RevenueCatSubscriberSnapshot
        {
            AppUserId = appUserId,
            Status = status,
            Entitlement = entitlementId,
            ProductId = productId,
            Store = store,
            CurrentPeriodEndUtc = expiresUtc,
            WillRenew = willRenew
        };
    }

    private static SubscriptionStatus ResolveStatus(
        DateTime? expiresUtc,
        string? periodType,
        DateTime? unsubscribedAt,
        DateTime? billingIssueAt)
    {
        var nowUtc = DateTime.UtcNow;

        if (expiresUtc is null || expiresUtc <= nowUtc)
            return SubscriptionStatus.Expired;

        if (billingIssueAt is not null)
            return SubscriptionStatus.InGracePeriod;

        if (unsubscribedAt is not null)
            return SubscriptionStatus.Cancelled;

        if (string.Equals(periodType, "trial", StringComparison.OrdinalIgnoreCase))
            return SubscriptionStatus.Trialing;

        return SubscriptionStatus.Active;
    }

    private static (string? Id, JsonElement? Entitlement) FindActiveEntitlement(JsonElement subscriber)
    {
        if (!subscriber.TryGetProperty("entitlements", out var entitlements)
            || entitlements.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        var nowUtc = DateTime.UtcNow;
        foreach (var property in entitlements.EnumerateObject())
        {
            var expires = ParseUtc(property.Value, "expires_date");
            if (expires is null || expires > nowUtc)
                return (property.Name, property.Value);
        }

        return (null, null);
    }

    private static (string? ProductId, JsonElement? Subscription) FindMatchingSubscription(
        JsonElement subscriber,
        JsonElement? entitlement)
    {
        if (!subscriber.TryGetProperty("subscriptions", out var subscriptions)
            || subscriptions.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        // If we have an active entitlement, prefer the subscription it points at.
        if (entitlement.HasValue
            && entitlement.Value.TryGetProperty("product_identifier", out var productIdElement)
            && productIdElement.ValueKind == JsonValueKind.String)
        {
            var productId = productIdElement.GetString();
            if (productId is not null
                && subscriptions.TryGetProperty(productId, out var matched))
            {
                return (productId, matched);
            }
        }

        // Otherwise pick the latest still-active subscription.
        var nowUtc = DateTime.UtcNow;
        foreach (var property in subscriptions.EnumerateObject())
        {
            var expires = ParseUtc(property.Value, "expires_date");
            if (expires is null || expires > nowUtc)
                return (property.Name, property.Value);
        }

        return (null, null);
    }

    private static SubscriptionStore? MapStore(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "app_store" => SubscriptionStore.AppStore,
            "mac_app_store" => SubscriptionStore.AppStore,
            "play_store" => SubscriptionStore.PlayStore,
            "stripe" => SubscriptionStore.Stripe,
            "promotional" => SubscriptionStore.Promotional,
            null or "" => null,
            _ => SubscriptionStore.Unknown
        };
    }

    private static DateTime? ParseUtc(JsonElement? element, string propertyName)
    {
        if (element is null || !element.Value.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return DateTime.TryParse(
            value.GetString(),
            null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }

    private static bool? ReadBool(JsonElement? element, string propertyName)
    {
        if (element is null || !element.Value.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? ReadString(JsonElement? element, string propertyName)
    {
        if (element is null || !element.Value.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return value.GetString();
    }
}
