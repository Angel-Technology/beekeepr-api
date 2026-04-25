# Feature: Google Sign-In (OAuth ID token)

Status: **Complete**.

## What it does

The frontend uses Google Identity Services to obtain an ID token (JWT). It posts that token to the backend in a single mutation. The backend:

1. Verifies the JWT signature, issuer, and `aud` against the configured client IDs (`GoogleTokenVerifier`).
2. Looks up an `external_accounts` row by `provider = Google` + `provider_account_id`.
3. If no link exists, looks up or creates a `User` by email (marking `email_verified = true` since Google has verified it).
4. Creates the `external_accounts` row if missing, updates `last_sign_in_at`.
5. Opens a `Session` and returns the session token (also setting the HTTP-only cookie via `SessionCookieManager`).

`signOut` and `currentUser` are shared with email sign-in — see [`authentication-email-signin.md`](authentication-email-signin.md).

## Database tables

| Table              | Purpose | Key columns |
| ------------------ | ------- | ----------- |
| `users`            | The account | `id`, `email`, `email_verified`, `display_name`, `created_at` |
| `external_accounts`| Links a user to an external identity provider | `id`, `user_id`, `provider`, `provider_account_id`, `provider_email`, `created_at`, `last_sign_in_at` |
| `sessions`         | Active logins | (same as email sign-in) |

`(provider, provider_account_id)` is unique — one Google account maps to exactly one user.

## GraphQL surface

| Operation | Type | Input | Output |
| --------- | ---- | ----- | ------ |
| `signInWithGoogle` | mutation | `idToken` | user (with `imageUrl` populated from Google's `picture` claim), session token, expires-at, error |

## External services

- **Google APIs** for ID token verification — `BuzzKeepr.Infrastructure/Auth/GoogleTokenVerifier.cs` (uses `Google.Apis.Auth`).
- Required config (`appsettings.json` → `Google:` section):
  - `ClientIds` — array of valid OAuth client IDs (one per platform you ship: web, iOS, Android).

## Files to watch

### Domain
- `BuzzKeepr.Domain/Entities/User.cs`
- `BuzzKeepr.Domain/Entities/ExternalAccount.cs`
- `BuzzKeepr.Domain/Entities/Session.cs`
- `BuzzKeepr.Domain/Enums/AuthProvider.cs`

### Application
- `BuzzKeepr.Application/Auth/AuthService.cs` (`SignInWithGoogleAsync`)
- `BuzzKeepr.Application/Auth/IGoogleTokenVerifier.cs`
- `BuzzKeepr.Application/Auth/Models/SignInWithGoogleInput.cs`
- `BuzzKeepr.Application/Auth/Models/SignInWithGoogleResult.cs`
- `BuzzKeepr.Application/Auth/Models/GoogleIdentity.cs`
- `BuzzKeepr.Application/Auth/IAuthRepository.cs` (external-account lookup methods)

### Infrastructure
- `BuzzKeepr.Infrastructure/Auth/GoogleTokenVerifier.cs`
- `BuzzKeepr.Infrastructure/Configuration/GoogleAuthOptions.cs`
- `BuzzKeepr.Infrastructure/Persistence/Repositories/AuthRepository.cs`
- `BuzzKeepr.Infrastructure/Persistence/BuzzKeeprDbContext.cs` (entity config for `external_accounts`)

### Presentation
- `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (`SignInWithGoogleAsync`)
- `BuzzKeepr.Presentation/GraphQL/Mutations/SignInWithGooglePayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Inputs/SignInWithGoogleInput.cs`
- `BuzzKeepr.Presentation/Auth/SessionCookieManager.cs`

## Conventions specific to this feature

- The ID token's `aud` claim **must** match one of `Google:ClientIds`. Add a new client ID here when you ship a new platform.
- Email is trimmed + lowercased before user lookup, same as email sign-in.
- If a user already exists by email but has no Google link, this flow will create the link automatically (no separate "link account" step).
- **`User.ImageUrl` is captured from Google's `picture` claim** on first sign-in, but only when the existing user record has no image yet (`??=` not `=`). We don't overwrite a user-set or already-fetched image on every sign-in.
- Errors are returned on `SignInWithGoogleResult` — invalid token, audience mismatch, etc. Do not throw.

## Common changes and where they live

| Change | Touch this |
| ------ | ---------- |
| Add a new platform / client ID | `appsettings.json` (`Google:ClientIds`) — no code change |
| Add a new OAuth provider (Apple, Facebook…) | New `AuthProvider` enum value, new verifier interface in Application + impl in Infrastructure, mirror the `SignInWithGoogle*` mutation/payload/input |
| Block disposable email domains, etc. | `AuthService.SignInWithGoogleAsync` before user creation |
