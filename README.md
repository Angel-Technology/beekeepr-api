# BuzzKeepr API

Backend for the BuzzKeepr app. .NET 10, GraphQL-first (Hot Chocolate), PostgreSQL via EF Core.

For architectural patterns and feature deep-dives, see:
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — high-level architecture decisions
- [`.claude/architectural-patterns.md`](.claude/architectural-patterns.md) — layering rules, conventions, "where to add things" cheat sheet
- [`.claude/features/`](.claude/features/) — one file per feature (auth, user mgmt, identity verification)

---

## Solution layout

```
BuzzKeepr.Presentation/    # ASP.NET Core host, GraphQL, webhooks
BuzzKeepr.Application/     # Use cases, services, DTOs, repository interfaces
BuzzKeepr.Domain/          # Entities, enums (no I/O)
BuzzKeepr.Infrastructure/  # EF Core, migrations, external API clients
BuzzKeepr.UnitTests/
BuzzKeepr.IntegrationTests/
```

---

## Prerequisites

- **.NET 10 SDK** — `dotnet --version` should report 10.x
- **Docker** — used to run the local PostgreSQL container
- **`dotnet-ef` CLI** — for migrations

```bash
dotnet tool install --global dotnet-ef
```

If `dotnet-ef` isn't on your `PATH`:

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
```

---

## Running locally

The local setup is **PostgreSQL in Docker, API on the host via `dotnet run`**. The repo also has a `Dockerfile` for production builds, but local dev does not run the API in a container.

### 1. Start PostgreSQL

```bash
docker compose up -d
```

This boots the `buzzkeepr-postgres` container defined in [`compose.yml`](compose.yml):
- image: `postgres:17`
- database: `buzzkeepr_dev`
- user / password: `postgres` / `postgres`
- exposed on `localhost:5432`
- data is persisted in the named volume `buzzkeepr_postgres_data`

Check it's healthy:

```bash
docker compose ps
```

Stop it with `docker compose down` (data survives) or `docker compose down -v` (data wiped — useful for a clean reset).

### 2. Configure secrets

Real credentials are kept in **.NET User Secrets**, not in `appsettings.Development.json`. The Presentation project has `<UserSecretsId>3822f249-d96f-4f49-bf27-17f0145dacb4</UserSecretsId>` in its csproj, so secrets are loaded automatically in the Development environment.

Set them once per machine:

```bash
cd BuzzKeepr.Presentation

dotnet user-secrets set "Email:ResendApiKey"           "re_..."
dotnet user-secrets set "Persona:ApiKey"               "persona_sandbox_..."
dotnet user-secrets set "Persona:WebhookSecrets:0"     "wbhsec_..."
dotnet user-secrets set "CheckrTrust:ClientId"         "..."
dotnet user-secrets set "CheckrTrust:ClientSecret"     "..."
```

List what's set:

```bash
dotnet user-secrets list
```

The non-secret bits (base URLs, the Persona inquiry template id, the Google client ids, CORS origins, the local DB connection string) live in [`BuzzKeepr.Presentation/appsettings.Development.json`](BuzzKeepr.Presentation/appsettings.Development.json) and are committed.

> **Rule of thumb:** if the value would let someone make API calls or impersonate the app — it goes in user-secrets. Anything else can stay in `appsettings.Development.json`.

### 3. Apply migrations

See [Database migrations](#database-migrations) below. First time on a fresh DB:

```bash
dotnet ef database update \
  --project BuzzKeepr.Infrastructure \
  --startup-project BuzzKeepr.Presentation \
  --context BuzzKeeprDbContext
```

### 4. Run the API

```bash
dotnet run --project BuzzKeepr.Presentation
```

Default endpoints (Development):
- GraphQL: `http://localhost:5158/graphql` (Banana Cake Pop UI in browser)
- Swagger: `http://localhost:5158/swagger`
- Persona webhook: `POST http://localhost:5158/webhooks/persona`

---

## Database migrations

Schema is managed with EF Core migrations. Migrations live in `BuzzKeepr.Infrastructure/Persistence/Migrations/` and target `BuzzKeeprDbContext`.

Two projects are always in the command:
- `--project BuzzKeepr.Infrastructure` — where migrations are written/read
- `--startup-project BuzzKeepr.Presentation` — so EF can load `appsettings.Development.json` + user-secrets to find the connection string

