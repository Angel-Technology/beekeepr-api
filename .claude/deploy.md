# Deploy & branching

## The model

```
feature branches  ──PR──►  develop  ────►  Render service: buzzkeepr-api-develop
                              │
                              │  PR (or fast-forward merge after dev verification)
                              ▼
                            main    ────►  Render service: buzzkeepr-api-prod
```

- **`develop`** — long-lived. Always deployable. All feature branches PR into here. Render auto-deploys it to the develop environment on every push.
- **`main`** — long-lived. Production tracker. We promote from `develop` to `main` whenever we want to ship to prod. Render auto-deploys it to production on every push.
- **Feature branches** — short-lived (`feature/welcome-emails`, `fix/persona-race`, etc.). PR to `develop`.

This is GitFlow-lite. Two environments, two long-lived branches, no `release/` or `hotfix/` ceremony unless we ever need it.

## What runs where

### CI (pre-merge validation) — GitHub Actions

`.github/workflows/ci.yml` runs on every PR + every push to `main`/`develop`:

1. Sets up .NET 10
2. Restores NuGet packages (cached)
3. Builds in Release config
4. Runs unit tests
5. Runs integration tests (Testcontainers spins up Postgres on the runner — Docker is pre-installed on GitHub-hosted ubuntu)

Required-status-check this workflow on the `main` and `develop` branches in GitHub branch protection — that's what blocks merging a red PR.

### CD (post-merge deploy) — Render

`render.yaml` at the repo root defines both services as Infrastructure-as-Code. When you push to a branch, the matching Render service:

1. Pulls the commit
2. Builds the Docker image from `Dockerfile`
3. Boots the new container
4. Runs `Database.Migrate()` on startup (we set `Database__ApplyMigrationsOnStartup=true`)
5. Health-checks `GET /health`
6. Cuts over traffic when healthy
7. Drains the old container

No GitHub Actions step for the deploy itself — Render handles it.

## One-time setup

### 1. Render — connect the Blueprint

1. Sign in to Render → **New** → **Blueprint**
2. Connect this GitHub repo
3. Render reads `render.yaml`, proposes two services (`buzzkeepr-api-develop` and `buzzkeepr-api-prod`), and asks for any secret values
4. Fill in **all the `sync: false` env vars** (see checklist below) — different values per service
5. Apply

Render will start the first deploy of each service immediately.

### 2. GitHub — branch protection

For both `main` and `develop`:
- Settings → Branches → Add protection rule
- Require pull request before merging
- Require status checks to pass — select the `build-and-test` job from `CI` workflow
- (Optional) Require linear history

Particularly for `main`: also require an approving review.

### 3. Persona webhook URLs

After the first deploy of each service, Render gives you a URL like `https://buzzkeepr-api-develop.onrender.com`. Update the Persona dashboard:
- Develop env → webhook URL: `https://buzzkeepr-api-develop.onrender.com/webhooks/persona`
- Prod env → webhook URL: `https://buzzkeepr-api-prod.onrender.com/webhooks/persona`

Each env should use its own webhook secret so a leak in one doesn't compromise the other.

### 4. RevenueCat webhook URLs

In the RevenueCat dashboard, configure one webhook per environment (Project Settings → Webhooks):
- Develop env → URL `https://buzzkeepr-api-develop.onrender.com/webhooks/revenuecat`
- Prod env → URL `https://buzzkeepr-api-prod.onrender.com/webhooks/revenuecat`

Set the **Authorization Header** value in the dashboard to a long random string per environment, and mirror it in the matching Render service's `RevenueCat__WebhookAuthorizationToken` env var. RevenueCat doesn't HMAC-sign webhooks — this header is the only auth, so treat it like a secret.

## Env var checklist (per service)

Set in Render UI under each service → Environment.

| Key | Develop value | Production value |
| --- | --- | --- |
| `Database__ConnectionString` | Neon develop branch URL | Neon prod branch URL |
| `Email__ResendApiKey` | dev Resend API key | prod Resend API key (or same key with separate sender) |
| `Email__SignInTemplateId` | `e7042412-...` | same |
| `Email__WelcomeTemplateId` | `601eecd1-...` | same |
| `Email__FrontendBaseUrl` | `https://dev.buzzkeepr.com` (or wherever) | `https://app.buzzkeepr.com` |
| `Google__ClientIds__0` | dev OAuth client id | prod OAuth client id |
| `Persona__ApiKey` | sandbox key | live key |
| `Persona__InquiryTemplateId` | sandbox `itmpl_...` | live `itmpl_...` |
| `Persona__ThemeSetId` | sandbox `theset_...` (optional — leave empty to use the template's dashboard default) | live `theset_...` (optional) |
| `Persona__WebhookSecrets__0` | dev webhook secret | prod webhook secret |
| `CheckrTrust__ClientId` | **test-env creds from your Checkr account exec** (deterministic mock results, no billing). Make sure the rep enables the `instant_criminal` product for the test account. | live prod creds |
| `CheckrTrust__ClientSecret` | test-env creds | prod creds |
| `CheckrTrust__RulesetIds__0` | `08f2b453-...` (felonies, all-time) | same (or different ruleset for prod) |
| `CheckrTrust__RulesetIds__1` | `40b1e7c2-...` (misdemeanors, 7-year) | same |
| `Auth__AppApiKey` | a long random string the dev frontend embeds | a different long random string for the prod frontend |
| `RevenueCat__SecretApiKey` | dev RevenueCat project's secret API key | prod project's secret API key |
| `RevenueCat__WebhookAuthorizationToken` | dev value (matches the Authorization Header value set in the RevenueCat webhook config) | prod value — **different secret per environment** so a leak in one doesn't compromise the other |
| `Sentry__Dsn` | dev project DSN | prod project DSN (separate Sentry project recommended) |
| `Cors__AllowedOrigins__0` | `https://dev.buzzkeepr.com` | `https://app.buzzkeepr.com` |

## Notes

- **Both services run as `ASPNETCORE_ENVIRONMENT=Production`** — that means `appsettings.Production.json` is the env-overlay file for both. The differences between dev and prod environments come from the per-service env vars, not from .NET environment switching. This keeps the develop env behaving identically to prod (no Swagger, no relaxed CORS, no dev shortcuts) — only the data sources differ.

- **Migrations apply on startup** via `Database__ApplyMigrationsOnStartup=true`. Cleanest for our scale. If you ever need a separate migration step (e.g. running migrations against a different DB user), we can split that out into a `predeploy.sh` Render command.

- **Sentry environment tagging** — Sentry currently picks the env tag from `builder.Environment.EnvironmentName`, which is "Production" for both deploys. Both services will appear as "Production" in Sentry. To distinguish: either (a) add a separate `Sentry:Environment` config key the SDK reads explicitly, or (b) just create two separate Sentry projects (recommended — cleaner permissions and quotas). Going with (b) is in the env var table above.

- **Cold starts** — on Render's `starter` plan, services don't spin down. Free tier does. Pay for `starter` from day one for any user-facing service.

- **`develop` env URL is public-facing** — it'll be reachable on the internet at `https://buzzkeepr-api-develop.onrender.com`. Either gate everything behind `Auth:AppApiKey` (which we already do for `createUser`), or put it behind Render's IP allowlist if you want to lock it down to your office/VPN.
