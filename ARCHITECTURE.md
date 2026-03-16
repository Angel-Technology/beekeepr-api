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

Current session access model:

- sign-in mutations are public
- authenticated identity is resolved from the backend-managed session cookie
- frontend GraphQL queries such as `currentUser` should rely on that cookie-backed session state
- email sign-in delivery uses Resend when configured and a development logger fallback otherwise

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
