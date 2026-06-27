# BuzzKeepr Backend Architecture

This document is the source of truth for how the BuzzKeepr backend is structured. Update this file whenever we make a meaningful architectural change.

## Goals

- Keep the backend intuitive to read and extend.
- Keep dependency flow simple and enforceable.
- Separate business rules from delivery concerns like HTTP, GraphQL, and database access.
- Make local development and production configuration predictable.

## Project Layout

```text
BuzzKeepr.Presentation/
BuzzKeepr.Application/
BuzzKeepr.Domain/
BuzzKeepr.Infrastructure/
BuzzKeepr.UnitTests/
BuzzKeepr.IntegrationTests/
```

## Layer Responsibilities

### `BuzzKeepr.Presentation`

Owns delivery concerns.

Responsibilities:
- ASP.NET Core startup and host configuration
- GraphQL endpoint via Hot Chocolate
- Swagger/OpenAPI
- Authentication and authorization middleware
- transport-specific GraphQL types and endpoint wiring

Must not:
- Contain database access logic
- Contain core business rules
- Call infrastructure implementations directly when an application abstraction exists

### `BuzzKeepr.Application`

Owns use-case orchestration.

Responsibilities:
- Application services and use cases
- Interfaces for infrastructure dependencies
- DTOs used between layers
- Validation and workflow coordination
- Dependency injection registration for application services

Must not:
- Depend on Presentation
- Depend on concrete infrastructure implementations
- Contain persistence framework details

### `BuzzKeepr.Domain`

Owns core business concepts.

Responsibilities:
- Entities
- Value objects
- Enums
- Domain rules
- Domain exceptions

Must not:
- Depend on any other project
- Contain HTTP, GraphQL, logging, or database-specific concerns

### `BuzzKeepr.Infrastructure`

Owns technical implementations.

Responsibilities:
- EF Core and database context
- Repository implementations
- External service integrations
- Logging provider setup and persistence-related concerns
- Dependency injection registration for infrastructure services

Must not:
- Contain API endpoint definitions
- Introduce business rules that belong in Domain or Application

## Dependency Direction

Dependencies must flow inward:

- `Presentation -> Application`
- `Application -> Domain`
- `Infrastructure -> Application`
- `Infrastructure -> Domain`
- `Domain -> nothing`

Presentation should not depend directly on Infrastructure unless startup wiring requires bootstrapping extension methods. Even then, business logic must still flow through Application.

## Configuration Strategy

We are not creating a separate Environment layer.

Environment-specific behavior will use standard ASP.NET Core configuration:

- `appsettings.json`
- `appsettings.Development.json`
- `appsettings.Production.json`
- environment variables
- secret storage for local secrets when needed

YAML is not the default application runtime configuration format for this backend. If YAML appears later, it should be for deployment tooling such as CI/CD or Kubernetes, not as the primary .NET app configuration source.

## API Strategy

The backend is GraphQL-first.

Primary API surface:

- GraphQL for frontend data access and auth flows

Secondary API surface:

- minimal REST endpoints only for operational concerns such as health checks or future webhook callbacks

Tools:

- Swagger/OpenAPI for REST exploration and testing
- Hot Chocolate for GraphQL server support

GraphQL resolvers in `Presentation` must call `Application` services. They must not access EF Core or repositories directly.

## Auth Direction

The backend is being designed to support frontend auth flows similar to Better Auth requirements, with backend-owned persistence.

Current auth target:

- Google sign-in
- email sign-in
- passwordless email verification

Current persistence concepts:

- `Users` as the canonical internal identity
- `ExternalAccounts` for provider links such as Google
- `Sessions` for session lifecycle and cookie-backed auth state
- `VerificationTokens` for passwordless email sign-in and email verification

Planned API direction:

- GraphQL mutations for sign-in and account linkage
- GraphQL queries for current user and session-aware identity reads
- minimal REST only where third-party callbacks or operational endpoints require it

Current auth mutation set:

- `requestEmailSignIn`
- `verifyEmailSignIn`
- `signInWithGoogle`

