# Feature: Identity Verification — Persona

Status: **Complete.** Inquiry creation, signature verification, out-of-order tolerance, idempotency, verified-data persistence, deferred-welcome trigger, and integration tests are all in place.

## What it does

Government-ID verification through the Persona hosted flow. Approval is **always async** — completing the SDK flow does not mean the user is approved, only that they submitted. Persona settles to a final status (Approved / Declined / NeedsReview) seconds-to-minutes later via webhooks.

Lifecycle (typical auto-approval template):

```
User opens Persona SDK
  │  webhook: inquiry.started        → status Pending
User submits ID + selfie, SDK fires onComplete
  │  webhook: inquiry.completed      → status Completed (verified data fetched)
Persona runs OCR / face-match / liveness / watchlist
  │  webhook: inquiry.approved       → status Approved   (1–10s after completed)
  │      OR
  │  webhook: inquiry.marked-for-review → status NeedsReview (then Approved/Declined when a human reviews)
  │      OR
  │  webhook: inquiry.declined       → status Declined
```

Backend behavior at each stage:

1. `startPersonaInquiry` (GraphQL mutation) — backend either creates a fresh inquiry on Persona (`inq_…`) or reuses an existing in-progress one if it's in a retryable state. **Subscription gate** fires here on the create-new-inquiry path — see below.
2. Frontend opens the Persona SDK with the returned `inquiryId`. Backend has no involvement until the webhooks land.
3. Persona POSTs to `/webhooks/persona` for each status transition. Backend verifies the HMAC signature, checks the event isn't stale (timestamp comparison), maps the status, and persists.
4. On `Completed` or `Approved` (whichever lands first with verified data not yet present), backend fetches government-ID data from Persona and persists `verified_*` fields plus `persona_verified_at_utc`.

## Subscription gate

`startPersonaInquiry` is the **first paid surface in the funnel**. Before we'd burn a Persona inquiry call (fresh or retry), `IdentityVerificationService` calls `IBillingService.GetSubscriptionForUserAsync(userId)` and rejects with `subscriptionRequired: true` if `IsActive` is false.

Rules:

- **Fires on `shouldCreateNewInquiry == true`:** initial attempt OR retry after `Declined`/`Expired`/`Failed`. Both paths cost us a Persona call.
- **Bypasses on existing in-progress inquiries:** if the user has an inquiry in `Created`/`Pending`/`Completed`/`Approved`/`NeedsReview`, they can keep fetching its status (free reuse path) even if their sub has lapsed mid-flow. They've already spent the inquiry; don't lock them out of seeing how it ended.
- **REST fallback:** `GetSubscriptionForUserAsync` falls back to a live RevenueCat `GET /v1/subscribers/{appUserId}` when the local mirror says inactive — protects users who just paid but whose webhook hasn't landed yet.
- **Conservative on RevenueCat outage:** if REST also fails or returns no entitlement, the gate blocks. Better to delay one user's KYC by a few minutes than let through unpaid runs.

Frontend should branch on `subscriptionRequired` (typed boolean), not on the `error` string. When true, route to the paywall.

## Database tables

Everything lives directly on `users`. Migrations:
- `20260321154755_AddPersonaIdentityVerification` (initial Persona columns)
- `20260425225310_AddPersonaInquiryUpdatedAt` (watermark column for out-of-order tolerance)
- `20260425230812_SimplifyVerifiedIdentityFields` (drops the address + license-detail columns, adds `verified_middle_name`, `verified_license_state`, `phone_number`)

