# Feature: Email Sign-In (Passwordless)

Status: **Complete** — used in dev and integration tests.

## What it does

Lets a user sign in without a password. The flow is:

1. User submits their email → backend generates a 5-digit numeric code, hashes it (SHA-256), stores it in `verification_tokens`, and emails the raw code via Resend.
2. User submits `email + code` → backend looks up the token, checks expiry (15 min) and `failed_attempts` (max 5), creates the `User` if it doesn't exist (with `email_verified = true`), opens a `Session`, and returns a session token.
3. The session token is also set as an HTTP-only cookie by `SessionCookieManager`.
4. `signOut` revokes the session and clears the cookie.
5. `currentUser` resolves the user from the cookie or `Authorization: Bearer <token>`.

## Database tables

| Table                 | Purpose | Key columns |
| --------------------- | ------- | ----------- |
| `users`               | The account | `id`, `email`, `email_verified`, `display_name`, `created_at`, `terms_accepted_at_utc` |
| `verification_tokens` | One-time codes | `id`, `user_id` (nullable for first-time users), `email`, `purpose`, `token_hash`, `failed_attempts`, `created_at`, `expires_at`, `consumed_at` |
| `sessions`            | Active logins | `id`, `user_id`, `token_hash`, `expires_at`, `created_at`, `last_seen_at`, `revoked_at`, `ip_address`, `user_agent` |

Token storage rule: **only the SHA-256 hash is persisted**. The raw 5-digit code is delivered via email; the raw 64-char session token is delivered via cookie/response only.

## GraphQL surface

| Operation | Type | Input | Output |
| --------- | ---- | ----- | ------ |
| `requestEmailSignIn` | mutation | `email` | success flag, email, expires-at |
| `verifyEmailSignIn` | mutation | `email`, `code` | user, session token, expires-at, error |
| `signOut` | mutation | (uses session) | success |
| `currentUser` | query | (uses session) | user with identity verification status |

## External services

- **Resend** for email delivery — `BuzzKeepr.Infrastructure/Auth/ResendEmailSignInSender.cs`.
- Required config (`appsettings.json` → `Email:` section):
  - `FrontendBaseUrl`
  - `ResendApiKey`
  - `ResendBaseUrl`
  - `SignInTemplateId` / `WelcomeTemplateId`
- The sender (`from`) and subject are configured **on each Resend template**, not in our config — the API call only sends `to`, `template.id`, and `template.variables`.

## Files to watch

### Domain
- `BuzzKeepr.Domain/Entities/User.cs`
- `BuzzKeepr.Domain/Entities/VerificationToken.cs`
- `BuzzKeepr.Domain/Entities/Session.cs`
- `BuzzKeepr.Domain/Enums/VerificationTokenPurpose.cs`

### Application
- `BuzzKeepr.Application/Auth/AuthService.cs` (the orchestration: `RequestEmailSignInAsync`, `VerifyEmailSignInAsync`, `SignOutAsync`, `GetCurrentUserAsync`)
- `BuzzKeepr.Application/Auth/IAuthService.cs`
- `BuzzKeepr.Application/Auth/IAuthRepository.cs`
- `BuzzKeepr.Application/Auth/IEmailSignInSender.cs`
- `BuzzKeepr.Application/Auth/Models/RequestEmailSignInInput.cs`
- `BuzzKeepr.Application/Auth/Models/RequestEmailSignInResult.cs`
- `BuzzKeepr.Application/Auth/Models/VerifyEmailSignInInput.cs`
- `BuzzKeepr.Application/Auth/Models/VerifyEmailSignInResult.cs`
- `BuzzKeepr.Application/Auth/Models/SignOutResult.cs`
- `BuzzKeepr.Application/Auth/Models/CurrentUserResult.cs`
- `BuzzKeepr.Application/Auth/Models/AuthSessionDto.cs`

