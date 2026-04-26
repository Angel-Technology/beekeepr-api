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

-- Get your raw token's hash
SELECT encode(digest('<your raw token>', 'sha256'), 'hex') AS hash;

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

Add `-H 'Origin: http://localhost:3000'`. **Expect**: 200.

### 8.3 Same call + Bearer (no Origin) → passes

Replace `Cookie` with `-H 'Authorization: Bearer <token>'`. **Expect**: 200.

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

```graphql
mutation { startPersonaInquiry {
  success createdNewInquiry inquiryId identityVerificationStatus personaInquiryStatus error
} }
```
**Expect**: `inquiryId` is `inq_…`, `createdNewInquiry: true`, `identityVerificationStatus: CREATED` or `PENDING`.

Run it again immediately → `createdNewInquiry: false`, **same inquiryId** (reuse path).

---

## 13. Persona — webhook flow (via simulator)

In the Persona dashboard, find the webhook simulator for the inquiry created in step 12. Two flavors of simulator:

- **"Send to your endpoint"**: configure your webhook URL to your ngrok HTTPS URL → `https://<ngrok>/webhooks/persona`. Make sure the same secret you set in user-secrets matches the Persona dashboard secret.
- **"Show payload + signature"**: copy the JSON body and `Persona-Signature` header value, send via curl yourself (no ngrok needed):
  ```bash
  curl -i -X POST http://localhost:5158/webhooks/persona \
    -H 'Content-Type: application/json' \
    -H 'Persona-Signature: t=...,v1=...' \
    --data-raw '<paste body>'
  ```
  Expect `HTTP/1.1 204 No Content`.

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