| Column | Purpose |
| ------ | ------- |
| `identity_verification_status` | High-level enum (`NotStarted`, `Created`, `Pending`, `Completed`, `NeedsReview`, `Approved`, `Declined`, `Failed`, `Expired`) — **frontend reads this** |
| `persona_inquiry_id` | Persona inquiry id (`inq_…`), unique |
| `persona_inquiry_status` | Mirrors Persona's own vocabulary; for debugging, not for UI logic |
| `persona_inquiry_updated_at_utc` | Watermark — last `updated-at` we processed from Persona; used to drop stale/replayed webhooks |
| `persona_verified_at_utc` | Timestamp when verified data was first persisted |
| `verified_first_name` | From Persona's `name-first` |
| `verified_middle_name` | From Persona's `name-middle` (may be null) |
| `verified_last_name` | From Persona's `name-last` |
| `verified_birthdate` | From Persona's `birthdate` (kept as-string, generally `YYYY-MM-DD`) |
| `verified_license_state` | From Persona's `issuing-subdivision` (the state that issued the ID), normalized to upper-case 2-letter code where applicable. Used as Checkr's `source_states` filter |
| `phone_number` | **Not from Persona.** User-supplied via the frontend. Used as Checkr's `phone` field |

## GraphQL surface

| Operation | Type | Input | Output | Auth required |
| --------- | ---- | ----- | ------ | ------------- |
| `startPersonaInquiry` | mutation | (uses session) | `inquiry_id`, `identity_verification_status`, `persona_inquiry_status`, `created_new_inquiry`, `subscription_required` | yes |
| `currentUser` | query | (uses session) | includes identity-verification status fields + verified-data fields | yes |

## REST surface (webhooks)

| Route | Handler |
| ----- | ------- |
| `POST /webhooks/persona` | Wired in `BuzzKeepr.Presentation/Program.cs` → `IdentityVerificationService.ProcessPersonaWebhookAsync` |

