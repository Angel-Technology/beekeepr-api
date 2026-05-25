# Feature: Promo Codes

Status: **Complete.** Tables, redemption mutation, and three seed codes are live. No admin surface for creating new codes yet — add new codes via migration or direct SQL.

## What it does

We let signed-in users redeem a shared promo code (e.g. `NEWBEE2026`) for a free `Buzzkeepr Pro` entitlement of a fixed duration (1 / 3 / 6 months). Each code has a global redemption cap; each user can only redeem a given code once.

1. User submits a code via `redeemPromoCode` mutation.
2. Backend validates the code (exists, active, unexpired, under cap, user hasn't redeemed it before).
3. Backend ensures the RevenueCat subscriber record exists by calling `GET /v1/subscribers/{app_user_id}` — that endpoint is idempotent and creates the subscriber if missing. Without this, the grant POST returns `404 — subscriber was not found` for any user whose frontend SDK hasn't yet called `Purchases.logIn(user.id)`.
4. Backend opens a DB transaction, inserts the `PromoRedemption` row, atomically increments `RedemptionsUsed` (the UPDATE itself enforces the cap predicate), and calls RevenueCat's promotional-entitlement grant endpoint inside the transaction. Any step failing rolls back the others.
5. After commit, the service refreshes the local subscription mirror via `IBillingService.GetSubscriptionForUserAsync` so the next `currentUser.subscription` query returns the new entitlement immediately — without waiting for the RevenueCat webhook to land.

RevenueCat then fires an `INITIAL_PURCHASE` webhook (`store: PROMOTIONAL`) shortly after, which the existing billing webhook handler processes idempotently — see [billing-subscriptions.md](billing-subscriptions.md).

## Database tables

Migration: `20260525163707_AddPromoCodes`

### `PromoCodes`

| Column | Purpose |
| ------ | ------- |
| `Id` | Primary key (Guid). |
| `Code` | The redemption string (e.g. `NEWBEE2026`). Stored uppercase; the service normalizes incoming input with `Trim().ToUpperInvariant()` so casing doesn't matter to users. Unique index. |
| `EntitlementId` | The RevenueCat **entitlement identifier** (not display name) that successful redemption grants. Mirrors `User.SubscriptionEntitlement`. |
| `Duration` | Enum stored as string: `Daily`, `ThreeDay`, `Weekly`, `Monthly`, `TwoMonth`, `ThreeMonth`, `SixMonth`, `Yearly`, `Lifetime`. Mapped to RevenueCat's wire format in `PromoCodeDurationExtensions.ToRevenueCatDuration`. |
| `MaxRedemptions` | Nullable cap. `NULL` = unlimited. |
| `RedemptionsUsed` | Incremented atomically by the redemption transaction; the conditional UPDATE is the enforcement point for the hard cap. |
| `ExpiresAtUtc` | Nullable. `NULL` = never expires. |
| `IsActive` | Soft-disable switch; redemption ignores any code where this is false. |
| `CreatedAtUtc` | When the code was created. |

### `PromoRedemptions`

| Column | Purpose |
| ------ | ------- |
| `Id` | Primary key (Guid). |
| `PromoCodeId` | FK → `PromoCodes.Id` (cascade delete). |
| `UserId` | FK → `Users.Id` (cascade delete). |
| `RedeemedAtUtc` | When the redemption succeeded. |

**Unique index on `(PromoCodeId, UserId)`** — this is what enforces the "each user can only redeem a given code once" rule. The repository detects this constraint violation (Postgres SQLSTATE `23505`) and maps it to `PromoRedemptionOutcome.AlreadyRedeemed`.

### Seeded codes

The initial migration inserts three codes. All grant the `Buzzkeepr Pro` entitlement (literal identifier including the space, matching the RevenueCat dashboard).

| Code | Duration | Max redemptions | Expires |
| ---- | -------- | --------------- | ------- |
| `NEWBEE2026` | 1 month | 500 | 2026-12-31 23:59:59 UTC |
| `BUZZIN3` | 3 months | 250 | never |
| `QUEENBEE26` | 6 months | 100 | never |

## GraphQL surface

| Operation | Type | Input | Output | Auth |
| --------- | ---- | ----- | ------ | ---- |
| `redeemPromoCode` | mutation | `{ code: String! }` | `{ subscription, error }` | yes |

`subscription` is the same `SubscriptionDto` returned by `currentUser.subscription` — frontend can use it for an optimistic UI update without a follow-up query. `error` is a user-presentable string when redemption fails; `subscription` is null in that case.

### Error strings returned

Mapped in `BillingMutations.RedeemPromoCodeAsync` from `RedeemPromoCodeResult` flags:

| Result flag | User-facing string |
| ----------- | ------------------ |
| `CodeRequired` | "Promo code is required." |
| `CodeNotFound` | "That promo code is not valid." |
| `CodeInactive` | "That promo code is no longer available." |
| `CodeExpired` | "That promo code has expired." |
| `CodeFullyRedeemed` | "That promo code has reached its redemption limit." |
| `AlreadyRedeemed` | "You have already redeemed this promo code." |
| `UserNotFound` (or no session) | "Authentication is required." |
| `GrantFailed` | "We couldn't apply the promo. Please try again in a moment." |

`GrantFailed` is the catch-all when the RevenueCat REST call returns non-2xx or throws. The transaction is rolled back, so the user can retry without ending up with a phantom redemption row. The actual reason is in the log line emitted by `RevenueCatClient.GrantPromotionalEntitlementAsync` and the warning logged by `PromoCodeService.RedeemAsync`.

## External services

- **RevenueCat REST API** (`api.revenuecat.com`):
  - Subscriber lazy-create — `GET /v1/subscribers/{app_user_id}`. Called immediately before the grant; idempotent and creates the subscriber if missing. Required for users the frontend SDK hasn't yet identified.
  - Promotional grant — `POST /v1/subscribers/{app_user_id}/entitlements/{entitlement_identifier}/promotional` with body `{ "duration": "monthly" | "three_month" | "six_month" | ... }`.
  - Auth: `Authorization: Bearer <secret_api_key>` — same key as the rest of the billing feature.
  - Path segments are URL-encoded; the configured entitlement identifier contains a space (`Buzzkeepr Pro` → `Buzzkeepr%20Pro`).
- Required config (`appsettings.json` → `RevenueCat:` section, same as billing):
  - `SecretApiKey` (user-secret / env var) — also used by `GetSubscriberAsync`.
  - `ApiBaseUrl` — defaults to `https://api.revenuecat.com`.

## Files to watch

### Domain
- `BuzzKeepr.Domain/Entities/PromoCode.cs`
- `BuzzKeepr.Domain/Entities/PromoRedemption.cs`
- `BuzzKeepr.Domain/Enums/PromoCodeDuration.cs`

### Application
- `BuzzKeepr.Application/Billing/IPromoCodeService.cs`
- `BuzzKeepr.Application/Billing/PromoCodeService.cs` — input normalization, pre-checks, post-success mirror refresh.
- `BuzzKeepr.Application/Billing/IPromoCodeRepository.cs`
- `BuzzKeepr.Application/Billing/PromoRedemptionOutcome.cs`
- `BuzzKeepr.Application/Billing/PromoCodeDurationExtensions.cs` — enum → RevenueCat wire string.
- `BuzzKeepr.Application/Billing/IRevenueCatClient.cs` — the `GrantPromotionalEntitlementAsync` method.
- `BuzzKeepr.Application/Billing/Models/RedeemPromoCodeResult.cs`

### Infrastructure
- `BuzzKeepr.Infrastructure/Persistence/Repositories/PromoCodeRepository.cs` — the transaction + conditional UPDATE + unique-violation handling.
- `BuzzKeepr.Infrastructure/Billing/RevenueCatClient.cs` — the grant POST implementation.
- `BuzzKeepr.Infrastructure/Persistence/BuzzKeeprDbContext.cs` — `PromoCode` + `PromoRedemption` entity configs.
- `BuzzKeepr.Infrastructure/Persistence/Migrations/20260525163707_AddPromoCodes.cs` — tables + seed inserts.
- `BuzzKeepr.Infrastructure/DependencyInjection.cs` (registration)

### Presentation
- `BuzzKeepr.Presentation/GraphQL/Mutations/BillingMutations.cs` — the resolver. Composed onto the existing `UserMutations` root via `[ExtendObjectType(typeof(UserMutations))]` + `.AddTypeExtension<BillingMutations>()` in `Program.cs`.
- `BuzzKeepr.Presentation/GraphQL/Mutations/RedeemPromoCodePayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Inputs/RedeemPromoCodeInput.cs`
- `BuzzKeepr.Presentation/Program.cs` — `.AddTypeExtension<BillingMutations>()` registration.

### Application DI
- `BuzzKeepr.Application/DependencyInjection.cs` (registration)

## Conventions

- **Code text is normalized to uppercase server-side.** Users can type `newbee2026`, `NewBee2026`, etc. — `PromoCodeService.RedeemAsync` runs `Trim().ToUpperInvariant()` before lookup. Seed codes are stored uppercase to match.
- **The cap is enforced by the database, not app logic.** `PromoCodeRepository.TryRedeemAsync` runs `UPDATE PromoCodes SET RedemptionsUsed = RedemptionsUsed + 1 WHERE Id = @id AND IsActive AND (ExpiresAtUtc IS NULL OR ExpiresAtUtc > NOW()) AND (MaxRedemptions IS NULL OR RedemptionsUsed < MaxRedemptions)`. If 0 rows are affected, the cap was hit (or the code expired/was deactivated between pre-check and UPDATE) — treated as `CapReached`. This replaces a `SELECT FOR UPDATE` and keeps the row-lock window to a single statement.
- **The grant call runs inside the transaction.** A failed grant rolls back the redemption row + cap increment together, so retry semantics are clean. The lock window includes the RevenueCat HTTP latency — at our volume that's fine; if redemption traffic ever becomes contended, revisit by reserving + granting in two phases with a compensating undo.
- **Per-code entitlement.** Each row owns its `EntitlementId`. Adding a code that grants a different entitlement is just a new row — no code changes needed.
- **No admin mutation.** Adding a code is a migration (or direct SQL insert) — see "Common changes" below.

## Common changes

| Change | Touch this |
| ------ | ---------- |
| Add a new promo code | Either insert directly into `PromoCodes` (`Code` uppercase, `Duration` as the enum string e.g. `Monthly`/`ThreeMonth`/`SixMonth`), or add a migration with an `InsertData` call — follow the pattern in `20260525163707_AddPromoCodes`. |
| Deactivate a code | `UPDATE "PromoCodes" SET "IsActive" = false WHERE "Code" = '...'`. Existing redemptions stay valid; further redemption attempts return `CodeInactive`. |
| Change a code's cap | `UPDATE "PromoCodes" SET "MaxRedemptions" = X WHERE "Code" = '...'`. The conditional UPDATE in `TryRedeemAsync` reads this every redemption, so the new cap takes effect immediately. |
| Extend a code's expiry | `UPDATE "PromoCodes" SET "ExpiresAtUtc" = '...' WHERE "Code" = '...'`. |
| Allow a new duration the enum doesn't have | The enum already has every RevenueCat-supported value (`Daily`, `ThreeDay`, `Weekly`, `Monthly`, `TwoMonth`, `ThreeMonth`, `SixMonth`, `Yearly`, `Lifetime`) — no change should be needed. If RevenueCat adds a new bucket, extend `PromoCodeDuration` + `PromoCodeDurationExtensions.ToRevenueCatDuration`. |
| Grant a different entitlement | Insert a code row with the other `EntitlementId` — the identifier comes from the RevenueCat dashboard's "Identifier" column (not "Display Name"). |
| Add an admin "create promo code" GraphQL mutation | New method on `BillingMutations` gated by the existing `AppApiKeyValidator` (see how `createUser` uses it); add a corresponding service method. Not built today. |

## Known sharp edges

- **No admin surface.** Code creation, deactivation, and cap adjustment are SQL-only. Build a gated mutation when this becomes operationally painful.
- **The grant call holds a DB lock across HTTP latency.** Acceptable at current volumes but a sharp edge if redemption traffic ever becomes high-concurrency on a single code. Mitigation if/when needed: two-phase reserve (insert + increment in tx 1, grant out-of-band, mark `granted_at` in tx 2 with a sweeper that reverses uncommitted reservations after N minutes).
- **Entitlement identifier mismatch fails silently.** If the value in `PromoCodes.EntitlementId` doesn't exactly match the RevenueCat dashboard's Identifier column, RevenueCat returns 404 and we return `GrantFailed`. The only signal is the warning log line from `RevenueCatClient.GrantPromotionalEntitlementAsync`. Worth grep-ing for after seeding any new code.
- **`RevenueCatAppUserId` fallback assumes the SDK convention.** When `user.RevenueCatAppUserId` is null we fall back to `user.Id.ToString()` — same convention as the rest of the billing feature. The service lazy-creates the RevenueCat subscriber under that ID before granting, so the grant itself won't 404. **But** if the frontend SDK has separately been calling `Purchases.logIn` with a *different* identifier (e.g. an `$RCAnonymousID:` from before sign-in), the backend will grant to the `user.Id`-based subscriber while the SDK on the device is reading from the other one — the redemption will look successful server-side but the user won't see the entitlement in the app. Mitigation: frontend should call `Purchases.logIn(currentUser.id)` on sign-in so both sides are referring to the same subscriber.
- **Redemption count is a count of attempts that committed, not a count of currently-active entitlements.** If a user's promotional entitlement expires (1 month later), the `RedemptionsUsed` count doesn't decrement. This is the correct semantic for a "first 500 to redeem" cap, but it's not the right number to query if you want "how many users currently have an active promo." For that, join `PromoRedemptions` to `Users.SubscriptionStatus` / `SubscriptionCurrentPeriodEndUtc`.
- **No analytics surface for which codes drove which subscriptions.** RevenueCat's dashboard shows the grant came through their REST API but doesn't know which code text was used. If product asks "how many people used `NEWBEE2026` vs `QUEENBEE26`," it has to come from a query against `PromoRedemptions` joined to `PromoCodes`.
