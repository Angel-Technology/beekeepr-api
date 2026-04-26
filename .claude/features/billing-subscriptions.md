# Feature: Billing & Subscriptions — RevenueCat

Status: **Mirror + webhook in place; no paid surfaces gated on it yet.** The frontend uses the RevenueCat SDK as the source of truth for "what can the user see right now." The backend mirrors entitlement state into the `users` table for server-side gating (e.g. "is this user actually paid before I let them run a paid background-check renewal?").

## What it does

We do **not** own purchase flow, store integration, dunning, retries, or refunds — RevenueCat does. We:

1. Receive **webhooks** from RevenueCat on every subscription event and update a small set of mirror columns on `users`.
2. Expose `currentUser.subscription` in GraphQL so the frontend has a backend-attested view of state (useful for support tooling and as a fallback when the SDK isn't available).
3. Provide a **REST fallback** (`IRevenueCatClient.GetSubscriberAsync`) used by `BillingService.GetSubscriptionForUserAsync` when the local mirror says "not active" but the user may have just purchased and the webhook hasn't landed.

We do **not** own the price catalog. Pricing, intro offers, and product configuration live in App Store Connect / Play Console; RevenueCat reads them; the frontend renders them via the SDK's `Offerings`. We deliberately did not introduce a `plans` table — duplicating store pricing creates a sync hazard with no upside (RevenueCat already gives us analytics).

## The plan we ship today

One subscription product, both stores:

- **Premium monthly** — RevenueCat product id `premium_monthly` (mirrored under `subscription_product_id`).
  - **Introductory price:** 1 month at **$3.99**.
  - **Recurring:** **$9.99 / month** thereafter.
  - Configured per-platform in App Store Connect / Play Console as **introductory pricing** (not a free trial — the user is charged $3.99 immediately).
  - On RevenueCat side, mapped to a single entitlement (`premium`).

A second SKU for "let other people run background checks on someone" is contemplated but not built. Re-open this section when we add it.

## Database tables

Everything lives directly on `users` — no separate billing or plans tables. Migration:
- `20260426*_AddSubscriptionMirror`

| Column | Purpose |
| ------ | ------- |
| `subscription_status` | Enum: `None`, `Trialing`, `Active`, `InGracePeriod`, `Cancelled`, `Expired`. **Frontend reads this** for backend-attested status. |
| `subscription_entitlement` | RevenueCat entitlement identifier (`premium`). |
| `subscription_product_id` | RevenueCat product id (`premium_monthly`). |
| `subscription_store` | Enum: `AppStore`, `PlayStore`, `Stripe`, `Promotional`, `Unknown` — useful for support when a user reports a billing issue and we need to know which store to send them to. |
| `subscription_current_period_end_utc` | When the current paid period ends. Combined with status to compute "is this user actually entitled right now". |
| `subscription_will_renew` | Whether auto-renew is on. False after `CANCELLATION` even though the user still has access through `current_period_end_utc`. |
| `subscription_updated_at_utc` | **Watermark** — last RevenueCat `event_timestamp_ms` we processed. Drops out-of-order replays the same way Persona's watermark does. |
| `revenuecat_app_user_id` | The `appUserId` we asked the SDK to use. Convention: equals `users.id` (Guid string). Stamped on first webhook for any user. |

Indexed: `revenuecat_app_user_id` is unique.

## GraphQL surface

| Operation | Type | Input | Output | Auth |
| --------- | ---- | ----- | ------ | ---- |
| `currentUser` | query | (uses session) | `subscription { status, entitlement, productId, store, currentPeriodEndUtc, willRenew, isActive }` | yes |

`isActive` is the convenience field: it's `true` iff `status` is anything other than `None` / `Expired` AND `currentPeriodEndUtc` is in the future. Frontend should branch on this rather than reimplementing the rule.

There is no mutation surface — purchases happen entirely through the RevenueCat SDK on the frontend.

## REST surface (webhooks)

| Route | Handler |
| ----- | ------- |
| `POST /webhooks/revenuecat` | Wired in `BuzzKeepr.Presentation/Program.cs` → `BillingService.ProcessRevenueCatWebhookAsync` |

### Authentication

RevenueCat doesn't sign webhooks. They let you set a static **Authorization Header** value in their dashboard which they echo back on every POST. We compare it constant-time to `RevenueCat:WebhookAuthorizationToken` in `RevenueCatWebhookAuthorizer`. To rotate, set both the dashboard value and our config in lockstep.

On valid auth: returns `204 No Content` regardless of business outcome (so RevenueCat doesn't retry). On invalid auth: `401 Unauthorized`.

### Event handling

We map RevenueCat event types to status transitions. Unknown event types are logged and the watermark advances.

| Event type | Status set | Notes |
| --- | --- | --- |
| `INITIAL_PURCHASE` (period_type=`TRIAL`) | `Trialing` | Trial = the intro $3.99 period. |
| `INITIAL_PURCHASE` (period_type=`NORMAL`) | `Active` | If user skips intro / uses promotional offer. |
| `RENEWAL` | `Active` | Extends `current_period_end_utc`. |
| `PRODUCT_CHANGE` | `Active` | Cross-grades. We just stamp the new product id. |
| `UNCANCELLATION` | `Active` | User re-enabled auto-renew before expiry. |
| `CANCELLATION` | `Cancelled` | **Period is still paid through.** `is_active` stays true until `current_period_end_utc`. `will_renew` flips to false. |
| `BILLING_ISSUE` | `InGracePeriod` | Card declined; RevenueCat retries. |
| `EXPIRATION` | `Expired` | Access actually ended. |
| `NON_RENEWING_PURCHASE` | (no change) | One-time consumable; not a subscription event. Reserved for future "extra background-check credit" SKU. |
| `TRANSFER` / `SUBSCRIBER_ALIAS` | (no change) | Identity events; we just refresh `revenuecat_app_user_id`. |
| `TEST` | (no change) | Dashboard "send test event" — log only. |

### Out-of-order + idempotency

RevenueCat may retry on 5xx and may deliver out of order. We compare the incoming `event_timestamp_ms` to the stored `subscription_updated_at_utc` watermark; if the incoming event is **not strictly newer**, we drop it and log. Same approach as Persona — see `.claude/features/identity-verification-persona.md` for the full rationale.

### Unknown app_user_id

If a webhook references an `app_user_id` we don't recognise (and it doesn't parse as a Guid that resolves to a `users.id`), we log a warning and return `204`. A retry won't help — typically this means the SDK was initialised with the wrong identifier.

## REST fallback (`GET /v1/subscribers/{app_user_id}`)

`BillingService.GetSubscriptionForUserAsync` reads the local mirror. If the mirror says the user is **not active**, it falls back to a live REST call to RevenueCat and persists whatever it finds. This covers the race where:

1. User just purchased through the SDK (frontend immediately shows premium).
2. The webhook hasn't landed yet.
3. A server-side gate runs (e.g. paid background-check renewal mutation) and would otherwise deny the action.

Calls to RevenueCat are bounded — only happens when the mirror is "not active" and the user has a `revenuecat_app_user_id` stamped.

## External services

- **RevenueCat REST API** (`api.revenuecat.com`):
  - Subscriber lookup — `GET /v1/subscribers/{app_user_id}`
  - Auth: `Authorization: Bearer <secret_api_key>`
- Required config (`appsettings.json` → `RevenueCat:` section):
  - `ApiBaseUrl` — defaults to `https://api.revenuecat.com`
  - `SecretApiKey` (user-secret) — REST API key from the RevenueCat dashboard
  - `WebhookAuthorizationToken` (user-secret) — the value you set as the "Authorization Header" in the RevenueCat webhook config; we constant-time-compare incoming `Authorization` headers to this

## Files to watch

### Domain
- `BuzzKeepr.Domain/Entities/User.cs` (the `Subscription*` and `RevenueCatAppUserId` columns)
- `BuzzKeepr.Domain/Enums/SubscriptionStatus.cs`
- `BuzzKeepr.Domain/Enums/SubscriptionStore.cs`

### Application
- `BuzzKeepr.Application/Billing/BillingService.cs` — the event-mapping and watermark logic
- `BuzzKeepr.Application/Billing/IBillingService.cs`
- `BuzzKeepr.Application/Billing/IBillingRepository.cs`
- `BuzzKeepr.Application/Billing/IRevenueCatClient.cs`
- `BuzzKeepr.Application/Billing/Models/SubscriptionDto.cs` — also owns `IsLocallyActive` and `FromUser` shared by `AuthService` / `UserService` mappers

### Infrastructure
- `BuzzKeepr.Infrastructure/Billing/RevenueCatClient.cs` — REST client
- `BuzzKeepr.Infrastructure/Billing/RevenueCatWebhookAuthorizer.cs` — bearer-token comparison
- `BuzzKeepr.Infrastructure/Configuration/RevenueCatOptions.cs`
- `BuzzKeepr.Infrastructure/Persistence/Repositories/BillingRepository.cs`
- `BuzzKeepr.Infrastructure/DependencyInjection.cs` (registrations)

### Presentation
- `BuzzKeepr.Presentation/Program.cs` — `POST /webhooks/revenuecat` mapping
- `BuzzKeepr.Presentation/GraphQL/Types/UserGraph.cs` — `subscription` field

### Tests
- `BuzzKeepr.IntegrationTests/Billing/RevenueCatWebhookTests.cs` — covers initial purchase (trial), renewal, cancellation, expiration, stale-event drop, bad auth, unknown app_user_id
- `BuzzKeepr.IntegrationTests/Common/Fakes/FakeRevenueCatClient.cs`

## Conventions

- **`appUserId == users.id`.** Frontend MUST initialize the RevenueCat SDK with our `users.id` (Guid stringified, no surrounding braces) as the `appUserId`. If we ever need a different identifier (e.g. anonymous → identified user), `BillingService.TryResolveUserByAppUserIdAsync` handles the Guid-string fallback once.
- **Frontend is the source of truth for purchase UI.** Use the RevenueCat SDK's `Offerings` and entitlement check. `currentUser.subscription` is for backend-attested gating, not for rendering "Buy Premium" copy.
- **No subscription mutations.** Anything purchase-related goes through the SDK.

## Common changes

| Change | Touch this |
| ------ | ---------- |
| Add a new event type to mirror | `BillingService.ApplyEventToUser` switch |
| Add a new product / entitlement | RevenueCat dashboard only — backend treats new product ids generically as long as the entitlement is consistent |
| Add a new store | `BillingService.MapStore` + `RevenueCatClient.MapStore` + `SubscriptionStore` enum |
| Change "is active" rule | `SubscriptionDto.IsLocallyActive` (single source of truth — both `BillingService` and the GraphQL DTO call it) |
| Rotate webhook auth token | Update `RevenueCat:WebhookAuthorizationToken` (user-secret locally / Render env var in prod) AND the value in the RevenueCat dashboard, in lockstep |

## Known sharp edges

- **First gated surface is `startPersonaInquiry`.** Identity verification is the entrance to the funnel — users must have an active subscription before we'll burn a Persona inquiry call. See `.claude/features/identity-verification-persona.md` → "Subscription gate" for the rules. Background check renewals are auto-handled by the renewal sweeper and are not gated as a user-facing action; the manual `startInstantCriminalCheck` mutation is intentionally ungated for now (revisit if/when we expose it directly to users).
- **No unhappy-path receipt validation.** RevenueCat handles store receipt validation; we trust their webhook. If we ever stop trusting them (or want a defense-in-depth check), add a server-side `validateReceipt` mutation that takes the receipt from the SDK and forwards to RevenueCat's REST API.
- **No churn analytics on our side.** RevenueCat's dashboard owns this. If product asks for "users who cancelled in last 7 days" we'd need to query RevenueCat's REST API (export endpoints) rather than building our own report.