### Add a migration after changing entities or DbContext

```bash
dotnet ef migrations add <DescriptiveName> \
  --project BuzzKeepr.Infrastructure \
  --startup-project BuzzKeepr.Presentation \
  --context BuzzKeeprDbContext \
  --output-dir Persistence/Migrations
```

Naming convention: PascalCase, action-first, scoped to one logical change. Examples already in the repo: `InitialCreate`, `ExpandAuthSchema`, `AddAuthFlows`, `AddPinCodeSignIn`, `AddPersonaIdentityVerification`, `AddUserTermsAcceptance`.

### Apply pending migrations to the local DB

```bash
dotnet ef database update \
  --project BuzzKeepr.Infrastructure \
  --startup-project BuzzKeepr.Presentation \
  --context BuzzKeeprDbContext
```

### Roll back a migration that hasn't been applied yet

You wrote a migration, the file looks wrong, you haven't run `database update`:

```bash
dotnet ef migrations remove \
  --project BuzzKeepr.Infrastructure \
  --startup-project BuzzKeepr.Presentation \
  --context BuzzKeeprDbContext
```

### Roll back a migration that **has** been applied

Update the database back to a known migration (use the migration name without the timestamp), then remove:

```bash
dotnet ef database update <PreviousMigrationName> \
  --project BuzzKeepr.Infrastructure \
  --startup-project BuzzKeepr.Presentation \
  --context BuzzKeeprDbContext

dotnet ef migrations remove \
  --project BuzzKeepr.Infrastructure \
  --startup-project BuzzKeepr.Presentation \
  --context BuzzKeeprDbContext
```

### Nuke and reset the local DB

When the schema gets too tangled to migrate cleanly in dev:

```bash
docker compose down -v       # drops the volume → wipes the DB
docker compose up -d
dotnet ef database update \
  --project BuzzKeepr.Infrastructure \
  --startup-project BuzzKeepr.Presentation \
  --context BuzzKeeprDbContext
```

### Hosted environments

In hosted environments the API can apply migrations on startup with:

```bash
Database__ApplyMigrationsOnStartup=true
```

Acceptable for early staging. For production, prefer running `dotnet ef database update` (or a generated SQL script) as an explicit deploy step.

---

## Configuration reference

Sections expected at startup. Bold = must come from user-secrets in dev.

| Section | Key | Notes |
| ------- | --- | ----- |
| `Database` | `Provider` | `Postgres` (only provider wired today) |
| `Database` | `ConnectionString` | Local default points at the docker container |
| `Database` | `ApplyMigrationsOnStartup` | Optional, hosted only |
| `Email` | `FromEmail` | Sender address shown to users |
| `Email` | `FrontendBaseUrl` | Used in email links |
| `Email` | **`ResendApiKey`** | Resend API key |
| `Email` | `ResendBaseUrl` | Optional override |
| `Google` | `ClientIds` | Array — one entry per platform |
| `Persona` | `ApiBaseUrl` | Default `https://api.withpersona.com` |
| `Persona` | **`ApiKey`** | Persona API key |
| `Persona` | `InquiryTemplateId` | `itmpl_...` |
| `Persona` | **`WebhookSecrets`** | Array, supports rotation |
| `CheckrTrust` | `ApiBaseUrl` | Default `https://api.checkrtrust.com` |
| `CheckrTrust` | **`ClientId`** | OAuth2 client id |
| `CheckrTrust` | **`ClientSecret`** | OAuth2 client secret |
| `Cors` | `AllowedOrigins` | Array of origins allowed to call the API with credentials |

---

## Frontend integration

Send credentials so the `buzzkeepr_session` cookie round-trips:

```ts
fetch("http://localhost:5158/graphql", {
  method: "POST",
  credentials: "include",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ query, variables })
});
```

For native (Expo) clients, prefer Bearer auth — store the session token returned from sign-in and send it as `Authorization: Bearer <token>`.

Make sure your origin is in `Cors:AllowedOrigins` (default: `http://localhost:3000`).

Smoke test order: `requestEmailSignIn` → `verifyEmailSignIn` → `currentUser` → `signOut`.

---

## Build and test

```bash
dotnet restore
dotnet build
dotnet test
```

Test projects: `BuzzKeepr.UnitTests`, `BuzzKeepr.IntegrationTests`.
