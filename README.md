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
- future session and verification-token tables

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
