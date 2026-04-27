# Feature: Identity Verification — Checkr Trust (Instant Criminal Check)

Status: **Complete.** Instant criminal check, profile reuse, multi-ruleset, badge classification with 3-month expiry, automatic renewal sweeper for active subscribers, and integration tests are all in place.

## What it does

Instant criminal background check via Checkr Trust's `POST /v1/checks` endpoint. The endpoint is synchronous — results come back on the response (201) — and we apply a Checkr-side ruleset on every call so flagged-vs-clean decisions stay consistent across users.

There are two ways to call it:

1. **First time for a user** — we send PII (`first_name`, `last_name`, optional `middle_name`, `dob`, `phone`, `source_states`). Checkr creates a profile on their side and returns a `profile_id` + the check `id` + a `results` array.
2. **Subsequent runs** — we send only the stored `profile_id`. No PII leaves our app. Checkr re-runs the check against the same person.

We persist `profile_id`, the most recent `check_id`, when we ran it, and whether it returned possible matches — directly on the `users` row. We **do not** store PII (name, DOB, phone, address) ourselves.

## Database tables

All on `users` (added by migration `20260425210544_AddCheckrProfileTracking`).

| Column | Purpose |
| ------ | ------- |
| `checkr_profile_id` | Checkr profile UUID. Once set, future checks reuse it instead of resending PII. Unique index. |
| `checkr_last_check_id` | UUID of the most recent check we ran for this user. |
| `checkr_last_check_at_utc` | When that last check ran. |
| `checkr_last_check_has_possible_matches` | `true` if the last response's `results` array was non-empty. |

No PII is persisted on our side.

## Badge classification (from check results)

The user-visible "background-checked" badge is derived directly from the check result + Checkr's ruleset filtering. Stored on the user as two columns:

| Column | Type | Notes |
| --- | --- | --- |
| `background_check_badge` | enum (`None`, `Approved`, `Denied`) | Set by `IdentityVerificationService.CreateInstantCriminalCheckAsync` on every successful Checkr response. **The backend never transitions this value on its own** — it's the immutable result of the last actual check. |
| `background_check_badge_expires_at_utc` | timestamp | Stamped to `now + 3 months` on every check. For active subscribers, `BackgroundCheckRenewalBackgroundService` automatically re-runs the check shortly after this passes; for lapsed subscribers, the timestamp simply ages out and the frontend treats the badge as expired. |

**Classification logic is delegated to Checkr's ruleset.** We send `ruleset_ids: [...]` on every check; Checkr's server applies the rules and only returns records that survive (i.e. that the ruleset considers disqualifying). So:
- `result_count == 0` → ruleset filtered everything out → **`Approved`**
- `result_count > 0` → ruleset judged at least one record disqualifying → **`Denied`**

Tweaking what counts as "minor / OK" vs "disqualifying" is a no-code change in Checkr's dashboard rulesets — we don't classify on our side.

**Multi-ruleset setup (current):**
- Felonies, all-time, conviction (`08f2b453-...`)
- Misdemeanors, 7-year, conviction (`40b1e7c2-...`)

Checkr merges + de-duplicates the rulesets server-side, so a record only needs to violate one to count.

### Renewal model

Active subscribers get **automatic renewal**. `BackgroundCheckRenewalBackgroundService` (`BuzzKeepr.Infrastructure/IdentityVerification/`) sweeps every 6 hours and re-runs the instant criminal check for any user matching:

- `BackgroundCheckBadge != None` AND
- `BackgroundCheckBadgeExpiresAtUtc < now` AND
- subscription is locally active (status not `None`/`Expired`, current period not yet ended) AND
- has an existing `CheckrProfileId` (so the renewal uses the cheap profile-reuse path, not a fresh PII send)

It calls `IIdentityVerificationService.CreateInstantCriminalCheckAsync` with empty input — the existing service code reuses the profile, runs the check, updates the badge based on results, and stamps a fresh 3-month expiry. Failures are logged and retried next pass; a subscription that lapses mid-renewal-cycle is simply skipped on subsequent passes.

For **lapsed subscribers**, nothing happens automatically. Their `BackgroundCheckBadgeExpiresAtUtc` ages past `now()` and the frontend treats the badge as expired. If they resubscribe, the very next sweep picks them up (assuming their badge expiry is past, which it will be).

This is the "we eat the Checkr cost as part of the subscription" model — the user pays for premium monthly; we re-verify them every 3 months on our dime.

## GraphQL surface

