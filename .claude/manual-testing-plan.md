# Manual Testing Plan

A top-down walkthrough to verify every feature end-to-end against a real running stack. Companion to `.claude/testing.md` (which covers the philosophy and the automated tests).

Work through the phases in order. Each phase carries state forward — sign-in produces the bearer token Persona/Checkr need, Persona produces the verified data Checkr requires, etc.

---

## 0. Prerequisites

Install once per machine:
- **.NET 10 SDK** (`dotnet --version` → `10.x`)
- **Docker Desktop** running
- **`dotnet-ef`** CLI: `dotnet tool install --global dotnet-ef`. Add `~/.dotnet/tools` to PATH if needed.
- **ngrok** (or Cloudflare Tunnel) — only needed if you want Persona to POST webhooks at your local API directly. If you use Persona's simulator with the "show payload + signature" option, you can skip ngrok.

External accounts you'll need credentials for:
- **Resend** — for emails. The API key lives in user-secrets.
- **Persona** — sandbox template id + webhook secret in user-secrets.
- **Checkr Trust** — client id/secret in user-secrets. ⚠️ Calls may be billed; their docs say there's no separate sandbox.
- **Google OAuth** — the client id is already in `appsettings.Development.json`. Optional for testing (skip if you don't want to grab a real ID token by hand).

---

## 1. One-time setup

```bash
cd BuzzKeepr.Presentation

dotnet user-secrets set "Email:ResendApiKey"           "re_..."
dotnet user-secrets set "Persona:ApiKey"               "persona_sandbox_..."
dotnet user-secrets set "Persona:WebhookSecrets:0"     "wbhsec_..."
dotnet user-secrets set "CheckrTrust:ClientId"         "..."
dotnet user-secrets set "CheckrTrust:ClientSecret"     "..."

# Sanity check:
dotnet user-secrets list
```

`Auth:AppApiKey` is intentionally **not** set in dev — leaving it blank opens the `createUser` gate so Banana Cake Pop / Strawberry Shake can hit it without a header. Set it later if you want to test the prod-style behavior.

---

## 2. Boot the stack

```bash
# 1. Postgres in Docker
docker compose up -d
docker compose ps                                       # confirm 'healthy'

# 2. Apply pending migrations
dotnet ef database update \
  --project BuzzKeepr.Infrastructure \
  --startup-project BuzzKeepr.Presentation \
  --context BuzzKeeprDbContext

# 3. Run the API
dotnet run --project BuzzKeepr.Presentation
```

Confirm in the logs:
- `Now listening on: http://0.0.0.0:5158`
- `Application started`
- One-time on first boot: `Deleted N aged sessions older than ...` from the cleanup background service (proves it's running).

Open Banana Cake Pop at <http://localhost:5158/graphql>. All GraphQL ops below paste into the operations pane. Set bearer auth in the **Connection Settings → Default HTTP Headers** tab once you have a token:
```json
{ "Authorization": "Bearer <your-token>" }
```

---

## 3. Health & metadata sanity check

```bash
curl http://localhost:5158/health
# → "Healthy"

curl http://localhost:5158/
# → JSON with name, version, GraphQL/Swagger/Health URLs
```

If either fails, stop and check the boot logs.

---

## 4. Email sign-in (happy path — gets you a token)

### 4.1 Request the code

```graphql
mutation { requestEmailSignIn(input: { email: "you@example.com" }) {
  success email expiresAtUtc error
} }
```
**Expect**: `success: true`, `error: null`, `expiresAtUtc` ~15 min out.

**Verify side effects**:
- Inbox: 5-digit code arrives, rendered with the verification template
- Resend dashboard: send recorded
- DB: `SELECT * FROM "VerificationTokens" WHERE "Email"='you@example.com' ORDER BY "CreatedAtUtc" DESC LIMIT 1;` → row with `purpose='EmailSignIn'`, `consumed_at_utc=NULL`

### 4.2 Verify the code

```graphql
mutation { verifyEmailSignIn(input: { email: "you@example.com", code: "12345" }) {
  user { id email displayName imageUrl emailVerified identityVerificationStatus }
  session { token expiresAtUtc }
  error
} }
```
**Expect**: `error: null`, `user.emailVerified: true`, `session.token` is a 64-char hex string.

**Save the token** — paste it into the Default HTTP Headers tab so all subsequent calls in this session carry it.

**Verify side effects**:
- DB: `SELECT * FROM "Sessions" WHERE "UserId"='<id>'` → one row, `expires_at` ~30 days out
- DB: `SELECT "WelcomeEmailSentAtUtc" FROM "Users" WHERE "Id"='<id>'` → non-null timestamp
- Inbox: welcome email arrives (welcome template), addressed to your `firstname`
- Logs: should not see the welcome-email warning

---

## 5. Authenticated baseline

### 5.1 `currentUser`

```graphql
query { currentUser {
  id email displayName imageUrl phoneNumber
  identityVerificationStatus personaInquiryId personaInquiryStatus
  verifiedFirstName verifiedMiddleName verifiedLastName verifiedBirthdate verifiedLicenseState
  personaVerifiedAtUtc termsAcceptedAtUtc createdAtUtc
} }
```
**Expect**: full user object. All `verified*` fields and `personaInquiryId` are `null` (Persona not run yet).

Without the bearer header set: returns `null`.

### 5.2 `userById` — self

```graphql
query { userById(id: "<your-id-from-currentUser>") { id email } }
```
**Expect**: returns your row.

### 5.3 `userById` — stranger (RLS)

```graphql
query { userById(id: "00000000-0000-0000-0000-000000000000") { id email } }
```
**Expect**: `null`. Same response if you query someone else's real id — no enumeration leakage.

### 5.4 `acceptTerms`

```graphql
mutation { acceptTerms { user { id termsAcceptedAtUtc } error } }
```
**Expect**: `error: null`, `user.termsAcceptedAtUtc` ~ now.

Without bearer: `error: "Authentication is required."`

---

## 6. Wrong-code lockout

Request a fresh code (`requestEmailSignIn`), but submit the wrong one 5 times:

```graphql
mutation { verifyEmailSignIn(input: { email: "you@example.com", code: "00000" }) { user { id } error } }
```
Then submit the **real** code on the 6th attempt — should still return `Error: "Invalid or expired token."` because the token's `failed_attempts` exceeded the limit.

**Verify**: `SELECT "FailedAttempts" FROM "VerificationTokens" WHERE "Email"='you@example.com' ORDER BY "CreatedAtUtc" DESC LIMIT 1;` → 5.

---

## 7. Sliding session TTL

Backdate the session's `LastSeenAtUtc` to simulate 25 hours of activity gap. In psql:

```sql
docker compose exec postgres psql -U postgres -d buzzkeepr_dev

-- One-time per database: enable pgcrypto so we can compute SHA-256 in SQL.
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Get your raw token's hash. The upper() matters — our HashToken returns
-- UPPERCASE hex via Convert.ToHexString, but encode(..., 'hex') returns lowercase.
SELECT upper(encode(digest('<your raw token>', 'sha256'), 'hex')) AS hash;

-- Backdate using that hash
UPDATE "Sessions"
SET "LastSeenAtUtc" = now() - interval '25 hours'
WHERE "TokenHash" = '<hash from previous query>';

-- Note the current ExpiresAtUtc
SELECT "ExpiresAtUtc" FROM "Sessions" WHERE "TokenHash" = '<hash>';
```

Run `currentUser` again from Banana Cake Pop. Re-query the session row:
```sql
SELECT "ExpiresAtUtc", "LastSeenAtUtc" FROM "Sessions" WHERE "TokenHash" = '<hash>';
```
**Expect**: `ExpiresAtUtc` advanced to ~30 days from now, `LastSeenAtUtc` ~ now.

---

## 8. CSRF protection

Banana Cake Pop sends `Origin` automatically, so the CSRF middleware lets you through. Use curl to simulate a malicious cross-origin POST.

### 8.1 Cookie + no Origin + no Bearer → blocked

```bash
curl -i -X POST http://localhost:5158/graphql \
  -H 'Content-Type: application/json' \
  -H 'Cookie: buzzkeepr_session=<your-token>' \
  -d '{"query":"query { currentUser { id } }"}'
```
**Expect**: `HTTP/1.1 403 Forbidden`, body `Forbidden: cross-origin request rejected by CSRF protection.`

### 8.2 Same call + allowed Origin → passes

```bash
curl -i -X POST http://localhost:5158/graphql \
  -H 'Content-Type: application/json' \
  -H 'Origin: http://localhost:3000' \
  -H 'Cookie: buzzkeepr_session=<your-token>' \
  -d '{"query":"query { currentUser { id } }"}'
```
**Expect**: 200 with the user object.

### 8.3 Same call + Bearer (no Cookie, no Origin) → passes

```bash
curl -i -X POST http://localhost:5158/graphql \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer <your-token>' \
  -d '{"query":"query { currentUser { id } }"}'
```
**Expect**: 200. Bearer auth is CSRF-immune by design — no cookie means no CSRF check fires at all.

---

## 9. createUser app-key gate

Default dev: gate is open.

```graphql
mutation { createUser(input: { email: "manual@example.com", displayName: "Manual" }) {
  user { id email } error
} }
```
**Expect**: success.

To test the prod-style gate locally:
```bash
cd BuzzKeepr.Presentation
dotnet user-secrets set "Auth:AppApiKey" "test-app-key"
# Restart the API
```
Now the same mutation **without** an `X-App-Api-Key` header returns `error: "Forbidden: missing or invalid X-App-Api-Key header."`. Add the header (in Banana Cake Pop's Default HTTP Headers tab) → succeeds:
```json
{ "Authorization": "Bearer ...", "X-App-Api-Key": "test-app-key" }
```
Remove the user-secret afterwards or keep it set for the rest of the run.

---

## 10. Sign out

```graphql
mutation { signOut { success } }
```
**Expect**: `success: true`.

Re-run `currentUser` → `null`. The token is dead.

DB: `SELECT "RevokedAtUtc" FROM "Sessions" WHERE "TokenHash"='<hash>';` → non-null timestamp.

Sign back in (`requestEmailSignIn` → `verifyEmailSignIn`) before continuing — you'll need a fresh token for the next phases.

---

## 11. Google sign-in (optional — needs a real ID token)

Easiest way to grab one: save this as `google.html` and open in a browser.

```html
<!DOCTYPE html><html><head>
<script src="https://accounts.google.com/gsi/client" async></script>
</head><body>
<div id="g_id_onload"
     data-client_id="1065098379708-9h86f9ciia544o173jpj7q6c8ebvfoj4.apps.googleusercontent.com"
     data-callback="onSignIn"></div>
<div class="g_id_signin" data-type="standard"></div>
<script>function onSignIn(r) { console.log("ID TOKEN:", r.credential); navigator.clipboard.writeText(r.credential); }</script>
</body></html>
```
Click the button → ID token printed in DevTools console and copied to clipboard.

```graphql
mutation { signInWithGoogle(input: { idToken: "eyJh..." }) {
  user { id email displayName imageUrl emailVerified }
  session { token expiresAtUtc }
  error
} }
```
**Expect**: `user.imageUrl` populated with your Google profile-picture URL. `external_accounts` row exists in DB with `Provider='Google'`. Welcome email sent (if first time for this email).

---

## 12. Persona — start an inquiry

(Make sure you're signed in with a bearer token.)

### 12a. Subscription gate (negative case — run this first)

A fresh user has no active subscription, so the gate should block. Before doing anything else:

```graphql
mutation { startPersonaInquiry {
  success createdNewInquiry inquiryId subscriptionRequired error
} }
```

**Expect**: `success: false`, `subscriptionRequired: true`, `error: "Active subscription required to start identity verification."` Persona dashboard shows **no** new inquiry — the gate blocked before any Persona call.

### 12b. Mark the user as subscribed and retry

To unblock without going through the actual RevenueCat purchase loop, fire a synthetic `INITIAL_PURCHASE` webhook (see phase 16.6a) — that flips `SubscriptionStatus` to `Trialing`, which is enough to satisfy the gate.

Or, for a quick local hack:

```sql
UPDATE "Users"
SET "SubscriptionStatus" = 'Trialing',
    "SubscriptionEntitlement" = 'premium',
    "SubscriptionProductId" = 'premium_monthly',
    "SubscriptionStore" = 'AppStore',
    "SubscriptionCurrentPeriodEndUtc" = now() + interval '7 days',
    "SubscriptionWillRenew" = true
WHERE "Email" = 'you@example.com';
```

### 12c. Now create the inquiry

```graphql
mutation { startPersonaInquiry {
  success createdNewInquiry inquiryId identityVerificationStatus personaInquiryStatus error subscriptionRequired
} }
```
**Expect**: `inquiryId` is `inq_…`, `createdNewInquiry: true`, `subscriptionRequired: false`, `identityVerificationStatus: CREATED` or `PENDING`.

Run it again immediately → `createdNewInquiry: false`, **same inquiryId** (reuse path — works even if you re-`UPDATE` the sub back to `Expired` first, since reuse bypasses the gate).

---

## 13. Persona — webhook flow

There are **two distinct things to verify** here, and both matter:

| What | Why test it |
| --- | --- |
| **13.A** Our handler processes Persona's envelope shape correctly | Catches bugs in our parsing / status-mapping / DB writes — independent of network |
| **13.B** Persona's actual servers can reach us, sign payloads we accept, and deliver successfully | Catches bugs in webhook URL config, secret mismatch, ngrok tunnel, signature format drift |

13.A is fast (no external dep). 13.B is slow but proves the integration works end-to-end. Do A first to confirm the handler is sound, then B to confirm the wiring.

---

### 13.A — Local handler test (`scripts/send-persona-webhook.sh`)

A bash helper in the repo builds the payload + computes the HMAC signature + POSTs it to your local API directly (no Persona infra, no ngrok needed). Reads the webhook secret from your user-secrets automatically.

```bash
# Run startPersonaInquiry first so the inq_... id is stored on your user row.
./scripts/send-persona-webhook.sh inq_xxxxx started
./scripts/send-persona-webhook.sh inq_xxxxx completed
./scripts/send-persona-webhook.sh inq_xxxxx approved
```

Override the endpoint to test against an ngrok tunnel or a deployed env:
```bash
./scripts/send-persona-webhook.sh inq_xxxxx approved \
  https://yourtunnel.ngrok.app/webhooks/persona \
  wbhsec_explicit_secret
```

Test the stale-event guard by setting an older `updated-at`:
```bash
STALE_AT=$(date -u -v-1H +"%Y-%m-%dT%H:%M:%S.000Z") \
  ./scripts/send-persona-webhook.sh inq_xxxxx completed
```
Logs should show `Skipping stale Persona webhook for inquiry inq_… : event @… is not newer than stored @…`.

What this proves: the handler parses, verifies, maps, and writes correctly. **What it doesn't prove**: that Persona's actual signing format and envelope still matches what we expect.

---

### 13.B — Real Persona-driven test (proves the integration)

This is what you should run **before going to production** with any new env.

**One-time setup per testing run:**

1. Boot ngrok if you don't have it running: `ngrok http 5158`. Note the `https://<random>.ngrok.app` URL.
2. In the Persona dashboard → **Webhooks** (or your inquiry template's webhook config) → set **Destination URL** to `https://<ngrok>/webhooks/persona`.
3. Confirm the **webhook secret** in Persona's dashboard matches what's in your local user-secrets:
   ```bash
   dotnet user-secrets list --project BuzzKeepr.Presentation | grep WebhookSecrets
   ```
   If they don't match, copy Persona's value into your user-secrets and restart the API:
   ```bash
   dotnet user-secrets set "Persona:WebhookSecrets:0" "wbhsec_..."
   ```

**Trigger a real event** — three ways, in order of fidelity:

1. **"Send test event" from Persona's webhook config page** — Persona generates a synthetic inquiry, signs it with the configured secret, and POSTs to your URL. Cleanest first test: proves the URL is reachable, the secret matches, and the signature format is what we expect.
2. **Manually transition an existing inquiry's status from Persona's dashboard** — most inquiries can be advanced by an admin. This produces a real `inquiry.{status}` event with your actual `inq_...` id.
3. **Complete the hosted flow** at `https://withpersona.com/verify?inquiry-id=<your-inq-id>` (or via the SDK in your frontend) using Persona's [sandbox documents](https://docs.withpersona.com/). This is the gold-standard test — fires a real `started → completed → approved` sequence AND attaches actual government-ID verification data, so `verified_*` columns will populate when you query `currentUser`.

**Watch for in your API logs as Persona delivers events:**
```
Processed Persona webhook for inquiry inq_<your-id>. User <user-id> now has identity status Pending
Processed Persona webhook for inquiry inq_<your-id>. User <user-id> now has identity status Completed
Processed Persona webhook for inquiry inq_<your-id>. User <user-id> now has identity status Approved
```

If Persona's dashboard shows the delivery as **failed** (red) but your API logs are silent, the webhook didn't reach you — usually:
- ngrok URL is stale (restart ngrok and update the Persona destination)
- Or the secret in Persona doesn't match the one in user-secrets (signature verification failed → 401 → Persona logs failed delivery)

If delivery shows **successful** (green) but our user state didn't update, the inquiry id Persona is sending doesn't match `users.persona_inquiry_id` for any user. Confirm with:
```sql
SELECT "Id", "PersonaInquiryId", "IdentityVerificationStatus" FROM "Users" WHERE "Id" = '<your-id>';
```

---

### Suggested simulation order

| # | Event | Expect after running `currentUser` |
| - | ----- | ---------------------------------- |
| 1 | `inquiry.started` | `identityVerificationStatus: PENDING` |
| 2 | `inquiry.completed` | `COMPLETED`, `verifiedFirstName`/`verifiedMiddleName`/`verifiedLastName`/`verifiedBirthdate`/`verifiedLicenseState` populated, `personaVerifiedAtUtc` set |
| 3 | `inquiry.approved` | `APPROVED`. **Logs**: should NOT show another `Persona inquiry creation`-style call — verified data already present, idempotency rule skips the re-fetch. |
| 4 | Replay `inquiry.completed` with the same/older `updated-at` | Logs: `Skipping stale Persona webhook for inquiry inq_… : event @… is not newer than stored @…` — DB unchanged. |
| 5 | `inquiry.declined` (use a fresh user) | `DECLINED`, no verified data. |
| 6 | `inquiry.marked-for-review` (fresh user) | `NEEDS_REVIEW`. |

### Bad-signature smoke test

```bash
curl -i -X POST http://localhost:5158/webhooks/persona \
  -H 'Content-Type: application/json' \
  -H 'Persona-Signature: t=12345,v1=garbage' \
  -d '{}'
```
**Expect**: `HTTP/1.1 401 Unauthorized`. Logs note signature verification failed.

---

## 14. Checkr — first run

> **Highly recommended**: use Checkr Trust's **test environment credentials** for this phase. Ask your account exec to provision a test account AND **enable the `instant_criminal` product** for it (otherwise you'll get 401 Unauthorized). Test mode uses the same base URL as production; only credentials determine the environment, and calls don't bill against prod. Swap in:
> ```bash
> dotnet user-secrets set "CheckrTrust:ClientId" "<test-id>"
> dotnet user-secrets set "CheckrTrust:ClientSecret" "<test-secret>"
> ```
> Restart the API. In test mode, `instant_criminal` matches on **first + last name only** (DOB / phone / state are sent and validated but ignored when picking the mock outcome). Names not in the mock list (e.g. `Samuel Wemimo`) return empty results → `Approved`. See `.claude/features/identity-verification-checkr.md` for the full list of mock names that produce Denied outcomes.

(Persona must be Approved on this user. If not, skip to phase 16.)

```graphql
mutation { startInstantCriminalCheck(input: { phoneNumber: "+14155552671" }) {
  success checkId profileId resultCount hasPossibleMatches error
} }
```
**Expect**: `success: true`, `checkId` and `profileId` are UUIDs, `error: null`.

**Verify side effects**:
- DB: `SELECT "CheckrProfileId", "CheckrLastCheckId", "CheckrLastCheckAtUtc", "CheckrLastCheckHasPossibleMatches", "PhoneNumber" FROM "Users" WHERE "Id"='<id>';` → all populated
- Checkr Trust dashboard: a new check appears under your account, tied to a fresh profile

---

## 15. Checkr — profile reuse

Run again with no input:
```graphql
mutation { startInstantCriminalCheck(input: {}) { success checkId profileId error } }
```
**Expect**: `success: true`, **same `profileId`** as the first run, **new `checkId`**. In Checkr's dashboard the second check is tied to the same profile (no duplicate profile).

---

## 16. Checkr — without verified identity (negative case)

Sign in as a fresh user (don't run Persona). Then:
```graphql
mutation { startInstantCriminalCheck(input: {}) { success error } }
```
**Expect**: `error: "Identity verification must be completed before running a background check."`. Checkr dashboard: no new check.

---

## 16.5 Checkr — Denied path (test-env only)

Skip if you're using prod credentials. With Checkr test creds + the deterministic mock profiles, you can verify the Denied badge end-to-end without making up names of real felons:

```sql
-- Force a re-PII-send by nulling the existing profile, swap the name to a mock that
-- produces records surviving our rulesets, and re-run. Restore afterward.
-- David Thompson → sex offense + failure to register (felony ruleset hits → Denied)
UPDATE "Users"
SET "VerifiedFirstName" = 'David',
    "VerifiedLastName"  = 'Thompson',
    "VerifiedMiddleName"= NULL,
    "CheckrProfileId"   = NULL,
    "CheckrLastCheckId" = NULL
WHERE "Email" = 'you@example.com';
```

Run `mutation { startInstantCriminalCheck(input: {}) { success hasPossibleMatches resultCount error } }`.

**Expect**: `hasPossibleMatches: true`, `resultCount > 0`. After:
```sql
SELECT "BackgroundCheckBadge", "BackgroundCheckBadgeExpiresAtUtc"
FROM "Users" WHERE "Email" = 'you@example.com';
```
→ `BackgroundCheckBadge = Denied`, expiry stamped 3 months out.

Then restore your real verified identity (or do a fresh sign-in for a clean test user) before continuing.

---

## 16.6 Billing — RevenueCat webhook simulation

We test the webhook handler with curl rather than going through the full RN-app + RevenueCat-sandbox loop. The integration tests already exercise the same parsing and state-transition code; this section is for sanity-checking that the deployed surface accepts the auth header, persists the mirror, and ignores stale events.

### Setup (one time)

Set a local webhook auth token. This is the value RevenueCat would echo back as the `Authorization` header in production; locally you pick anything that's hard to guess:

```bash
dotnet user-secrets set "RevenueCat:WebhookAuthorizationToken" "rc_local_test_pick_something_random" --project BuzzKeepr.Presentation
dotnet user-secrets set "RevenueCat:SecretApiKey" "rc_test_dummy_secret_for_rest_fallback" --project BuzzKeepr.Presentation
```

Restart the API. Pick a signed-in user's `id` (Guid) from phase 5 — call it `<USER_ID>` below. By convention, `revenuecat_app_user_id` equals `users.id`, so we use the Guid as `app_user_id` in every event.

### 16.6a Initial purchase (trial — the $3.99 intro period)

```bash
USER_ID=<paste-your-user-guid>
TOKEN="rc_local_test_pick_something_random"
NOW_MS=$(($(date +%s) * 1000))
EXP_MS=$(($(date +%s) * 1000 + 7 * 24 * 60 * 60 * 1000))

curl -i -X POST http://localhost:5000/webhooks/revenuecat \
  -H "Authorization: $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"event\": {
      \"id\": \"evt_initial_$(uuidgen)\",
      \"type\": \"INITIAL_PURCHASE\",
      \"app_user_id\": \"$USER_ID\",
      \"product_id\": \"premium_monthly\",
      \"entitlement_ids\": [\"premium\"],
      \"store\": \"APP_STORE\",
      \"period_type\": \"TRIAL\",
      \"event_timestamp_ms\": $NOW_MS,
      \"expiration_at_ms\": $EXP_MS
    }
  }"
```

**Expect:** `204 No Content`. Verify in DB:

```sql
SELECT "SubscriptionStatus", "SubscriptionEntitlement", "SubscriptionProductId",
       "SubscriptionStore", "SubscriptionWillRenew", "SubscriptionCurrentPeriodEndUtc",
       "SubscriptionUpdatedAtUtc", "RevenueCatAppUserId"
FROM "Users" WHERE "Id" = '<USER_ID>';
```

→ `Trialing`, `premium`, `premium_monthly`, `AppStore`, `true`, period-end ~7 days out, watermark stamped, app_user_id == users.id.

Verify via GraphQL too — `currentUser` should now expose it:

```graphql
query { currentUser { subscription { status entitlement productId store currentPeriodEndUtc willRenew isActive } } }
```

→ `isActive: true`, status `TRIALING`.

### 16.6b Renewal (extend the period)

```bash
NOW_MS=$(($(date +%s) * 1000))
EXP_MS=$(($(date +%s) * 1000 + 30 * 24 * 60 * 60 * 1000))

curl -i -X POST http://localhost:5000/webhooks/revenuecat \
  -H "Authorization: $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"event\": {
      \"id\": \"evt_renewal_$(uuidgen)\",
      \"type\": \"RENEWAL\",
      \"app_user_id\": \"$USER_ID\",
      \"product_id\": \"premium_monthly\",
      \"entitlement_ids\": [\"premium\"],
      \"store\": \"APP_STORE\",
      \"period_type\": \"NORMAL\",
      \"event_timestamp_ms\": $NOW_MS,
      \"expiration_at_ms\": $EXP_MS
    }
  }"
```

**Expect:** `204`. DB: `SubscriptionStatus` flips to `Active`, `SubscriptionCurrentPeriodEndUtc` advances ~30 days.

### 16.6c Cancellation (user cancels but period still paid)

```bash
NOW_MS=$(($(date +%s) * 1000))

curl -i -X POST http://localhost:5000/webhooks/revenuecat \
  -H "Authorization: $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"event\": {
      \"id\": \"evt_cancel_$(uuidgen)\",
      \"type\": \"CANCELLATION\",
      \"app_user_id\": \"$USER_ID\",
      \"product_id\": \"premium_monthly\",
      \"entitlement_ids\": [\"premium\"],
      \"store\": \"APP_STORE\",
      \"period_type\": \"NORMAL\",
      \"event_timestamp_ms\": $NOW_MS,
      \"expiration_at_ms\": $(($(date +%s) * 1000 + 30 * 24 * 60 * 60 * 1000))
    }
  }"
```

**Expect:** `204`. DB: status `Cancelled`, `WillRenew=false`, but `isActive: true` from GraphQL because the period hasn't ended yet (this is the right behavior — user keeps premium until period_end).

### 16.6d Stale event drop (out-of-order replay)

Send a `CANCELLATION` with an `event_timestamp_ms` *older* than the watermark we just stamped:

```bash
STALE_MS=$(($(date +%s) * 1000 - 10 * 60 * 1000))  # 10 min ago

curl -i -X POST http://localhost:5000/webhooks/revenuecat \
  -H "Authorization: $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"event\": {
      \"id\": \"evt_stale_$(uuidgen)\",
      \"type\": \"EXPIRATION\",
      \"app_user_id\": \"$USER_ID\",
      \"product_id\": \"premium_monthly\",
      \"entitlement_ids\": [\"premium\"],
      \"store\": \"APP_STORE\",
      \"period_type\": \"NORMAL\",
      \"event_timestamp_ms\": $STALE_MS,
      \"expiration_at_ms\": 0
    }
  }"
```

**Expect:** `204` (we always 204 so RevenueCat doesn't retry), but **state is unchanged** in the DB and the API logs:

```
Skipping stale RevenueCat event evt_stale_…/EXPIRATION: @… not newer than stored @…
```

### 16.6e Bad auth (rejected)

```bash
curl -i -X POST http://localhost:5000/webhooks/revenuecat \
  -H "Authorization: not-the-right-token" \
  -H "Content-Type: application/json" \
  -d '{"event":{"type":"INITIAL_PURCHASE","app_user_id":"whatever"}}'
```

**Expect:** `401 Unauthorized`. DB unchanged.

### 16.6f Reset before next test run

```sql
UPDATE "Users"
SET "SubscriptionStatus" = 'None',
    "SubscriptionEntitlement" = NULL,
    "SubscriptionProductId" = NULL,
    "SubscriptionStore" = NULL,
    "SubscriptionCurrentPeriodEndUtc" = NULL,
    "SubscriptionWillRenew" = NULL,
    "SubscriptionUpdatedAtUtc" = NULL,
    "RevenueCatAppUserId" = NULL
WHERE "Id" = '<USER_ID>';
```

### Notes

- **Real RevenueCat → backend** can't be tested locally without a public URL. Use the develop Render service (`https://buzzkeepr-api-develop.onrender.com/webhooks/revenuecat`) configured in the RevenueCat dashboard, or expose localhost via cloudflared / ngrok. RevenueCat also has a "Send Test Event" button on each webhook config row that fires a `TEST` event — useful for confirming auth + reachability without needing a real purchase.
- **REST fallback path** (`IBillingService.GetSubscriptionForUserAsync`) isn't exercised through `currentUser`, which reads the mirror directly. It only fires when something explicitly calls the service — e.g. a future paid-surface gate. The integration tests cover it.

---

## 16.7 Background check renewal sweeper (auto-renewal for active subscribers)

`BackgroundCheckRenewalBackgroundService` runs every 6 hours and re-runs the Checkr instant criminal check for any user whose badge has expired *and* whose subscription is still active. To trigger immediately without waiting:

1. Make sure the user has a Checkr profile already (run phase 14 at least once).
2. Make sure the user has an active subscription (run phase 16.6a to fire an `INITIAL_PURCHASE` webhook).
3. Force the badge expiry into the past:

```sql
UPDATE "Users"
SET "BackgroundCheckBadgeExpiresAtUtc" = now() - interval '1 hour'
WHERE "Email" = 'you@example.com';
```

4. Restart the API — the sweeper runs immediately on host startup.
5. Watch logs:

```
Renewing Checkr badge for user <id> (sub active, badge expired @ <ts>).
Background check renewal sweep: 1/1 renewed, 0 failed.
```

6. Verify in DB:

```sql
SELECT "BackgroundCheckBadge", "BackgroundCheckBadgeExpiresAtUtc",
       "CheckrLastCheckId", "CheckrLastCheckAtUtc"
FROM "Users" WHERE "Email" = 'you@example.com';
```

→ `CheckrLastCheckId` is a fresh UUID, `CheckrLastCheckAtUtc` is just now, `BackgroundCheckBadgeExpiresAtUtc` is ~3 months in the future.

**Negative case (subscription not active):**

```sql
UPDATE "Users"
SET "BackgroundCheckBadgeExpiresAtUtc" = now() - interval '1 hour',
    "SubscriptionStatus" = 'Expired'
WHERE "Email" = 'you@example.com';
```

Restart the API. Logs show no renewal for this user; their badge expiry stays in the past. The frontend will treat the badge as invalid.

---

## Optional: verify Sentry is wired

Skip this if you haven't set `Sentry:Dsn` yet. Set it via `dotnet user-secrets set "Sentry:Dsn" "https://...@...sentry.io/..."` and restart the API.

To prove Sentry is sending: temporarily add `throw new InvalidOperationException("sentry-smoke-test");` at the top of any GraphQL resolver (e.g. `CurrentUserAsync`), restart, call the resolver, watch your Sentry dashboard — the exception with stack trace + request context should land within seconds. Remove the throw.

If the DSN is blank or unset, the Sentry SDK is a no-op (no events sent, no errors). See [`.claude/observability.md`](../observability.md) for the full setup.

---

## 17. Welcome email sweeper (safety-net)

Simulate a missed welcome email:
```sql
UPDATE "Users"
SET "WelcomeEmailSentAtUtc" = NULL,
    "CreatedAtUtc" = now() - interval '10 minutes'
WHERE "Email" = 'you@example.com';
```
Wait up to 15 min, **or** restart the API to trigger an immediate sweep on startup.

Watch logs for:
```
Welcome email sweep delivered 1/1 pending welcomes.
```
Inbox: welcome email arrives. DB: `WelcomeEmailSentAtUtc` is now non-null.

---

## 18. Session cleanup background service

Insert a stale revoked/expired session:
```sql
INSERT INTO "Sessions" ("Id", "UserId", "TokenHash", "CreatedAtUtc", "ExpiresAtUtc")
VALUES (gen_random_uuid(), '<your-user-id>', 'bogus-hash-for-cleanup', now() - interval '40 days', now() - interval '10 days');
```
Restart the API. Logs on startup:
```
Deleted 1 aged sessions older than ...
```
And the row is gone.

---

## 19. Reset between full runs

Wipe all DB state and start fresh:
```bash
docker compose down -v       # drops the volume
docker compose up -d
dotnet ef database update --project BuzzKeepr.Infrastructure --startup-project BuzzKeepr.Presentation --context BuzzKeeprDbContext
```

Or just truncate users (cascades to sessions, external_accounts, verification_tokens):
```sql
TRUNCATE "Users" CASCADE;
```

---

## 20. Coverage map

Once you've worked through phases 4–18, the following are end-to-end-verified against a real running stack:

| Feature | Phase(s) |
| --- | --- |
| Email sign-in (request + verify) | 4 |
| Welcome email (inline + sweeper) | 4, 17 |
| Bearer auth | 4, 5 |
| Cookie auth + sliding TTL | 4, 7 |
| CSRF middleware | 8 |
| `createUser` app-key gate | 9 |
| Sign-out + session revocation | 10 |
| Google sign-in + image capture | 11 |
| `currentUser` | 5, throughout |
| `userById` row-level security | 5 |
| `acceptTerms` | 5 |
| Failed-attempts lockout | 6 |
| Persona inquiry start + reuse | 12 |
| Persona webhook (signature, status mapping, verified-data fetch) | 13 |
| Persona out-of-order tolerance | 13 (#4) |
| Persona idempotency | 13 (#3) |
| Checkr instant check + phone capture | 14 |
| Checkr profile reuse | 15 |
| Checkr requires Persona-verified identity | 16 |
| Session cleanup background service | 18 |

If any one of these doesn't behave as the doc says, stop and dig in — the corresponding integration test should also fail, so cross-check there.
