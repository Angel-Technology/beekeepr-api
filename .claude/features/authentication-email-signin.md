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
  - `FromEmail`
  - `FrontendBaseUrl`
  - `ResendApiKey`
  - `ResendBaseUrl`

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
- Session token: 64 hex chars, 30-day expiry, hashed before persist, delivered as HTTP-only cookie.
- Errors return on the Result object — `EmailRequired`, `InvalidToken`, `EmailDeliveryFailed`, etc. Do **not** throw.

## Common changes and where they live

| Change | Touch this |
| ------ | ---------- |
| Change code length / TTL | `AuthService.cs` (constants near the top) |
| Change session TTL or cookie name | `SessionCookieManager.cs` + `AuthService.cs` |
| New error variant | The relevant `*Result.cs` model + handle in `UserMutations.cs` payload mapping |
| Switch email provider | New `IEmailSignInSender` impl in Infrastructure, swap registration in `Infrastructure/DependencyInjection.cs` |
