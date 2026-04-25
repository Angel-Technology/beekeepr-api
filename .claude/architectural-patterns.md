# Architectural Patterns — BuzzKeepr API

This document describes the architectural patterns used in the BuzzKeepr backend so that new work stays consistent with the existing structure. If you are adding a feature, read this first, then read the relevant file under `.claude/features/`.

> Source of truth: this file complements `ARCHITECTURE.md` at the repo root. If they ever disagree, the live code wins — update both.

---

## 1. High-level pattern: Clean Architecture (4 layers)

The solution is split into four projects that mirror the classic Clean / Onion Architecture layout. Dependencies point **inward** toward the Domain — the Domain knows nothing about the outside world.

```
┌──────────────────────────────────────────────────────────────┐
│  BuzzKeepr.Presentation   (HTTP / GraphQL / Webhooks)         │
│  - Hot Chocolate GraphQL server                               │
│  - REST endpoints for webhooks (e.g. /webhooks/persona)       │
│  - Session cookie + bearer-token resolution                   │
│  - Maps Application DTOs → GraphQL payload types              │
│         │                                                     │
│         ▼                                                     │
│  BuzzKeepr.Application    (Use cases, orchestration)          │
│  - Services: AuthService, UserService,                        │
│              IdentityVerificationService                      │
│  - Interfaces for repositories and external clients           │
│  - DTOs / Input / Result models                               │
│         │                                                     │
│         ▼                                                     │
│  BuzzKeepr.Domain         (Pure entities + enums)             │
│  - User, Session, ExternalAccount, VerificationToken          │
│  - No EF, no MediatR, no I/O                                  │
└──────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│  BuzzKeepr.Infrastructure (Tech implementations)              │
│  - EF Core DbContext + Migrations (PostgreSQL / Npgsql)       │
│  - Repository implementations                                 │
│  - External clients: PersonaClient, CheckrTrustClient,        │
│    GoogleTokenVerifier, ResendEmailSignInSender               │
│  - IOptions<T> configuration classes                          │
│                                                               │
│  Depends on Application + Domain only. Presentation references│
│  Infrastructure for DI wiring at startup.                     │
└──────────────────────────────────────────────────────────────┘
```

### Allowed dependency directions

| From            | May depend on                          |
| --------------- | -------------------------------------- |
| Presentation    | Application, Infrastructure (DI only)  |
| Application     | Domain                                 |
| Infrastructure  | Application, Domain                    |
| Domain          | nothing                                |

If you find yourself wanting to import `Microsoft.EntityFrameworkCore` from Application or Domain — stop. Add an interface in Application, implement it in Infrastructure.

---

## 2. Request flow (the "shape" every feature follows)

A typical mutation moves through the layers like this:

```
Client (GraphQL request)
   │
   ▼
[Presentation] UserMutations.cs
   - Reads input from GraphQL Inputs/*.cs
   - Calls IAuthService / IUserService / IIdentityVerificationService
   - Maps Result DTO → Payload type
   - For auth flows: sets/clears the session cookie via SessionCookieManager
   │
   ▼
[Application] *Service.cs
   - Validates inputs (e.g. email required, code length)
   - Orchestrates repositories + external clients
   - Returns a Result object — never throws for business errors
   │
   ▼
[Application interface] → [Infrastructure implementation]
   - I*Repository  ──► *Repository.cs        (EF Core, PostgreSQL)
   - I*Client      ──► *Client.cs            (HttpClient)
   - I*Sender      ──► *Sender.cs            (Resend HTTP API)
   │
   ▼
[Domain] entity is mutated or created, then persisted by the repository
```

Queries follow the same flow without the cookie/state mutations.

---

## 3. Patterns and conventions used

### 3.1 Repository pattern
- Interfaces live in `BuzzKeepr.Application/<Feature>/I*Repository.cs`.
- Implementations live in `BuzzKeepr.Infrastructure/Persistence/Repositories/*Repository.cs`.
- Repositories return entities, never `IQueryable<T>` (EF stays inside Infrastructure).
- Methods are named for intent: `GetUserByEmailAsync`, `GetValidVerificationTokenAsync`.

### 3.2 Service layer
- One service per feature area: `AuthService`, `UserService`, `IdentityVerificationService`.
- Services are the only place that combine multiple repositories and external clients.
- Services accept `Input` records and return `Result` records.

### 3.3 Result object pattern (no exceptions for expected errors)
- Every Application service method returns a result type with a success flag plus typed error context. Examples: `RequestEmailSignInResult`, `VerifyEmailSignInResult`, `SignInWithGoogleResult`, `CreateUserResult`, `StartPersonaInquiryResult`, `CreateInstantCriminalCheckResult`.
- Throwing is reserved for programmer errors / unexpected I/O failures, not for "user typed the wrong code".

### 3.4 DTO / Input / Result models
- All cross-layer data is moved through plain records under `BuzzKeepr.Application/<Feature>/Models/`.
- Domain entities are never returned directly to Presentation. Map to a DTO (e.g. `UserDto`, `AuthSessionDto`) first.