Google sign-in should not trust raw identity fields from the client. The backend verifies a Google-issued ID token and only then creates or links the internal user/session records.

For native clients such as Expo, the backend should expose the application session token in auth mutation payloads so the client can store it securely and send it back as a bearer token on subsequent requests. Cookie-backed sessions can still be kept for browser clients.

Current session access model:

- sign-in mutations are public
- authenticated identity is resolved from the backend-managed session cookie
- frontend GraphQL queries such as `currentUser` should rely on that cookie-backed session state
- email sign-in delivery uses Resend

## Social Graph

BuzzKeepr models user-to-user relationships as three discrete aggregates rather than a single "relationship" table. Each captures a distinct intent and has its own lifecycle.

### Entities

- `Friendship` — one row per ordered `(RequesterId, AddresseeId)` pair, with `Status` in `Pending` or `Accepted`. Accepting flips the status in place; declining or cancelling deletes the row.
- `UserBlock` — asymmetric. `(BlockerId, BlockedId)` is unique. Blocking is one-way: blocker hides blocked, blocked also loses search visibility of blocker.
- `UserFlag` — moderation signal. `(FlaggerId, FlaggedUserId)` is unique, so the flag count is *distinct flaggers*, not total flag events. Re-flagging is a no-op.

### Invariants

- **Flag implies block + unfriend.** Flagging a user atomically: inserts the `UserFlag`, removes any existing friendship between them, and creates a `UserBlock`. Re-flagging stays idempotent (already-blocked stays blocked, already-not-friends stays not-friends).
- **Block removes friendship.** Blocking atomically deletes any existing friendship (pending or accepted) between the two users.
- **Search hides blocked-either-direction.** A user the caller has blocked, or who has blocked the caller, is filtered out of `searchUsers`. Because flag implies block, flagged users disappear automatically.
- **No auto-action on flag count.** Flag totals are stored but no threshold triggers suspension or hiding. Moderation is out-of-band by design.
- **Block visibility is silent.** Neither blocking nor flagging notifies the target.

### Cross-direction race handling

The `Friendship` unique index is on `(RequesterId, AddresseeId)`, which prevents `A→B` from being inserted twice but allows `A→B` and `B→A` to coexist briefly if both sides race-click "Add friend." `ConnectionsService.SendFriendRequestAsync` resolves this by looking up the friendship between the two users in either direction before inserting:

- If a reverse pending row exists (`B→A` already there when `A` sends to `B`), it auto-accepts — both sides clearly want the connection.
- If a same-direction row exists (pending or accepted), the call is an idempotent no-op.

### Block-state non-disclosure

`SendFriendRequestAsync` distinguishes `BlockedByCaller`, `BlockedByTarget`, and `TargetNotFound` flags internally. The GraphQL mutation maps `BlockedByCaller` to an actionable error ("Unblock this user before sending a friend request.") but collapses `BlockedByTarget` and `TargetNotFound` to the same generic copy — `"Unable to send friend request."` — so the caller cannot probe whether a specific user has blocked them or whether the user exists at all.

### Search result annotation

Every row returned from `searchUsers` carries a `ViewerFriendshipState` field — one of `None`, `RequestSent`, `RequestReceived`, `Friends` — relative to the authenticated viewer. The projection is computed in SQL via three `EXISTS` subqueries hitting the `(RequesterId, Status)` / `(AddresseeId, Status)` helper indexes, so it adds bounded per-row cost at typeahead page sizes. The frontend uses this to choose between the Add / Pending / Accept / Friends button without a second round-trip.

### Contact-visibility gating on list projections

Every list query that returns a user row (`searchUsers`, `friends`, `incomingFriendRequests`, `outgoingFriendRequests`, `blockedUsers`) exposes the same set of self-supplied profile fields, but the contact-info subset (`phoneNumber`, `googleVoicePhone`, `whatsAppPhone`, `instagramHandle`, `telegramHandle`, `signalPhone`) is filtered per-row by the row user's `ContactVisibility` setting *and* the viewer's relationship to that user.

