# beekeepr-api

Backend for the BuzzKeepr app.

The source of truth for architectural decisions lives in [ARCHITECTURE.md](/Users/samuelwemimo/Angel-Technologies/beekeepr-api/ARCHITECTURE.md).

## Planned Structure

```text
BuzzKeepr.Presentation/
BuzzKeepr.Application/
BuzzKeepr.Domain/
BuzzKeepr.Infrastructure/
BuzzKeepr.UnitTests/
BuzzKeepr.IntegrationTests/
```

## Current Status

The solution structure, PostgreSQL container configuration, GraphQL endpoint, and Swagger setup are in place.

## API Direction

This backend is GraphQL-first for product-facing frontend features.

- GraphQL endpoint: `/graphql`
- Swagger is kept for operational REST endpoints and debugging
- Business logic should be reused through `Application` services, regardless of transport

## Auth Direction

The current auth design target is:

- Google sign-in
- email sign-in
- passwordless email verification

The database is being shaped around:

- `Users` as the internal identity record
- `ExternalAccounts` for linked providers like Google
- `Sessions` for session lifecycle
- `VerificationTokens` for passwordless email sign-in and email verification

## Current Auth Schema

The auth-related tables currently modeled in the backend are:

- `Users`
- `ExternalAccounts`
- `Sessions`
- `VerificationTokens`

These support the planned flows for:

- Google sign-in
- passwordless email sign-in
- email verification
- backend-managed session state

## Current GraphQL Auth Mutations

The backend now exposes these auth-oriented GraphQL mutations:

- `requestEmailSignIn`
- `verifyEmailSignIn`
- `signInWithGoogle`

Current development behavior:

- `requestEmailSignIn` uses Resend for code delivery
- sign-in mutations now issue a session cookie instead of returning the session token in GraphQL payloads
- `currentUser` should be read through the cookie-backed session
- production auth should keep HTTP-only cookies and secure cookie settings

## Auth Access Model

Public mutations:

- `requestEmailSignIn`
- `verifyEmailSignIn`
- `signInWithGoogle`

Cookie-backed identity reads:

- `currentUser`

This is intentional. A user must be able to start sign-in without already being authenticated. The protected part is reading or acting as an authenticated user after a valid session cookie has been issued.

## Email Provider

The backend is wired for Resend as the first email provider.

Relevant config lives under the `Email` section:

- `FromEmail`
- `FrontendBaseUrl`
- `ResendApiKey`
- `ResendBaseUrl`

In development:

- set `ResendApiKey` to send real emails through Resend (required at startup)

The current email flow sends a 5-digit code that the frontend submits back to `verifyEmailSignIn`.

## Local Postgres

Start the local development database with Docker:

```bash
docker compose up -d
```

Stop it with:

```bash
docker compose down
```

The default development connection string is configured for the local container in [appsettings.Development.json](/Users/samuelwemimo/Angel-Technologies/beekeepr-api/BuzzKeepr.Presentation/appsettings.Development.json).

## Frontend Local Integration Checklist

1. Ensure your frontend origin is in [appsettings.Development.json](/Users/samuelwemimo/Angel-Technologies/beekeepr-api/BuzzKeepr.Presentation/appsettings.Development.json) under `Cors:AllowedOrigins` (default: `http://localhost:3000`).
2. Apply all pending migrations.
3. Run the API and use `/graphql` from the frontend with credentials included.

For frontend GraphQL calls, include credentials so the `buzzkeepr_session` cookie is sent:

```ts
fetch("http://localhost:5158/graphql", {
  method: "POST",
  credentials: "include",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ query, variables })
});
```

Recommended auth smoke test order:

- `requestEmailSignIn`
- `verifyEmailSignIn`
- `currentUser`
- `signOut`

## Database Setup

This project uses PostgreSQL for local development and EF Core migrations for schema management.

### 1. Install the EF Core CLI Tool

```bash
dotnet tool install --global dotnet-ef
```

If `dotnet-ef` is not on your `PATH` yet, add it for the current shell session:

```bash
export PATH="$PATH:/Users/samuelwemimo/.dotnet/tools"
```

Verify the install:

```bash
dotnet-ef --version
```

### 2. Start the Local PostgreSQL Container

```bash
docker compose up -d
docker compose ps
```

This starts the local PostgreSQL instance defined in [compose.yml](/Users/samuelwemimo/Angel-Technologies/beekeepr-api/compose.yml).

### 3. Create the First Migration

```bash
dotnet ef migrations add InitialCreate --project BuzzKeepr.Infrastructure --startup-project BuzzKeepr.Presentation --context BuzzKeeprDbContext --output-dir Persistence/Migrations
```

What this command does:

- uses `BuzzKeepr.Infrastructure` as the migrations project
- uses `BuzzKeepr.Presentation` as the startup project so EF can load app configuration
- targets the `BuzzKeeprDbContext`
- writes migration files into `BuzzKeepr.Infrastructure/Persistence/Migrations`

### 4. Apply the Migration to the Database

```bash
dotnet ef database update --project BuzzKeepr.Infrastructure --startup-project BuzzKeepr.Presentation --context BuzzKeeprDbContext
```

What this command does:

- creates the target database if needed
- creates the `__EFMigrationsHistory` table
- applies any pending migrations to the configured PostgreSQL database

### 5. Build the Solution

```bash
dotnet restore
dotnet build
```

This verifies that the solution, package references, and project wiring are valid before or after running migrations.

If you change the auth entities or `BuzzKeeprDbContext`, generate a new migration and apply it again:

```bash
dotnet ef migrations add <MigrationName> --project BuzzKeepr.Infrastructure --startup-project BuzzKeepr.Presentation --context BuzzKeeprDbContext --output-dir Persistence/Migrations
dotnet ef database update --project BuzzKeepr.Infrastructure --startup-project BuzzKeepr.Presentation --context BuzzKeeprDbContext
```
