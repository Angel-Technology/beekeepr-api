# Feature: User Management

Status: **Complete**.

## What it does

Owns the lifecycle of the `User` entity outside of authentication: explicit user creation, lookup by id, and recording terms-of-service acceptance.

Note: most users are created implicitly by the sign-in flows (`verifyEmailSignIn`, `signInWithGoogle`). The `createUser` mutation is only used when an admin / onboarding flow needs to provision a user up-front.

## Database tables

| Table   | Purpose | Key columns |
| ------- | ------- | ----------- |
| `users` | The account | `id`, `email`, `email_verified`, `display_name`, `created_at`, `terms_accepted_at_utc`, plus all identity-verification fields (see [`identity-verification-persona.md`](identity-verification-persona.md)) |

## GraphQL surface

| Operation | Type | Input | Output | Auth required |
| --------- | ---- | ----- | ------ | ------------- |
| `createUser` | mutation | `email`, `displayName?` | user, error | **App key** — `X-App-Api-Key` header must match `Auth:AppApiKey`. If the config value is blank (dev default) the gate is open. |
| `acceptTerms` | mutation | (uses session) | updated user | yes (session) |
| `getUserById` | query | `id` | user or null | **Self only** — returns null unless the caller's session matches the requested id. Public-profile lookups for search will get a separate `userProfile`/`searchUsers` query backed by a stripped `UserProfileGraph` (no PII). |
| `currentUser` | query | (uses session) | user with identity verification status | yes (returns null if no session) |

### Why `createUser` is gated by an app key, not a session

Sign-up doesn't have a session yet — but we don't want anyone on the internet creating arbitrary users (email enumeration, spam accounts, etc.). The shared `Auth:AppApiKey` is embedded in the official frontend app(s), so prod traffic that doesn't carry it is rejected. Dev leaves the key blank so Banana Cake Pop / Strawberry Shake can hit the endpoint without juggling secrets.

For most users, `createUser` won't be the path anyway — `requestEmailSignIn → verifyEmailSignIn` creates the user implicitly on first verify. `createUser` exists for the explicit "set up an account before verification" cases.

### Why `getUserById` is self-only

The full `UserGraph` includes PII (email, verified name parts, DOB, phone, license state). Anyone who could query other users' rows could harvest that data with a guessable id. When search lands, we'll add a separate `userProfile(id)` query that returns a deliberately-narrow `UserProfileGraph` (display name, image, identity-verification badge — no PII). That separation keeps `getUserById` simple and safe.

## External services

None.

## Files to watch

### Domain
- `BuzzKeepr.Domain/Entities/User.cs`

### Application
- `BuzzKeepr.Application/Users/UserService.cs`
- `BuzzKeepr.Application/Users/IUserService.cs`
- `BuzzKeepr.Application/Users/IUserRepository.cs`
- `BuzzKeepr.Application/Users/Models/CreateUserInput.cs`
- `BuzzKeepr.Application/Users/Models/CreateUserResult.cs`
- `BuzzKeepr.Application/Users/Models/AcceptTermsResult.cs`
- `BuzzKeepr.Application/Users/Models/UserDto.cs`

### Infrastructure
- `BuzzKeepr.Infrastructure/Persistence/Repositories/UserRepository.cs`
- `BuzzKeepr.Infrastructure/Persistence/BuzzKeeprDbContext.cs` (entity config for `users`)
- Migration `20260411225732_AddUserTermsAcceptance` (adds `terms_accepted_at_utc`)

### Presentation
- `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (`CreateUserAsync`, `AcceptTermsAsync`)
- `BuzzKeepr.Presentation/GraphQL/Queries/UserQueries.cs`
- `BuzzKeepr.Presentation/GraphQL/Mutations/CreateUserPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Mutations/AcceptTermsPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Inputs/CreateUserInput.cs`
- `BuzzKeepr.Presentation/GraphQL/Types/UserGraph.cs`

## Conventions specific to this feature

- Email uniqueness is enforced both at the DB index level and in `UserService.CreateAsync` (returns `EmailAlreadyExists` error rather than throwing).
- `acceptTerms` is idempotent in spirit but always overwrites `terms_accepted_at_utc` with the current UTC time. Don't change this without product input — the timestamp is the audit trail.
- `UserGraph` (Presentation) is the projection that includes identity-verification status; do not return raw `User` from GraphQL.

## Common changes and where they live

| Change | Touch this |
| ------ | ---------- |
| Add a profile field (e.g. phone) | `User.cs` → `BuzzKeeprDbContext` config → new EF migration → `UserDto` + `UserGraph` → mutation if user-editable |
| Add a profile-update mutation | New method on `UserService`, new payload + input under `Presentation/GraphQL/`, add to `UserMutations` |
| Soft-delete users | New `deleted_at_utc` column on `users`, filter in `UserRepository`, decide impact on `Sessions` and `ExternalAccounts` |