The rule is applied in SQL as part of the projection — not in service code, not in the GraphQL layer. That's intentional: the gate is the contract, so any code path that builds a `UserSearchResultDto` or `UserConnectionDto` gets it for free. A malicious client crafting a query asking for `phoneNumber` gets `null` back, not the value, because Postgres never selects the field in the first place when the gate is closed.

| Where the row appears | ConnectionsOnly | Private |
| --- | --- | --- |
| `searchUsers` | emitted only if viewer & target are accepted friends (EXISTS subquery per row) | always `null` |
| `friends` | emitted (every row is an accepted friend, so the relationship is constant — no EXISTS needed) | always `null` |
| `incomingFriendRequests` / `outgoingFriendRequests` | always `null` (pending ≠ friend yet) | always `null` |
| `blockedUsers` | always `null` (block removes any friendship) | always `null` |

The `Public` value was removed from `ContactVisibility` in PR 3 — contact info no longer flows to strangers via any code path. The only opt-in to share contact data is "with my accepted friends."

Three of the four connection queries don't even need a friendship subquery because the viewer-to-target relationship is implied by which list you're calling — pending requests are by definition not friends, blocked users by definition are not friends, and friends list rows are by definition friends. The Incoming/Outgoing/Blocked projections statically null all 6 contact fields. Only `searchUsers` needs the per-row friendship check (search results can be strangers, pending, or accepted-friend in any combination). `UserRepository.Search` inlines six `EXISTS` subqueries (one per contact field) — Postgres typically dedupes these into a single common subexpression, but if profiling shows pain we can rewrite the projection as a CTE.