Signature is verified via `PersonaWebhookSignatureVerifier` against any of the configured `Persona:WebhookSecrets` (array, supports zero-downtime rotation). On valid signature: returns `204 No Content` regardless of business outcome (so Persona doesn't retry). On invalid: `401 Unauthorized`.

### Enabled events (configured in Persona dashboard)

- `inquiry.started`
- `inquiry.completed`
- `inquiry.marked-for-review`
- `inquiry.approved`
- `inquiry.declined`
- `inquiry.failed`
- `inquiry.expired`

The webhook handler reads `data.attributes.payload.data.attributes.status` (the inquiry's status field), not the event type name. Both fields generally agree, but the status field is the canonical source.

### Status mapping

`MapPersonaInquiryStatus` in `IdentityVerificationService.cs` maps the wire status to our enum. Both kebab and snake-case variants are accepted defensively because Persona has used both at different times.

| Persona status (wire value) | `PersonaInquiryStatus` | `IdentityVerificationStatus` |
| --- | --- | --- |
| `created` | `Created` | `Created` |
| `started` | `Pending` (no Started enum value — informational only) | `Pending` |
| `pending` | `Pending` | `Pending` |
| `completed` | `Completed` | `Completed` |
| `needs-review` / `needs_review` | `NeedsReview` | `NeedsReview` |
| `marked-for-review` / `marked_for_review` | `NeedsReview` | `NeedsReview` |
| `approved` | `Approved` | `Approved` |
| `declined` | `Declined` | `Declined` |
| `failed` | `Failed` | `Failed` |
| `expired` | `Expired` | `Expired` |
| *anything else* | `Pending` (fallback, logged) | `Pending` |

Frontend should branch on `identityVerificationStatus`. The Persona-flavored field exists for debugging only.

### Out-of-order + idempotency handling

Persona delivers events in a queue and **may retry on any 5xx** (or after a slow 2xx). Two failure modes that ordering-naive handlers hit:

1. **Replayed events** — same event delivered twice. A naive handler re-fetches government-ID data and re-stamps `persona_verified_at_utc` on every replay, doubling Persona API calls.
2. **Out-of-order events** — `approved` arrives, then a re-delivery of an older `completed` lands. A naive handler would downgrade the user from Approved back to Completed.

Our handler defends against both via a **monotonic watermark**:

- We extract `payload.data.attributes.updated-at` from each webhook (falls back to the event's `created-at`).
- If the incoming timestamp is `≤` the stored `persona_inquiry_updated_at_utc`, we log and return without mutating anything.
- Otherwise we apply the new status and bump the watermark.

We also avoid re-fetching verified data if it's already persisted (`verified_first_name` non-null) — this prevents the `completed → approved` transition from making a second `GET /verifications/government-id/...` call to Persona.

### Side effect: deferred welcome email

Email-sign-in users have no display name when their `User` row is created, so the welcome email is intentionally deferred (see `.claude/features/authentication-email-signin.md` → "Welcome email — name-gated, hybrid trigger"). Persona is the trigger that resolves the deferral.

After persisting the verified data, `ProcessPersonaWebhookAsync` checks `WelcomeEmailSentAtUtc IS NULL && VerifiedFirstName != null` and calls `TrySendDeferredWelcomeAsync`, which sends through `IWelcomeEmailSender` using the verified first name and stamps `WelcomeEmailSentAtUtc`. Failure is swallowed and logged — the sweeper picks it up next pass.

Google sign-in users and `createUser` consumers already have a name, so they bypass this path entirely (welcome was already sent inline at user creation).

### Frontend UX guidance

The Persona SDK's `onComplete` fires when the user submits — **not** when they're approved. Treat it as "documents received, waiting for verdict."

Recommended frontend flow:

```
SDK opens                     identityVerificationStatus: Created
SDK fires onStart             identityVerificationStatus: Pending  (after `started` webhook lands)
User submits + onComplete fires
                              identityVerificationStatus: Completed
                              UI: "Verifying you..." spinner
                              → poll currentUser every 2s, max 30s
After ~1–10s                  identityVerificationStatus: Approved → ✅ verified
                                                       OR Declined → "we couldn't verify"
                                                       OR after 30s still Completed/NeedsReview
                                                          → "this is taking longer than usual,
                                                             we'll email you when it's done"
```

Always re-fetch `currentUser` on app focus / resume — there's no push-to-client mechanism.

## External services

- **Persona API** (`api.withpersona.com`):
  - Create inquiry — `POST /api/v1/inquiries`
  - List verifications on an inquiry — `GET /api/v1/inquiries/{id}?include=verifications`
  - Read government-ID verification — `GET /api/v1/verifications/government-id/{verifId}`
- Required config (`appsettings.json` → `Persona:` section):
  - `ApiKey` (user-secret)
  - `ApiBaseUrl` — defaults to `https://api.withpersona.com`
  - `InquiryTemplateId` (the `itmpl_…` template id for your KYC flow)
  - `WebhookSecrets` (array of strings — for HMAC verification, supports rotation)

## Files to watch

### Domain
- `BuzzKeepr.Domain/Entities/User.cs` (all the `Verified*` and `Persona*` columns, including `PersonaInquiryUpdatedAtUtc`)
- `BuzzKeepr.Domain/Enums/IdentityVerificationStatus.cs`
- `BuzzKeepr.Domain/Enums/PersonaInquiryStatus.cs`

### Application
- `BuzzKeepr.Application/IdentityVerification/IdentityVerificationService.cs`
- `BuzzKeepr.Application/IdentityVerification/IIdentityVerificationService.cs`
- `BuzzKeepr.Application/IdentityVerification/IIdentityVerificationRepository.cs`
- `BuzzKeepr.Application/IdentityVerification/IPersonaClient.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/CreatePersonaInquiryInput.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/CreatePersonaInquiryResult.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/StartPersonaInquiryResult.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/PersonaGovernmentIdDataResult.cs`

### Infrastructure
- `BuzzKeepr.Infrastructure/IdentityVerification/PersonaClient.cs`
- `BuzzKeepr.Infrastructure/IdentityVerification/PersonaWebhookSignatureVerifier.cs`
- `BuzzKeepr.Infrastructure/Persistence/Repositories/IdentityVerificationRepository.cs`
- `BuzzKeepr.Infrastructure/Configuration/PersonaOptions.cs`
- `BuzzKeepr.Infrastructure/Persistence/BuzzKeeprDbContext.cs` (entity config)
- Migrations: `20260321154755_AddPersonaIdentityVerification`, `20260425*_AddPersonaInquiryUpdatedAt`

### Presentation
- `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (`StartPersonaInquiryAsync`)
- `BuzzKeepr.Presentation/GraphQL/Mutations/StartPersonaInquiryPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Types/UserGraph.cs` (exposes the verification fields)
- `BuzzKeepr.Presentation/Program.cs` (`/webhooks/persona` route registration)

## Conventions specific to this feature

- **Two status fields on purpose.** `persona_inquiry_status` mirrors Persona's vocabulary verbatim. `identity_verification_status` is our product-facing status. Frontend always reads the latter.
- **Webhook secrets are an array.** The verifier tries every entry. Rotate by appending the new secret, deploying, then removing the old.
- **Reuse before recreate.** `startPersonaInquiry` reuses the existing inquiry unless its `identity_verification_status` is `Declined`, `Expired`, or `Failed` (the `RetryableStatuses` set).
- **Webhooks are async + retried + out-of-order-capable.** Always go through the `persona_inquiry_updated_at_utc` watermark check before mutating. Never assume the latest webhook is the most recent state.
- **`Completed` ≠ `Approved`.** Verified data may be present at `Completed` but the user isn't actually approved yet. UI gates on `Approved`.
- **Store only what Checkr needs.** The Verified columns are deliberately scoped to first/middle/last/dob/license-state — anything else (address, license number, expiration) gets dropped on the floor. If a future feature needs more, add a column then; don't speculatively persist.
- **Phone is not from Persona.** Persona doesn't capture it; the frontend collects it through a separate profile flow and writes to `phone_number`. Treat it like any other user-claimed field — no verification.
- **No exceptions for business outcomes.** Webhook handler returns 204 once the signature is valid; business issues are logged + reflected in status.

## Common changes and where they live

| Change | Touch this |
| ------ | ---------- |
| Add a verified field (e.g. middle name) | `User.cs` → `BuzzKeeprDbContext` config → new EF migration → `PersonaClient.GetGovernmentIdDataAsync` mapping → `UserGraph` |
| New Persona inquiry status value appears | Add a case to `MapPersonaInquiryStatus` in `IdentityVerificationService.cs`; if it warrants a new product state, also add to the two enums |
| Rotate the webhook secret | Append new value to `Persona:WebhookSecrets`, deploy, swap in Persona dashboard, then drop the old value |
| Switch KYC vendor | New `I*Client` interface + Infrastructure impl, new options class, swap registration in `Infrastructure/DependencyInjection.cs`. Keep the `identity_verification_status` enum stable so the frontend doesn't break. |
| Toggle which Persona events fire | In the Persona dashboard under the template's webhook settings |

## Known TODOs / sharp edges

- **No retry on government-ID fetch.** `GetGovernmentIdDataAsync` returns `Success=false` on any HTTP failure and we silently skip. If Persona is briefly down at webhook time, we end up `Approved` with empty `verified_*` columns. Add a retry / reconciliation job if this becomes a real problem.
- **Race between `startPersonaInquiry` save and an early webhook.** If Persona webhooks land before our local SaveChanges committed `PersonaInquiryId`, the handler logs "unknown inquiry" and bails. Persona's webhook lag (≥1s) makes this unlikely in practice.
- **`FindPassedGovernmentIdVerificationId` picks the first passing verification.** If a template ever runs multiple government-ID checks (e.g. SDK retry produces several `verification/government-id` items), we take the first array entry with `status` in (`passed`, `completed`). Almost always fine; sanity-check the actual response shape if your template changes.
- **No dedicated `persona_inquiries` table.** If we ever want history (multiple attempts, audit trail across retries), this becomes a real schema change.
- **Watermark uses Persona's clock.** If Persona's `updated-at` ever drifts backward (shouldn't, but stranger things have happened), we'd lose updates. Acceptable risk.