| Operation | Type | Input | Output | Auth required |
| --------- | ---- | ----- | ------ | ------------- |
| `startInstantCriminalCheck` | mutation | `phoneNumber?` (the only field — everything else is pulled from the user's verified identity) | `check_id`, `profile_id`, `result_count`, `has_possible_matches`, `error` | yes |

**Verified identity is required.** If `user.VerifiedFirstName` or `VerifiedLastName` is null (the user hasn't completed Persona), the mutation returns `"Identity verification must be completed before running a background check."` and never calls Checkr.

**Verified fields are locked.** First/middle/last name, DOB, and license state come straight from `user.Verified*` — the frontend cannot override them. This is a deliberate security choice: the whole point of running people through Persona is that we trust the identity we send to Checkr, and we'd erase that trust if we let users edit the verified data after the fact.

**Phone is the one editable field.** `user.PhoneNumber` holds the most-recently-supplied phone. The mutation accepts an optional `phoneNumber` input which, if provided, is sent to Checkr **and** persisted on the user (overwriting any prior value). If no input is supplied, we fall back to `user.PhoneNumber`. Phone is never verified, so editing it is fine.

## Wire format we send to Checkr

`POST {ApiBaseUrl}/v1/checks` with `Authorization: Bearer <oauth-token>`.

Always included:
- `check_type: "instant_criminal"`
- `ruleset_id: <CheckrTrust:RulesetId>` (when configured)

Profile-reuse path (if `user.CheckrProfileId` is set):
- `profile_id: <stored uuid>`

PII path (first time only):
- `first_name`, `last_name` (required)
- `middle_name` (if provided)
- `dob` — normalized to `YYYYMMDD` (8 digits)
- `phone` — normalized to E.164 (US default: 10 digits → `+1XXXXXXXXXX`)
- `source_states: ["XX"]` — derived from the optional `state` field; restricts which state's records to search

We do **not** send: SSN, addresses, full_name, no_middle_name, ssn — even when collected.

## Wire format we read back

We extract from the `check` response object:
- `id` → `check_id`
- `profile_id` → stored on the user
- `results` → length is the `result_count`; `> 0` ⇒ `has_possible_matches = true`

We currently don't surface the actual matched records, just the count.

## Token handling

`CheckrTrustClient` calls `POST /v1/accounts/token` (client_credentials) and caches the access token in `IMemoryCache` under `checkrtrust:access-token:{ClientId}` for `expires_in` minus a 5-minute refresh buffer. On any 401 from a check call, we force-refresh and retry once.

This is more efficient than either of the two patterns Checkr's docs suggest (refresh per call, refresh on a schedule) — we hold the token until just before expiry, refresh on demand, and self-heal if the cached token is rejected.

## External services

- **Checkr Trust API** — `https://api.checkrtrust.com`
- Required config (under `CheckrTrust:`):
  - `ApiBaseUrl` — defaults to `https://api.checkrtrust.com`
  - **`ClientId`** — user-secret
  - **`ClientSecret`** — user-secret
  - `RulesetIds` — array of ruleset UUIDs from the Checkr dashboard. Non-secret, set in `appsettings.Development.json` (and per-env).

### Test environment

Checkr Trust offers a separate test environment with deterministic mock data. Important details from [their test-accounts docs](https://docs.checkrtrust.com/test-accounts):

- **Same base URL** as production (`https://api.checkrtrust.com/v1`). Only the credentials determine which environment you're hitting.
- **Test accounts can't reach prod data and vice versa.** Calls don't bill against production.
- **Each test account needs products explicitly enabled.** When you ask your Checkr exec for test creds, ask them to **enable `instant_criminal`** specifically — otherwise you'll get `401 Unauthorized` on Create Check calls.
- **Instant Criminal test mode matches on first + last name only.** DOB, phone, state, middle name are all ignored when selecting a mock profile (though they're still sent / validated). So changing only `VerifiedFirstName` + `VerifiedLastName` is enough to pick a test outcome.

#### Mock profiles for `instant_criminal` (what we call)

| Name | Returns | Hits our rulesets → badge |
| --- | --- | --- |
| `David Thompson` | Sexual offense records + failure to register | Felony ruleset hits → **`Denied`** |
| `Oliver Smith` | Sex offense registry record | Felony ruleset hits → **`Denied`** |
| `Miguel Santos` | Sex offender registry record | Felony ruleset hits → **`Denied`** |
| `Kimberly Park` | Sex offender registry + court/DOC records | Felony ruleset hits → **`Denied`** |
| `Ava Jones` | Sexual offense + failure to register | Felony ruleset hits → **`Denied`** |
| `Marcus Williams` | Traffic violations + domestic violence | Likely hits one of the rulesets → probably **`Denied`** |
| `Tyler Brooks` | Sex offender registry + traffic violation | Felony ruleset hits → **`Denied`** |
| `Rebecca Foster` | Theft + traffic violations | Depends on disposition / level — test it |
| `Lisa Chen` | DUI + traffic violations | Depends on disposition — test it |
| `Sarah Martinez` | Speeding only | Likely filtered by both rulesets → **`Approved`** |
| `Dolores Abernathy` | Speeding only | Likely filtered → **`Approved`** |
| **Any name not in this list** (e.g. `Samuel Wemimo`, `John Smith`) | Empty results | **`Approved`** |

Note: the names appear under **other Checkr Trust products** for different scenarios (e.g. `Michael Doe` is a Cook County misdemeanor in the County-Check product, `Thomas Reed` is a clear County-Check). Those don't apply to instant_criminal.

#### Setup once your exec sends test creds

```bash
cd BuzzKeepr.Presentation
dotnet user-secrets set "CheckrTrust:ClientId"     "<test-client-id>"
dotnet user-secrets set "CheckrTrust:ClientSecret" "<test-client-secret>"
# restart the API
```

To exercise the Denied path manually:

```sql
UPDATE "Users"
SET "VerifiedFirstName"   = 'David',
    "VerifiedLastName"    = 'Thompson',
    "VerifiedMiddleName"  = NULL,
    "CheckrProfileId"     = NULL,   -- force fresh PII send instead of profile reuse
    "CheckrLastCheckId"   = NULL
WHERE "Email" = 'you@example.com';
-- run startInstantCriminalCheck → expect Denied badge
```

Restore your real verified identity (or sign in as a fresh user) when done. For prod: swap to live credentials before deploying. Render's per-service env vars handle this naturally.

## Files to watch

### Domain
- `BuzzKeepr.Domain/Entities/User.cs` (`CheckrProfileId`, `CheckrLastCheckId`, `CheckrLastCheckAtUtc`, `CheckrLastCheckHasPossibleMatches`)

### Application
- `BuzzKeepr.Application/IdentityVerification/IdentityVerificationService.cs` (`CreateInstantCriminalCheckAsync` — branches on `user.CheckrProfileId`, persists results)
- `BuzzKeepr.Application/IdentityVerification/ICheckrTrustClient.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/StartInstantCriminalCheckInput.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/CreateInstantCriminalCheckInput.cs` (carries `ProfileId`)
- `BuzzKeepr.Application/IdentityVerification/Models/CreateInstantCriminalCheckResult.cs` (carries `ProfileId`)

### Infrastructure
- `BuzzKeepr.Infrastructure/IdentityVerification/CheckrTrustClient.cs` (request building in `BuildCreateCheckRequestBody`, response parsing, token cache)
- `BuzzKeepr.Infrastructure/Configuration/CheckrTrustOptions.cs` (`ApiBaseUrl`, `ClientId`, `ClientSecret`, `RulesetId`)
- Migration: `20260425210544_AddCheckrProfileTracking`

### Presentation
- `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (`StartInstantCriminalCheckAsync`)
- `BuzzKeepr.Presentation/GraphQL/Mutations/StartInstantCriminalCheckPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Inputs/StartInstantCriminalCheckInput.cs`

## Conventions specific to this feature

- **Profile-first PII strategy.** Once we have a `checkr_profile_id`, never resend PII. Anything that overrides this should be an explicit "force re-verify" path with its own UX.
- **Ruleset on every call.** Don't allow per-call ruleset overrides from the GraphQL surface — it should always be the configured account ruleset.
- **Token cache is the third option.** The Checkr team's email suggested either (a) scheduled refresh or (b) refresh-per-call. We do (c) cache-with-TTL + 401-retry, which is strictly better than both.
- **No PII persistence.** The whole point of the profile pattern is that Checkr stores the PII for us. Don't add columns that defeat that.
- **No exceptions for business outcomes.** All Checkr failures return on `CreateInstantCriminalCheckResult.Error`.

## Common changes and where they live

| Change | Touch this |
| ------ | ---------- |
| Rotate the ruleset | Set `CheckrTrust:RulesetId` to the new value (config only — no code change) |
| Force re-verification (ignore stored profile) | Add a `forcePiiReverify` flag on `StartInstantCriminalCheckInput` (Presentation) → propagate to `IdentityVerificationService` → bypass the `hasExistingProfile` branch |
| Surface matched records to the frontend | Extend `CreateInstantCriminalCheckResult` and the GraphQL payload; map `results[*]` fields in `CheckrTrustClient` |
| Add address-based filtering | The PII branch in `BuildCreateCheckRequestBody` accepts an `addresses` array per the OpenAPI spec — extend `StartInstantCriminalCheckInput` and forward |
| Pull the report PDF | New `GET /v1/checks/{id}/report` call on `ICheckrTrustClient` returning a stream |

## Known TODOs / sharp edges

- We don't persist match details, only the count. If we ever need to show "what was found" in-app, we have to either re-fetch via `GET /v1/checks/{check_id}` or store it.
- No webhook handling. `/v1/checks` is sync so we don't need one for instant criminal, but Checkr's other check types (driver, regulated, criminal_check) are webhook-only.
- `state` on the input becomes `source_states: ["XX"]` (filter), not an address. If product needs full address-based search later, we need a structured `addresses` input rather than just a state code.
- No idempotency key on Create Check — a duplicate click could create two profiles. The unique index on `checkr_profile_id` will let the second one race-fail, but UX-wise the frontend should debounce.