The `ProfileVisibility` and `ContactVisibility` enums themselves are always exposed on every row so the frontend can render a hint ("hidden — connect to see") without having to infer state. `Private` users are also excluded from `searchUsers` entirely (the row doesn't appear at all), not just contact-filtered.

What never leaks through these projections, regardless of visibility settings: Persona-verified PII (`verifiedFirstName/MiddleName/LastName/Birthdate/LicenseState`, `personaVerifiedAtUtc`), internal verification state (`identityVerificationStatus`, `personaInquiryId`, `personaInquiryStatus`), `email`, `termsAcceptedAtUtc`, `subscription`, `deletedAtUtc`. Those only appear on `currentUser` / `getUserById`, which are gated to the caller's own row.

### Soft-delete behavior

Users use `DeletedAtUtc` + a global query filter for soft-delete with a 72-hour grace period. Friendship/block/flag rows are *not* deleted on soft-delete — they remain so a cancelled deletion restores the social graph intact. Joining these aggregates through `dbContext.Users` (which applies the global filter) naturally hides soft-deleted users from friend lists and search results during the grace window. Hard delete cascades through the `Requester`/`Addressee`/`Blocker`/`Flagger` FKs; the `Blocked`/`FlaggedUser` FKs are `Restrict` to avoid Postgres' "multiple cascade paths through the same User row" error, so child rows must be cleared before a hard purge (which the deletion sweeper handles).

### Current API surface

Mutations (all require an authenticated session):

- `sendFriendRequest`, `acceptFriendRequest`, `declineFriendRequest`, `cancelFriendRequest`
- `removeFriend`
- `blockUser`, `unblockUser`
- `flagUser`

Queries (all cursor-paginated via `[UsePaging]`):

- `friends`, `incomingFriendRequests`, `outgoingFriendRequests`, `blockedUsers`

### Deferred

Notifications for incoming friend requests (GraphQL subscriptions and/or APNs/FCM push) are intentionally not yet implemented. The frontend polls `incomingFriendRequests` until this is revisited.

## Logging Strategy

Logging is a cross-cutting concern, not a standalone architecture layer.

Initial plan:

- Use `ILogger<T>` throughout the application
- Prefer structured logging
- Add Serilog later if richer sinks or formatting become necessary

## Dependency Injection Strategy

Each layer that registers services should own its registration entry point.

Expected pattern:

- `BuzzKeepr.Application/DependencyInjection.cs`
- `BuzzKeepr.Infrastructure/DependencyInjection.cs`

Presentation will call those registration methods during startup.

## Testing Strategy

We will keep tests separate by purpose:

- `BuzzKeepr.UnitTests` for fast business logic tests
- `BuzzKeepr.IntegrationTests` for database, API, and wiring tests

Unit tests should target Domain and Application behavior first. Integration tests should verify infrastructure and presentation wiring against realistic configurations.

## Decision Log

### 2026-03-01

Accepted initial architecture:

- Use `Presentation`, `Application`, `Domain`, and `Infrastructure` layers
- Do not create a separate `Environment` layer
- Use standard ASP.NET Core appsettings files for environments
- Support both REST and GraphQL
- Use Swagger for REST documentation and testing
- Use Hot Chocolate for GraphQL
- Treat logging as cross-cutting, not as its own top-level layer

### 2026-03-02

Accepted persistence and API metadata defaults:

- Use PostgreSQL as the primary relational database
- Use EF Core with `BuzzKeepr.Infrastructure` as the migrations assembly
- Keep runtime startup in `BuzzKeepr.Presentation`
- Expose Swagger documentation under the API name `BuzzKeepr.API`

Accepted API and auth direction:

- Prefer GraphQL over REST for product-facing frontend APIs
- Keep GraphQL resolvers thin and route all business behavior through `Application`
- Model auth around internal users plus external provider accounts
- Target Google sign-in and passwordless email verification as the first auth flows

Accepted auth persistence shape:

- Add `Sessions` for backend-managed auth state
- Add `VerificationTokens` for passwordless sign-in and email verification
- Keep GraphQL-specific input and payload models in `Presentation`
- Keep application use-case input and output models in `Application`

### 2026-06-20

Accepted social graph shape:

- Model friends, blocks, and flags as three distinct entities (`Friendship`, `UserBlock`, `UserFlag`) rather than a unified relationship table — each captures different intent and has its own lifecycle
- Flag is a one-click action (no reason field) and atomically implies unfriend + block
- Search hides users blocked in either direction so blocking severs discoverability symmetrically
- Block / flag state is non-disclosing: `BlockedByTarget` and `TargetNotFound` collapse to the same generic error in `sendFriendRequest`
- No automatic action triggers on flag-count thresholds — moderation remains out-of-band
- Notifications for incoming friend requests are deferred; the frontend polls `incomingFriendRequests` until subscriptions or push are added

### 2026-06-25

Accepted contact-visibility gating on list projections:

- All five list queries that return user rows (`searchUsers`, `friends`, `incomingFriendRequests`, `outgoingFriendRequests`, `blockedUsers`) expose the same set of self-supplied profile fields plus the verification badge / timestamps
- Contact-info subset (6 fields: phone, Google Voice, WhatsApp, Instagram, Telegram, Signal) is gated server-side per row by `ContactVisibility` + viewer-friendship status
- Gate is enforced in the SQL projection, not service code or GraphQL — making it impossible for any client request to bypass
- "Strict" gating: even on the friends list, `Private` contact stays hidden — a friend who later flips to Private has their fields nulled, not surfaced retroactively
- Persona-verified PII, internal verification state, email, subscription, terms acceptance, and deletion timestamps stay off these projections entirely — those only appear on `currentUser` / `getUserById`, which are gated to the caller's own row

### 2026-06-26

Removed `Public` from `ContactVisibility`:

- Enum now has only `ConnectionsOnly` and `Private` — no way to share contact info with strangers
- Migration `RemovePublicContactVisibility` converts existing `Public` rows → `ConnectionsOnly` (preserves the user's "I opted to share" intent; closest available semantic)
- The "Share with everyone" toggle on the profile page is gone — UI collapses to "Share with connections" / "Don't share"
- Default stays `Private` — safe-by-default opt-in
- Incoming/Outgoing/Blocked projections now statically null the 6 contact fields (no friendship → no contact visibility ever)
- Breaking GraphQL change: clients sending `contactVisibility: PUBLIC` get a schema-validation error. Frontend must drop the value from any mutation it sends.
