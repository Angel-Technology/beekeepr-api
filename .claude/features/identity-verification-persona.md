# Feature: Identity Verification — Persona

Status: **In-flight** (active on the `persona` branch). Inquiry creation, webhook signature verification, and verified-data persistence are wired up; treat this as the "happy path works, edges still being polished" stage.

## What it does

Government-ID verification through the Persona hosted flow.

1. The signed-in user calls `startPersonaInquiry`. The backend either creates a fresh inquiry on Persona (`inq_…` id) or reuses an existing in-progress one if it is still retryable.
2. The frontend opens the Persona SDK with the returned `inquiry_id`. The user submits ID + selfie inside Persona's UI.
3. Persona calls `POST /webhooks/persona` with the result. The backend verifies the HMAC signature, then updates the user's `persona_inquiry_status` and the higher-level `identity_verification_status`.
4. On approval the backend calls Persona again to fetch the verified government-ID data (name, DOB, address, license expiry / last 4) and stores those on the `users` row plus `persona_verified_at_utc`.

## Database tables

Everything lives directly on `users` (no separate inquiry table yet). All columns added by migration `20260321154755_AddPersonaIdentityVerification`.

| Column | Purpose |
| ------ | ------- |
| `identity_verification_status` | High-level enum (`NotStarted`, `Created`, `Pending`, `Completed`, `NeedsReview`, `Approved`, `Declined`, `Failed`, `Expired`) |
| `persona_inquiry_id` | Persona inquiry id (`inq_…`), unique |
| `persona_inquiry_status` | Mirrors Persona's own status enum |
| `persona_verified_at_utc` | Timestamp when verified data was persisted |
| `verified_first_name`, `verified_last_name`, `verified_birthdate` | Identity from the ID |
| `verified_address_street1`, `verified_address_street2`, `verified_address_city`, `verified_address_subdivision`, `verified_address_postal_code`, `verified_country_code` | Verified address |
| `verified_license_last4`, `verified_license_expiration_date` | License metadata (no full number stored) |

## GraphQL surface

| Operation | Type | Input | Output | Auth required |
| --------- | ---- | ----- | ------ | ------------- |
| `startPersonaInquiry` | mutation | (uses session) | `inquiry_id`, `identity_verification_status`, `persona_inquiry_status`, `created_new_inquiry` | yes |
| `currentUser` | query | (uses session) | includes identity-verification status fields | yes |

## REST surface (webhooks)

| Route | Handler |
| ----- | ------- |
| `POST /webhooks/persona` | Wired in `BuzzKeepr.Presentation/Program.cs` → `IdentityVerificationService.ProcessPersonaWebhookAsync` |

Signature is verified via `PersonaWebhookSignatureVerifier` against any of the configured `Persona:WebhookSecrets` (array, so secrets can be rotated without downtime).

## External services

- **Persona API** (`api.withpersona.com`):
  - Create inquiry — `POST /inquiries`
  - Get government-ID data — Persona's verifications endpoint
- Required config (`appsettings.json` → `Persona:` section):
  - `ApiKey`
  - `ApiBaseUrl`
  - `InquiryTemplateId` (the `tmpl_…` template id for your KYC flow)
  - `WebhookSecrets` (array of strings — for HMAC verification, supports rotation)

## Files to watch

### Domain
- `BuzzKeepr.Domain/Entities/User.cs` (all the `Verified*` and `Persona*` columns)
- `BuzzKeepr.Domain/Enums/IdentityVerificationStatus.cs`
- `BuzzKeepr.Domain/Enums/PersonaInquiryStatus.cs`

### Application
- `BuzzKeepr.Application/IdentityVerification/IdentityVerificationService.cs`
- `BuzzKeepr.Application/IdentityVerification/IIdentityVerificationService.cs`
- `BuzzKeepr.Application/IdentityVerification/IIdentityVerificationRepository.cs`
- `BuzzKeepr.Application/IdentityVerification/IPersonaClient.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/CreatePersonaInquiryInput.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/CreatePersonaInquiryResult.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/StartPersonaInquiryResult.cs`
- `BuzzKeepr.Application/IdentityVerification/Models/PersonaGovernmentIdDataResult.cs`

### Infrastructure
- `BuzzKeepr.Infrastructure/IdentityVerification/PersonaClient.cs`
- `BuzzKeepr.Infrastructure/IdentityVerification/PersonaWebhookSignatureVerifier.cs`
- `BuzzKeepr.Infrastructure/Persistence/Repositories/IdentityVerificationRepository.cs`
- `BuzzKeepr.Infrastructure/Configuration/PersonaOptions.cs`
- `BuzzKeepr.Infrastructure/Persistence/BuzzKeeprDbContext.cs` (entity config)
- Migration: `20260321154755_AddPersonaIdentityVerification`

### Presentation
- `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (`StartPersonaInquiryAsync`)
- `BuzzKeepr.Presentation/GraphQL/Mutations/StartPersonaInquiryPayload.cs`
- `BuzzKeepr.Presentation/GraphQL/Types/UserGraph.cs` (exposes the verification fields)
- `BuzzKeepr.Presentation/Program.cs` (`/webhooks/persona` route registration)

## Conventions specific to this feature

- **Two status fields on purpose.** `persona_inquiry_status` mirrors Persona's vocabulary verbatim — keep them in sync if Persona adds a new value. `identity_verification_status` is *our* product-facing status, mapped from the Persona one inside `IdentityVerificationService`. Frontend should rely on `identity_verification_status`.
- **Webhook secrets are an array.** Always read all of them when verifying — never assume index 0. This is what allows a zero-downtime secret rotation.
- **Reuse before recreate.** `startPersonaInquiry` should reuse an existing inquiry if its `persona_inquiry_status` is in a retryable state — don't create a new `inq_…` for every retry click.
- **Don't store full license numbers**, only the last 4. Same goes for any field Persona returns that we don't actively need — minimize PII.
- **No exceptions for business outcomes.** Webhook handler should always return 200 to Persona once the signature is valid; business problems get logged and reflected in the user's status, not raised back at Persona.

## Common changes and where they live

| Change | Touch this |
| ------ | ---------- |
| Add a verified field (e.g. middle name) | `User.cs` → `BuzzKeeprDbContext` config → new EF migration → `PersonaClient` mapping → `UserGraph` |
| New webhook event type | `IdentityVerificationService.ProcessPersonaWebhookAsync` (status mapping) |
| Rotate the webhook secret | Add the new value to `Persona:WebhookSecrets`, deploy, then remove the old one once Persona is using the new secret |
| Switch KYC vendor | New `I*Client` interface + Infrastructure impl, new options class, swap registration in `Infrastructure/DependencyInjection.cs`. Keep the `identity_verification_status` enum stable so the frontend doesn't break. |

## Known TODOs / sharp edges

- Webhook idempotency: confirm we no-op if Persona retries the same event id.
- Race between `startPersonaInquiry` (sync write) and an early webhook arriving before the inquiry id is committed locally — verify the order of operations in `IdentityVerificationService.StartPersonaInquiryAsync`.
- No dedicated `persona_inquiries` table yet; if we need history (multiple attempts, audit trail) this becomes a real schema change.