### 3.5 GraphQL surface (Hot Chocolate)
- Mutations: `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (one file groups them all today).
- Queries: `BuzzKeepr.Presentation/GraphQL/Queries/UserQueries.cs`.
- GraphQL-specific input types: `BuzzKeepr.Presentation/GraphQL/Inputs/*.cs`.
- GraphQL payload (response) types: `BuzzKeepr.Presentation/GraphQL/Mutations/*Payload.cs`.
- GraphQL projection of domain DTOs: `BuzzKeepr.Presentation/GraphQL/Types/*Graph.cs`.

### 3.6 Webhooks (REST escape hatch)
- Webhook routes are defined in `BuzzKeepr.Presentation/Program.cs` (e.g. `POST /webhooks/persona`).
- Signature verification happens in Infrastructure (`PersonaWebhookSignatureVerifier`).
- Body parsing → Application service → repository update.

### 3.7 Authentication / sessions
- Sign-in produces a 64-char hex session token; only its SHA-256 hash is stored in `Sessions`.
- Default TTL: 30 days. Raw token is delivered to the client as an HTTP-only cookie via `SessionCookieManager`.
- `SessionTokenResolver` accepts the cookie *or* a Bearer token in `Authorization`.
- All authenticated GraphQL operations resolve the user through `IAuthService.GetCurrentUserAsync`.

### 3.8 Validation
- Lightweight, in-service validation today (no FluentValidation library yet).
- Email is always trimmed + lowercased before lookup or storage.
- Verification tokens track `failed_attempts` (max 5) and `expires_at` (15 min).

### 3.9 Configuration
- Strongly typed `IOptions<T>` classes live in `BuzzKeepr.Infrastructure/Configuration/`: `DatabaseOptions`, `EmailDeliveryOptions`, `GoogleAuthOptions`, `PersonaOptions`, `CheckrTrustOptions`.
- Bound from `appsettings.json` + environment variables in `BuzzKeepr.Infrastructure/DependencyInjection.cs`.
- Required secrets are validated at startup — fail fast if missing.

### 3.10 Logging
- `ILogger<T>` injected at service and external-client boundaries.
- Log includes operation context (user id, inquiry id) — no PII tokens.

### 3.11 Persistence
- PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`.
- DbContext: `BuzzKeepr.Infrastructure/Persistence/BuzzKeeprDbContext.cs`.
- Migrations: `BuzzKeepr.Infrastructure/Persistence/Migrations/`. Migrations assembly is `BuzzKeepr.Infrastructure`.
- Table/column names are snake_case (Postgres convention).

---

## 4. Tech stack snapshot

| Concern              | Choice                                         |
| -------------------- | ---------------------------------------------- |
| Runtime              | .NET 10.0, C# latest, nullable refs on        |
| HTTP                 | ASP.NET Core minimal APIs                      |
| GraphQL              | Hot Chocolate 16.x                             |
| ORM                  | EF Core 10.0 + Npgsql 10.0                     |
| Database             | PostgreSQL (Docker for local, see compose.yml) |
| Email                | Resend (HTTP)                                  |
| Google auth          | `Google.Apis.Auth` for ID-token verification   |
| Identity verify      | Persona API (inquiries + webhooks)             |
| Background check     | Checkr Trust (OAuth2-protected REST API)       |
| API docs             | Swagger / Swashbuckle (Dev only at `/swagger`) |
| Tests                | `BuzzKeepr.UnitTests`, `BuzzKeepr.IntegrationTests` |

---

## 5. Where to add new things (cheat sheet)

| You want to add…                                  | Put it here |
| ------------------------------------------------- | ----------- |
| A new GraphQL mutation                            | `BuzzKeepr.Presentation/GraphQL/Mutations/UserMutations.cs` (+ input under `GraphQL/Inputs/`, payload under `GraphQL/Mutations/`) |
| A new business rule / use case                    | New method on the relevant `*Service` in `BuzzKeepr.Application/<Feature>/` |
| A new domain concept                              | Entity in `BuzzKeepr.Domain/Entities/`, enum in `BuzzKeepr.Domain/Enums/` |
| A new database column                             | Update entity → update `BuzzKeeprDbContext` config → `dotnet ef migrations add <Name>` (output to Infrastructure) |
| A new external API                                | Interface in `BuzzKeepr.Application/<Feature>/I*Client.cs`, implementation in `BuzzKeepr.Infrastructure/<Feature>/*Client.cs`, options in `BuzzKeepr.Infrastructure/Configuration/`, register in `Infrastructure/DependencyInjection.cs` |
| A new webhook                                     | Route in `BuzzKeepr.Presentation/Program.cs`, signature verifier in Infrastructure, processing method on the relevant Application service |
| A new repository query                            | Method on the existing `I*Repository` interface, implement in the matching `*Repository.cs` |

---

## 6. Things to avoid

- ❌ Returning `IQueryable<T>` or EF entities across layer boundaries.
- ❌ Throwing exceptions for expected business errors — return a Result.
- ❌ New top-level "Helpers" / "Utils" projects. Pick the right layer.
- ❌ Putting validation in the Presentation layer beyond GraphQL type shape.
- ❌ Storing raw session tokens or verification codes in the DB. Always SHA-256 hash first.
- ❌ Reading config via `IConfiguration["Foo:Bar"]` outside of `DependencyInjection.cs` — use a typed `IOptions<T>` instead.