### Infrastructure
- `BuzzKeepr.Infrastructure/Persistence/Repositories/AuthRepository.cs`
- `BuzzKeepr.Infrastructure/Auth/ResendEmailSignInSender.cs`
- `BuzzKeepr.Infrastructure/Configuration/EmailDeliveryOptions.cs`
- `BuzzKeepr.Infrastructure/Persistence/BuzzKeeprDbContext.cs` (entity config)
- Relevant migrations: `20260302022955_InitialCreate`, `20260303013207_ExpandAuthSchema`, `20260303035230_AddAuthFlows`, `20260316020653_AddPinCodeSignIn`

### Presentation
- `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (`RequestEmailSignInAsync`, `VerifyEmailSignInAsync`, `SignOutAsync`)
- `BuzzKeepr.Presentation/GraphQL/Mutations/RequestEmailSignInPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Mutations/VerifyEmailSignInPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Mutations/SignOutPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Inputs/RequestEmailSignInInput.cs`
- `BuzzKeepr.Presentation/GraphQL/Inputs/VerifyEmailSignInInput.cs`
- `BuzzKeepr.Presentation/GraphQL/Types/AuthSessionGraph.cs`
- `BuzzKeepr.Presentation/Auth/SessionCookieManager.cs`
- `BuzzKeepr.Presentation/Auth/SessionTokenResolver.cs`

## Conventions specific to this feature

- Email is **always** trimmed and lowercased before lookup.
- Code: 5 digits, 15-minute expiry, max 5 wrong attempts before the token is invalidated.
- Session token: 64 hex chars, hashed before persist, delivered both as HTTP-only cookie *and* as `session.token` in the GraphQL payload (cookie for browsers, raw token for Bearer-using mobile clients).
- Errors return on the Result object — `EmailRequired`, `InvalidToken`, `EmailDeliveryFailed`, etc. Do **not** throw.

### Session lifetime — sliding 30 days

- Initial expiry: `now + 30 days` (constants on `AuthService`: `SessionLifetime`, `SessionTouchInterval`).
- **Sliding window:** every authenticated request flows through `AuthService.GetCurrentUserAsync`. If `LastSeenAtUtc` is more than 24h old, we bump both `LastSeenAtUtc` and `ExpiresAtUtc` (to `now + 30 days`) in a single `ExecuteUpdateAsync` call. Active users effectively stay signed in indefinitely.
- The bumped expiry is surfaced on `CurrentUserResult.RefreshedSessionExpiresAtUtc`. Presentation's `SessionRefresher.ResolveAsync` re-issues the cookie with the new `Expires` attribute when (a) a bump happened and (b) the token came from the cookie (not Bearer-only).
- Always call `SessionRefresher.ResolveAsync` rather than `IAuthService.GetCurrentUserAsync` directly — otherwise the browser cookie will expire before the DB session does.

### Session cleanup

- `SessionCleanupBackgroundService` (Infrastructure, `IHostedService`) runs hourly and deletes sessions where either `ExpiresAtUtc < now - 7 days` OR `RevokedAtUtc < now - 7 days`. The 7-day grace window is for forensics (e.g. correlating a logout that happened 3 days ago with a support ticket).
- Implemented via `IAuthRepository.DeleteAgedSessionsAsync` → EF `ExecuteDeleteAsync`.

### CSRF protection

- `CsrfProtectionMiddleware` runs after CORS for every `POST /graphql` that includes the session cookie. It allows the request through if **either**:
  - `Authorization: Bearer …` is present (mobile clients are CSRF-immune), **or**
  - `Origin` (or `Referer`) matches the configured allowlist (`Cors:AllowedOrigins`, plus `localhost`/`127.0.0.1` in Development).
- Otherwise: returns `403 Forbidden`. No cookie present → no enforcement (anonymous reads from anywhere are fine).
- Allowlist is built once at startup as `CsrfOriginAllowlist` and registered as a singleton.

### Resend templates (wire contract)

The HTML lives in the **Resend dashboard** (not in this repo). Our senders pass a `template: { id, variables }` payload to Resend. If a placeholder name is renamed in the dashboard, rename it on the same line in the matching sender or it'll arrive as the literal `{{var}}` string.

| Template id | Trigger | Sender file | Variables passed |
| --- | --- | --- | --- |
| `e7042412-cef7-40fb-a805-a197ccf538b1` | `requestEmailSignIn` mutation | `BuzzKeepr.Infrastructure/Auth/ResendEmailSignInSender.cs` | `code` (5-digit), `expires_in_minutes` (int), `email` |
| `601eecd1-c163-419d-8e4b-def86652881f` | New `User` row created **and** we have a name to address them by | `BuzzKeepr.Infrastructure/Auth/ResendWelcomeEmailSender.cs` | `firstname` (first token of name, `"there"` fallback), `email` |

Template IDs bind to `Email:SignInTemplateId` and `Email:WelcomeTemplateId` in `appsettings*.json` (non-secret).

### Welcome email — name-gated, hybrid trigger

The welcome only fires once we have a name to greet the user with. Otherwise the template renders "Welcome to BuzzKeepr, there." which we want to avoid.

- **Inline send (Google + createUser):** `AuthService.SignInWithGoogleAsync` and `UserService.CreateAsync` always have a name in hand (Google profile / explicit `displayName`), so they send inline through `AuthService.TrySendWelcomeAsync`. Wrapped in `try/catch` — Resend hiccups never fail the sign-in.
- **Inline send (email sign-in, only if name present):** `AuthService.VerifyEmailSignInAsync` only sends inline when `user.DisplayName` is non-empty. New email-sign-in users have no name yet, so the welcome is **deferred**.
- **Deferred trigger (Persona approval):** `IdentityVerificationService.ProcessPersonaWebhookAsync` calls `TrySendDeferredWelcomeAsync` after persisting verified data, when `WelcomeEmailSentAtUtc IS NULL` and `VerifiedFirstName` is present. This catches the email-sign-in path: Persona gives us a real first name, we use it.
- On success the sender stamps `User.WelcomeEmailSentAtUtc` (set in the same `SaveChangesAsync` call as the verified data).
- **Safety net:** `WelcomeEmailSweeperBackgroundService` runs every 15 min, picks up users where `WelcomeEmailSentAtUtc IS NULL AND CreatedAtUtc < now - 5 min AND (DisplayName != null OR VerifiedFirstName != null)`, batches of 50. The name filter is what keeps unnamed-and-unverified users out of the retry loop forever — they only become eligible once a name lands.

### IP and User-Agent capture

- Sign-in mutations (`VerifyEmailSignIn`, `SignInWithGoogle`) extract the client IP (`X-Forwarded-For` first, falling back to `RemoteIpAddress`) and `User-Agent` header in `UserMutations.ResolveClientIpAddress`, pass them through the input model, and persist them on the new `Session` row.
- Stored as plain text on `sessions.ip_address` (max 64) and `sessions.user_agent` (max 512). Trimmed defensively on the way in.

## Common changes and where they live

| Change | Touch this |
| ------ | ---------- |
| Change code length / TTL | `AuthService.cs` (constants near the top) |
| Change session TTL or touch interval | `SessionLifetime` / `SessionTouchInterval` constants in `AuthService.cs` |
| Change session cookie name | `SessionCookieManager.SessionCookieName` |
| New error variant | The relevant `*Result.cs` model + handle in `UserMutations.cs` payload mapping |
| Switch email provider | New `IEmailSignInSender` impl in Infrastructure, swap registration in `Infrastructure/DependencyInjection.cs` |
| Change cleanup cadence / grace period | `SessionCleanupBackgroundService.cs` (`RunInterval`, `RetentionGracePeriod`) |
| Add an allowed origin (CORS + CSRF) | `Cors:AllowedOrigins` in appsettings — `CsrfOriginAllowlist` reads the same list |
