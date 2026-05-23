# Feature: User Management

Status: **Complete**.

## What it does

Owns the lifecycle of the `User` entity outside of authentication: explicit user creation, lookup by id, recording terms-of-service acceptance, profile updates (nickname + handle), and self-service account deletion with a 72-hour grace period.

Note: most users are created implicitly by the sign-in flows (`verifyEmailSignIn`, `signInWithGoogle`). The `createUser` mutation is only used when an admin / onboarding flow needs to provision a user up-front.

## Database tables

| Table   | Purpose | Key columns |
| ------- | ------- | ----------- |
| `users` | The account | `id`, `email`, `email_verified`, `display_name`, `nickname`, `handle`, `created_at_utc`, `deleted_at_utc`, `terms_accepted_at_utc`, plus all identity-verification fields (see [`identity-verification-persona.md`](identity-verification-persona.md)) |

`handle` carries a unique index and is stored lowercased without any leading `@` (e.g. `sammyw`). Display layers prepend the `@` themselves. `deleted_at_utc` powers the soft-delete flow described below.

## GraphQL surface

| Operation | Type | Input | Output | Auth required |
| --------- | ---- | ----- | ------ | ------------- |
| `createUser` | mutation | `email`, `displayName?` | user, error | **App key** — `X-App-Api-Key` header must match `Auth:AppApiKey`. If the config value is blank (dev default) the gate is open. |
| `acceptTerms` | mutation | (uses session) | updated user | yes (session) |
| `updateProfile` | mutation | `nickname?`, `handle?` | updated user, error | yes (session) |
| `requestAccountDeletion` | mutation | (uses session) | user with `deletedAtUtc` set, error | yes (session) |
| `cancelAccountDeletion` | mutation | (uses session) | user with `deletedAtUtc` cleared, error | yes (session) |
| `getUserById` | query | `id` | user or null | **Self only** — returns null unless the caller's session matches the requested id. Public-profile lookups for search will get a separate `userProfile`/`searchUsers` query backed by a stripped `UserProfileGraph` (no PII). |
| `currentUser` | query | (uses session) | user with identity verification status | yes (returns null if no session) |

### `updateProfile` — nickname + handle

Both inputs are optional and only fields explicitly present in the input get touched (passing `null` for a field leaves it unchanged is **not** the convention here — passing an empty string clears it; omitting the field skips it). Validation:

- `nickname`: trimmed, max 50 chars, empty → null. Otherwise stored as-is.
- `handle`: trimmed, lowercased, must match `^[a-zA-Z0-9_]{3,20}$` (3–20 alphanumeric or underscore — no `@` prefix; the frontend strips/prepends it for display). Empty → null. Uniqueness is enforced by a DB index and pre-checked by `UserRepository.HandleExistsAsync` so we return `That handle is already taken.` rather than letting the unique violation bubble up.

Error strings the client may see: `Nickname must be 50 characters or fewer.`, `Handle must be 3-20 letters, numbers, or underscores.`, `That handle is already taken.`

### `requestAccountDeletion` / `cancelAccountDeletion` — 72-hour soft delete

The flow:

1. User calls `requestAccountDeletion` → `UserService.RequestAccountDeletionAsync` stamps `deleted_at_utc = utcnow`. Idempotent: calling again does not move the timestamp forward.
2. A global EF query filter (`builder.HasQueryFilter(u => u.DeletedAtUtc == null)` in `BuzzKeeprDbContext`) hides the user from every standard query immediately. They effectively disappear from the app even though their row still exists.
3. **Sessions stay alive** during the 72-hour grace period. Two recovery paths:
   - Sign back in (email or Google) — `AuthService.RecoverIfPendingDeletion` clears `deleted_at_utc` automatically and logs the recovery.
   - Call `cancelAccountDeletion` while still authenticated — same effect, explicit user action.
4. After 72 hours, `AccountDeletionPurgeBackgroundService` (`IServiceScopeFactory`-based hosted service, hourly tick) hard-deletes the row. Cascade rules wipe `ExternalAccounts` and `Sessions`; `VerificationTokens.UserId` is set to `NULL` (so token records survive for audit, just disassociated).

