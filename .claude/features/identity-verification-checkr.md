# Feature: Identity Verification — Checkr Trust (Instant Criminal Check)

Status: **In-flight** (active on the `persona` branch — most recent commit before the WIP is `update: checkr`).

## What it does

A signed-in user submits their name + DOB + state, and the backend calls Checkr Trust's "instant criminal check" endpoint to surface possible matches. The response (`check_id`, `result_count`, `has_possible_matches`) is returned synchronously to the caller.

There is no per-check entity in our DB yet — the result is currently transient and lives only in the GraphQL response. If the product needs an audit trail, this is the next schema change to make.

## Database tables

None directly. The current implementation does **not** persist the check id or its results.

The user this check is associated with comes from the session (`AuthService.GetCurrentUserAsync`), so `users.id` is the implicit foreign key on the Checkr side.

## GraphQL surface

| Operation | Type | Input | Output | Auth required |
| --------- | ---- | ----- | ------ | ------------- |
| `startInstantCriminalCheck` | mutation | `firstName`, `lastName`, `middleName?`, `phoneNumber?`, `dateOfBirth?`, `state?` | `check_id`, `result_count`, `has_possible_matches`, `error` | yes |

`firstName` and `lastName` are required at the service layer — empty values come back as a typed error on the result, not as an exception.

## External services

- **Checkr Trust API** — uses an OAuth2 client-credentials flow; tokens are cached in `IMemoryCache` to avoid re-fetching per request.
- Required config (`appsettings.json` → `CheckrTrust:` section):
  - `ApiBaseUrl`
  - `ClientId`
  - `ClientSecret`

## Files to watch

### Application
- `BuzzKeepr.Application/IdentityVerification/IdentityVerificationService.cs` (`CreateInstantCriminalCheckAsync`)
- `BuzzKeepr.Application/IdentityVerification/ICheckrTrustClient.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/StartInstantCriminalCheckInput.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/CreateInstantCriminalCheckInput.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/CreateInstantCriminalCheckResult.cs`

### Infrastructure
- `BuzzKeepr.Infrastructure/IdentityVerification/CheckrTrustClient.cs` (HTTP client + OAuth2 token cache)
- `BuzzKeepr.Infrastructure/Configuration/CheckrTrustOptions.cs`

### Presentation
- `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (`StartInstantCriminalCheckAsync`)
- `BuzzKeepr.Presentation/GraphQL/Mutations/StartInstantCriminalCheckPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Inputs/StartInstantCriminalCheckInput.cs`

## Conventions specific to this feature

- The OAuth2 token is cached in `IMemoryCache` keyed per `ClientId`. Don't fetch a new token per request — let the cache handle it.
- Treat Checkr Trust failures as recoverable: return an error on `CreateInstantCriminalCheckResult`, don't throw.
- Inputs that go to Checkr (DOB, phone) are sensitive PII — don't log them.

## Common changes and where they live

| Change | Touch this |
| ------ | ---------- |
| Persist check results | New `criminal_checks` entity in Domain → DbContext config → migration → repository in Infrastructure → write from `IdentityVerificationService` after the API call |
| Add a deeper Checkr product (e.g. SSN trace, MVR) | New method on `ICheckrTrustClient`, new mutation following the same pattern, new input/payload types |
| Surface possible matches in detail | Extend `CreateInstantCriminalCheckResult` and the GraphQL payload; map fields from Checkr's response in `CheckrTrustClient` |

## Known TODOs / sharp edges

- No persistence of check ids → can't reconstruct what we asked Checkr later. Worth fixing before this is user-visible in production.
- No webhook handling for asynchronous Checkr products. Today only the synchronous "instant" check is wired up.
- The `state` input is optional but Checkr's accuracy improves a lot with it — frontend should make it required for the user even if the API doesn't.
