using System.Text.Json;
using BuzzKeepr.Application.Billing.Models;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BuzzKeepr.Application.Billing;

public sealed class BillingService(
    IBillingRepository billingRepository,
    IRevenueCatClient revenueCatClient,
    ILogger<BillingService> logger) : IBillingService
{
    public async Task ProcessRevenueCatWebhookAsync(string rawRequestBody, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(rawRequestBody);

        if (!TryExtractEvent(document.RootElement, out var evt))
        {
            logger.LogWarning("RevenueCat webhook payload did not contain a usable event.");
            return;
        }

        if (string.IsNullOrWhiteSpace(evt.AppUserId))
        {
            logger.LogWarning("RevenueCat webhook event {EventId} of type {Type} had no app_user_id.", evt.Id, evt.Type);
            return;
        }

        var user = await billingRepository.GetByRevenueCatAppUserIdAsync(evt.AppUserId, cancellationToken)
            ?? await TryResolveUserByAppUserIdAsync(evt.AppUserId, cancellationToken);

        if (user is null)
        {
            // We see a purchase for a user we don't recognize. Common cause: the SDK was initialized
            // with a different appUserId than our user.Id. Log and drop — a webhook retry won't fix this.
            logger.LogWarning(
                "RevenueCat webhook for unknown app_user_id {AppUserId} (event {EventId}/{Type}).",
                evt.AppUserId,
                evt.Id,
                evt.Type);
            return;
        }

        if (user.SubscriptionUpdatedAtUtc.HasValue
            && evt.EventTimestampUtc <= user.SubscriptionUpdatedAtUtc.Value)
        {
            // Watermark: drop replays + out-of-order events, mirroring the Persona webhook pattern.
            logger.LogInformation(
                "Skipping stale RevenueCat event {EventId}/{Type}: @{IncomingAt:o} not newer than stored @{StoredAt:o}.",
                evt.Id,
                evt.Type,
                evt.EventTimestampUtc,
                user.SubscriptionUpdatedAtUtc.Value);
            return;
        }

        ApplyEventToUser(user, evt);
        await billingRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Applied RevenueCat event {EventId}/{Type} for user {UserId} → status {Status}, period_end {PeriodEnd:o}.",
            evt.Id,
            evt.Type,
            user.Id,
            user.SubscriptionStatus,
            user.SubscriptionCurrentPeriodEndUtc);
    }

    public async Task<SubscriptionDto> GetSubscriptionForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await billingRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null)
            return new SubscriptionDto();

        if (SubscriptionDto.IsLocallyActive(user))
            return SubscriptionDto.FromUser(user);

        // Local mirror says not active. Try a live read in case the user just purchased and the
        // webhook hasn't landed yet. If RevenueCat agrees, persist the fresh snapshot.
        if (string.IsNullOrWhiteSpace(user.RevenueCatAppUserId))
            return SubscriptionDto.FromUser(user);

        var snapshot = await revenueCatClient.GetSubscriberAsync(user.RevenueCatAppUserId, cancellationToken);

        if (snapshot is not null)
        {
            ApplySnapshotToUser(user, snapshot);
            await billingRepository.SaveChangesAsync(cancellationToken);
        }

        return SubscriptionDto.FromUser(user);
    }

    private async Task<User?> TryResolveUserByAppUserIdAsync(string appUserId, CancellationToken cancellationToken)
    {
        // Fallback: we expect the frontend to set RevenueCat's appUserId == our user.Id (a Guid),
        // but if the user has never been mirrored before, RevenueCatAppUserId is still null on our
        // side. Try parsing the appUserId as a Guid and load by primary key, then stamp the field
        // so subsequent webhooks resolve via the indexed lookup.
        if (!Guid.TryParse(appUserId, out var userId))
            return null;

        var user = await billingRepository.GetByIdAsync(userId, cancellationToken);

        if (user is not null)
            user.RevenueCatAppUserId = appUserId;

        return user;
    }

    private static void ApplyEventToUser(User user, RevenueCatEvent evt)
    {
        user.SubscriptionUpdatedAtUtc = evt.EventTimestampUtc;
        user.RevenueCatAppUserId ??= evt.AppUserId;

        if (!string.IsNullOrWhiteSpace(evt.ProductId))
            user.SubscriptionProductId = evt.ProductId;

        if (!string.IsNullOrWhiteSpace(evt.EntitlementId))
            user.SubscriptionEntitlement = evt.EntitlementId;

        if (evt.Store is not null)
            user.SubscriptionStore = evt.Store;

        switch (evt.Type)
        {
            case "INITIAL_PURCHASE":
                user.SubscriptionStatus = evt.IsTrialPeriod
                    ? SubscriptionStatus.Trialing
                    : SubscriptionStatus.Active;
                user.SubscriptionCurrentPeriodEndUtc = evt.ExpirationUtc ?? user.SubscriptionCurrentPeriodEndUtc;
                user.SubscriptionWillRenew = true;
                break;

            case "RENEWAL":
            case "PRODUCT_CHANGE":
            case "UNCANCELLATION":
                user.SubscriptionStatus = SubscriptionStatus.Active;
                user.SubscriptionCurrentPeriodEndUtc = evt.ExpirationUtc ?? user.SubscriptionCurrentPeriodEndUtc;
                user.SubscriptionWillRenew = true;
                break;

            case "CANCELLATION":
                // User cancelled but the period is still paid through. Frontend should still show
                // "premium" until period_end, but mark it as cancelled so we can surface "renew".
                user.SubscriptionStatus = SubscriptionStatus.Cancelled;
                user.SubscriptionCurrentPeriodEndUtc = evt.ExpirationUtc ?? user.SubscriptionCurrentPeriodEndUtc;
                user.SubscriptionWillRenew = false;
                break;

            case "BILLING_ISSUE":
                user.SubscriptionStatus = SubscriptionStatus.InGracePeriod;
                user.SubscriptionCurrentPeriodEndUtc = evt.ExpirationUtc ?? user.SubscriptionCurrentPeriodEndUtc;
                break;

            case "EXPIRATION":
                user.SubscriptionStatus = SubscriptionStatus.Expired;
                user.SubscriptionWillRenew = false;
                break;

            case "NON_RENEWING_PURCHASE":
                // One-time consumable (e.g. an extra background-check credit). Doesn't change
                // the recurring-subscription mirror; frontend handles consumption via RevenueCat.
                break;

            case "TRANSFER":
            case "SUBSCRIBER_ALIAS":
                // Identity events. Make sure we have the latest app_user_id stamped.
                user.RevenueCatAppUserId = evt.AppUserId;
                break;

            case "TEST":
                // RevenueCat dashboard "send test event" — log and do nothing.
                break;

            default:
                // Future event types we haven't seen yet. Don't error — log and let the watermark advance.
                break;
        }
    }

    private static void ApplySnapshotToUser(User user, RevenueCatSubscriberSnapshot snapshot)
    {
        user.SubscriptionStatus = snapshot.Status;
        user.SubscriptionEntitlement = snapshot.Entitlement ?? user.SubscriptionEntitlement;
        user.SubscriptionProductId = snapshot.ProductId ?? user.SubscriptionProductId;
        user.SubscriptionStore = snapshot.Store ?? user.SubscriptionStore;
        user.SubscriptionCurrentPeriodEndUtc = snapshot.CurrentPeriodEndUtc ?? user.SubscriptionCurrentPeriodEndUtc;
        user.SubscriptionWillRenew = snapshot.WillRenew ?? user.SubscriptionWillRenew;
        user.SubscriptionUpdatedAtUtc = DateTime.UtcNow;
        user.RevenueCatAppUserId ??= snapshot.AppUserId;
    }

    private static bool TryExtractEvent(JsonElement root, out RevenueCatEvent evt)
    {
        evt = default!;

        if (!root.TryGetProperty("event", out var eventElement))
            return false;

        var id = ReadString(eventElement, "id") ?? string.Empty;
        var type = ReadString(eventElement, "type")?.Trim().ToUpperInvariant() ?? string.Empty;
        var appUserId = ReadString(eventElement, "app_user_id")
            ?? ReadString(eventElement, "original_app_user_id")
            ?? string.Empty;
        var productId = ReadString(eventElement, "product_id");
        var entitlementId = ReadString(eventElement, "entitlement_id")
            ?? FirstEntitlementIdentifier(eventElement);
        var storeRaw = ReadString(eventElement, "store");
        var periodTypeRaw = ReadString(eventElement, "period_type");
        var eventTimestampMs = ReadLong(eventElement, "event_timestamp_ms");
        var expirationMs = ReadLong(eventElement, "expiration_at_ms");

        evt = new RevenueCatEvent
        {
            Id = id,
            Type = type,
            AppUserId = appUserId.Trim(),
            ProductId = productId,
            EntitlementId = entitlementId,
            Store = MapStore(storeRaw),
            IsTrialPeriod = string.Equals(periodTypeRaw, "TRIAL", StringComparison.OrdinalIgnoreCase),
            EventTimestampUtc = eventTimestampMs.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(eventTimestampMs.Value).UtcDateTime
                : DateTime.UtcNow,
            ExpirationUtc = expirationMs.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(expirationMs.Value).UtcDateTime
                : null
        };

        return !string.IsNullOrWhiteSpace(evt.Type);
    }

    private static string? FirstEntitlementIdentifier(JsonElement eventElement)
    {
        if (eventElement.TryGetProperty("entitlement_ids", out var ids)
            && ids.ValueKind == JsonValueKind.Array
            && ids.GetArrayLength() > 0)
        {
            var first = ids[0];
            if (first.ValueKind == JsonValueKind.String)
                return first.GetString();
        }

        return null;
    }

    private static SubscriptionStore? MapStore(string? raw)
    {
        return raw?.Trim().ToUpperInvariant() switch
        {
            "APP_STORE" => SubscriptionStore.AppStore,
            "MAC_APP_STORE" => SubscriptionStore.AppStore,
            "PLAY_STORE" => SubscriptionStore.PlayStore,
            "STRIPE" => SubscriptionStore.Stripe,
            "PROMOTIONAL" => SubscriptionStore.Promotional,
            null or "" => null,
            _ => SubscriptionStore.Unknown
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsed)
            ? parsed
            : null;
    }

    private sealed class RevenueCatEvent
    {
        public string Id { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string AppUserId { get; init; } = string.Empty;
        public string? ProductId { get; init; }
        public string? EntitlementId { get; init; }
        public SubscriptionStore? Store { get; init; }
        public bool IsTrialPeriod { get; init; }
        public DateTime EventTimestampUtc { get; init; }
        public DateTime? ExpirationUtc { get; init; }
    }
}