The `deletedAtUtc` field is on `UserGraph` so clients can render a "your account is pending deletion — cancel?" banner during the grace window.

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
- `BuzzKeepr.Application/Auth/AuthService.cs` (sign-in recovery — `RecoverIfPendingDeletion`)
- `BuzzKeepr.Application/Users/Models/CreateUserInput.cs`
- `BuzzKeepr.Application/Users/Models/CreateUserResult.cs`
- `BuzzKeepr.Application/Users/Models/AcceptTermsResult.cs`
- `BuzzKeepr.Application/Users/Models/UpdateProfileInput.cs`
- `BuzzKeepr.Application/Users/Models/UpdateProfileResult.cs`
- `BuzzKeepr.Application/Users/Models/RequestAccountDeletionResult.cs`
- `BuzzKeepr.Application/Users/Models/CancelAccountDeletionResult.cs`
- `BuzzKeepr.Application/Users/Models/UserDto.cs`

### Infrastructure
- `BuzzKeepr.Infrastructure/Persistence/Repositories/UserRepository.cs`
- `BuzzKeepr.Infrastructure/Persistence/Repositories/AuthRepository.cs` (`IgnoreQueryFilters` on the lookups that must see soft-deleted users)
- `BuzzKeepr.Infrastructure/Persistence/BuzzKeeprDbContext.cs` (entity config for `users`, global query filter on `DeletedAtUtc`)
- `BuzzKeepr.Infrastructure/Users/AccountDeletionPurgeBackgroundService.cs`
- `BuzzKeepr.Infrastructure/DependencyInjection.cs` (registers the purge sweeper)
- Migration `20260411225732_AddUserTermsAcceptance` (adds `terms_accepted_at_utc`)
- Migration `20260523032521_AddUserNicknameAndHandle` (adds `nickname`, `handle`, unique index on `handle`)
- Migration `20260523035508_AddUserDeletedAt` (adds `deleted_at_utc`)
- Migration `20260523042312_ShrinkHandleMaxLength` (shrinks `handle` from `varchar(21)` → `varchar(20)` after dropping the stored `@` prefix)

### Presentation
- `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (`CreateUserAsync`, `AcceptTermsAsync`, `UpdateProfileAsync`, `RequestAccountDeletionAsync`, `CancelAccountDeletionAsync`)
- `BuzzKeepr.Presentation/GraphQL/Queries/UserQueries.cs`
- `BuzzKeepr.Presentation/GraphQL/Mutations/CreateUserPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Mutations/AcceptTermsPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Mutations/UpdateProfilePayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Mutations/RequestAccountDeletionPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Mutations/CancelAccountDeletionPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Inputs/CreateUserInput.cs`
- `BuzzKeepr.Presentation/GraphQL/Inputs/UpdateProfileInput.cs`
- `BuzzKeepr.Presentation/GraphQL/Types/UserGraph.cs`

## Conventions specific to this feature

- Email uniqueness is enforced both at the DB index level and in `UserService.CreateAsync` (returns `EmailAlreadyExists` error rather than throwing). The pre-check uses `IgnoreQueryFilters()` so pending-deletion users still reserve their email until the 72-hour purge runs.
- Handle uniqueness mirrors email: pre-checked with `IgnoreQueryFilters()`, enforced by a DB unique index.
- `acceptTerms` is idempotent in spirit but always overwrites `terms_accepted_at_utc` with the current UTC time. Don't change this without product input — the timestamp is the audit trail.
- `UserGraph` (Presentation) is the projection that includes identity-verification status; do not return raw `User` from GraphQL.
- The global query filter on `User` (`DeletedAtUtc == null`) is the **default** for all `dbContext.Users` queries. Any new repository method that needs to see soft-deleted users must opt out explicitly with `.IgnoreQueryFilters()`. Grep for `IgnoreQueryFilters` to audit every escape hatch — the existing ones are limited to auth lookups (so sign-in can recover the account), uniqueness pre-checks (so the unique index doesn't trip), and the purge sweeper.

## Common changes and where they live

| Change | Touch this |
| ------ | ---------- |
| Add a profile field (e.g. phone) | `User.cs` → `BuzzKeeprDbContext` config → new EF migration → `UserDto` + `UserGraph` + both `MapUser` helpers (`UserService` and `AuthService` each have one) → `updateProfile` if user-editable |
| Add a profile-update mutation | New method on `UserService`, new payload + input under `Presentation/GraphQL/`, add to `UserMutations` |
| Change the 72-hour deletion grace period | `AccountDeletionPurgeBackgroundService.GracePeriod` — also update this doc and any client-facing copy that quotes the window. |
| Add a place that must see soft-deleted users | Add a method to `IUserRepository` that calls `.IgnoreQueryFilters()` (see `GetByIdForUpdateIncludingDeletedAsync` as the template). Do not add `IgnoreQueryFilters` ad-hoc inside business logic. |
