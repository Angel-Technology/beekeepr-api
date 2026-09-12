# Feature: Sign in with Apple

Status: **Complete**.

Added to satisfy App Store Review Guideline 4.8 — any app offering a third-party login (we have Google) must also offer an equivalent login that meets Apple's privacy requirements. Sign in with Apple is the canonical such option.

## What it does

The frontend uses `expo-apple-authentication` to obtain Apple's `identityToken` (a JWT). It posts that token to the backend in a single mutation. The backend:

1. Validates the JWT against Apple's JWKS — signature, `iss = https://appleid.apple.com`, `aud ∈ Apple:ClientIds`, expiry (5min clock skew) — in `AppleTokenVerifier`.
2. Looks up an `external_accounts` row by `provider = Apple` + `provider_account_id` (Apple's `sub` claim).
3. If a link exists, signs the returning user in — **no `email` claim required** (Apple omits it on every sign-in after the first).
4. If no link exists, looks up or creates a `User` by email (marking `email_verified = true` since Apple has verified it). This path *does* require the token to carry the `email` claim, so it only runs for genuinely-first-time Apple signups.
5. Creates the `external_accounts` row if missing, updates `last_sign_in_at`.
6. Opens a `Session` and returns the session token (also sets the HTTP-only cookie via `SessionCookieManager`).

`signOut` and `currentUser` are shared with email + Google sign-in — see [`authentication-email-signin.md`](authentication-email-signin.md).

## Database tables

| Table              | Purpose | Key columns |
| ------------------ | ------- | ----------- |
| `users`            | The account | `id`, `email`, `email_verified`, `display_name`, `created_at` |
| `external_accounts`| Links a user to an external identity provider | `id`, `user_id`, `provider`, `provider_account_id`, `provider_email`, `created_at`, `last_sign_in_at` |
| `sessions`         | Active logins | (same as email sign-in) |

`(provider, provider_account_id)` is unique — one Apple ID maps to exactly one user.

## GraphQL surface

| Operation | Type | Input | Output |
| --------- | ---- | ----- | ------ |
| `signInWithApple` | mutation | `idToken`, `displayName` (optional, first sign-in only) | user, session token, expires-at, error |

## External services

- **Apple ID** — JWKS-backed token verification via `BuzzKeepr.Infrastructure/Auth/AppleTokenVerifier.cs` (uses `Microsoft.IdentityModel.Protocols.OpenIdConnect` + `System.IdentityModel.Tokens.Jwt`). The OIDC discovery doc and JWKS are fetched from `https://appleid.apple.com/.well-known/openid-configuration` and cached by `ConfigurationManager`. We override the defaults: `AutomaticRefreshInterval = 1h` and `RefreshInterval = 5m`. On a signature failure specifically (`SecurityTokenSignatureKeyNotFoundException` — Apple rotated a key since our last fetch), the verifier calls `configurationManager.RequestRefresh()` and retries validation once against a fresh JWKS. The old 24h default caused an App Store reviewer lockout on 2026-09-09; do not lengthen it without also revisiting the retry logic.
- Required config (`appsettings.json` / Render env → `Apple:` section):
  - `ClientIds` — array of valid audiences. For native iOS via `expo-apple-authentication` this is your **iOS bundle identifier** (e.g. `com.buzzkeepr.app`). For a Services ID (web/Android), add that here too. Add one entry per platform that ships an Apple-signed token.

## Files to watch

### Domain
- `BuzzKeepr.Domain/Entities/User.cs`
- `BuzzKeepr.Domain/Entities/ExternalAccount.cs`
- `BuzzKeepr.Domain/Entities/Session.cs`
- `BuzzKeepr.Domain/Enums/AuthProvider.cs` (`Apple = 3`)

### Application
- `BuzzKeepr.Application/Auth/AuthService.cs` (`SignInWithAppleAsync`)
- `BuzzKeepr.Application/Auth/IAppleTokenVerifier.cs`
- `BuzzKeepr.Application/Auth/Models/SignInWithAppleInput.cs`
- `BuzzKeepr.Application/Auth/Models/SignInWithAppleResult.cs`
- `BuzzKeepr.Application/Auth/Models/AppleIdentity.cs`
- `BuzzKeepr.Application/Auth/IAuthRepository.cs` (external-account lookup methods, shared with Google)

### Infrastructure
- `BuzzKeepr.Infrastructure/Auth/AppleTokenVerifier.cs`
- `BuzzKeepr.Infrastructure/Configuration/AppleAuthOptions.cs`
- `BuzzKeepr.Infrastructure/Persistence/Repositories/AuthRepository.cs`
- `BuzzKeepr.Infrastructure/Persistence/BuzzKeeprDbContext.cs` (entity config for `external_accounts`)

### Presentation
- `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (`SignInWithAppleAsync`)
- `BuzzKeepr.Presentation/GraphQL/Mutations/SignInWithApplePayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Inputs/SignInWithAppleInput.cs`
- `BuzzKeepr.Presentation/Auth/SessionCookieManager.cs`

## Conventions specific to this feature

- The ID token's `aud` claim **must** match one of `Apple:ClientIds`. Add a new client ID here when you ship a new platform (different bundle ID per Expo build profile, a Services ID for web, etc.).
- The handler is constructed with `MapInboundClaims = false`. Without this the default `JwtSecurityTokenHandler` rewrites `sub` and `email` to legacy SOAP claim URIs (`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/...`), which silently breaks claim extraction. Do not remove that line.
- **Display name handling is one-shot.** Apple only returns the user's `fullName` on the **very first** authorization, in the iOS-side authorization response (not in the JWT). The frontend captures it and forwards it as `signInWithApple.input.displayName`. On every subsequent sign-in, `fullName` is null on the client and the mutation is called with `displayName = null`. `AuthService.SignInWithAppleAsync` only writes `User.DisplayName` when it's currently null (`??=`), so re-sending null on subsequent sign-ins is safe — it never wipes the stored name.
- **Private relay emails are real emails.** Apple may return `<random>@privaterelay.appleid.com` if the user chose "Hide My Email." These addresses route through Apple's relay to the user's real inbox and **must** be treated as the user's email address — we store them in `User.Email` unchanged. The verifier exposes `AppleIdentity.IsPrivateRelayEmail` if a future feature ever needs to branch on this.
- **The `email` claim is first-authorization only.** Apple sends `email` on the very first Apple-ID → App-ID authorization; every subsequent token has `sub` but no `email`. `AppleTokenVerifier` therefore requires only `sub`; `AppleIdentity.Email` is `string?`. `AuthService.SignInWithAppleAsync` handles the two cases: existing `external_accounts` row → returning user, email not required, do NOT clobber `ProviderEmail` with null; no existing row → first-ever signup, email is required (return `InvalidToken` if it's missing).
- Email is verified by Apple in all flows — we trust `email_verified` from the JWT (Apple sends it as either bool or `"true"`/`"false"`; the verifier normalizes both). When the email claim is omitted, `EmailVerified` is `false` on the returned `AppleIdentity` (there's nothing to verify).
- If a user already exists by email but has no Apple link, this flow will create the link automatically (no separate "link account" step) — same behavior as Google.
- Errors are returned on `SignInWithAppleResult` — invalid input, invalid token. Do not throw.
- Validation failures are logged at `Warning` with only the inner exception type (`SecurityTokenInvalidAudienceException`, `SecurityTokenExpiredException`, `SecurityTokenSignatureKeyNotFoundException`, etc.) — never the token or claims. That's enough signal to debug a misconfigured bundle ID or a key-rotation event without leaking PII to logs. A missing `sub` claim (the last remaining silent rejection) also logs a warning.
- `AppleTokenVerifier` is registered as a **singleton** (not scoped like Google) because `ConfigurationManager<OpenIdConnectConfiguration>` does its own caching — there's no benefit to per-request churn, and singleton lets the JWKS cache survive across requests.

## Apple Developer setup

Before this flow can succeed in production:

1. Enable **Sign In with Apple** capability on the App ID in [developer.apple.com](https://developer.apple.com/account/resources/identifiers/list) → Identifiers → (your App ID) → Capabilities.
2. The App ID's bundle identifier must match what's in `Apple:ClientIds`.
3. If a new Expo build profile uses a different bundle ID (e.g. `com.buzzkeepr.app.dev`), add both to `Apple:ClientIds` and enable Sign in with Apple on each App ID.
4. No client secret / Services ID is needed for native iOS — those are only required for the web flow.

## Common changes and where they live

| Change | Touch this |
| ------ | ---------- |
| Add a new platform / bundle ID | `appsettings.json` (`Apple:ClientIds`) — no code change |
| Add another OAuth provider (Facebook, …) | New `AuthProvider` enum value, new verifier interface in Application + impl in Infrastructure, mirror the `SignInWithApple*` mutation/payload/input |
| Stop accepting private relay emails | Branch on `AppleIdentity.IsPrivateRelayEmail` in `AuthService.SignInWithAppleAsync` before user creation |
| Reuse Apple's name on subsequent sign-ins | Not possible — Apple only sends `fullName` on first authorization. Update the user via the regular `updateProfile` mutation instead. |

## Reviewer accounts

App-store reviewers (Apple/Google) cannot use this flow because they have no Apple ID. They sign in via the email + PIN flow described in [`authentication-email-signin.md`](authentication-email-signin.md) (`Auth:ReviewAccounts`).
