# Deploy & branching

## The model

```
feature branches  ──PR──►  main  ────►  Render service: buzzkeepr-api-prod
```

- **`main`** — long-lived. Production tracker. All feature branches PR into here. Render auto-deploys to production on every push.
- **Feature branches** — short-lived (`feature/welcome-emails`, `fix/persona-race`, etc.). PR to `main`.

One environment, one long-lived branch. Trunk-based, no GitFlow ceremony.

## What runs where

### CI (pre-merge validation) — GitHub Actions

`.github/workflows/ci.yml` runs on every PR + every push to `main`:

1. Sets up .NET 10
2. Restores NuGet packages (cached)
3. Builds in Release config
4. Runs unit tests
5. Runs integration tests (Testcontainers spins up Postgres on the runner — Docker is pre-installed on GitHub-hosted ubuntu)

Required-status-check this workflow on the `main` branch in GitHub branch protection — that's what blocks merging a red PR.

### CD (post-merge deploy) — Render

`render.yaml` at the repo root defines the service as Infrastructure-as-Code. When you push to `main`, Render:

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
3. Render reads `render.yaml`, proposes the `buzzkeepr-api-prod` service, and asks for any secret values
4. Fill in **all the `sync: false` env vars** (see checklist below)
5. Apply

Render will start the first deploy immediately.

### 2. GitHub — branch protection

For `main`:
- Settings → Branches → Add protection rule
- Require pull request before merging
- Require an approving review
- Require status checks to pass — select the `build-and-test` job from `CI` workflow
- (Optional) Require linear history

### 3. Persona webhook URL

After the first deploy, Render gives you a URL like `https://buzzkeepr-api-prod.onrender.com`. Update the Persona dashboard:
- Webhook URL: `https://buzzkeepr-api-prod.onrender.com/webhooks/persona`

### 4. RevenueCat webhook URL

In the RevenueCat dashboard (Project Settings → Webhooks):
- Webhook URL: `https://buzzkeepr-api-prod.onrender.com/webhooks/revenuecat`

Set the **Authorization Header** value in the dashboard to a long random string, and mirror it in the Render service's `RevenueCat__WebhookAuthorizationToken` env var. RevenueCat doesn't HMAC-sign webhooks — this header is the only auth, so treat it like a secret.

## Env var checklist

Set in Render UI under the service → Environment.

| Key | Value |
| --- | --- |
| `Database__ConnectionString` | Neon prod branch URL |
| `Email__ResendApiKey` | prod Resend API key |
| `Email__SignInTemplateId` | `e7042412-...` |
| `Email__WelcomeTemplateId` | `601eecd1-...` |
| `Email__FrontendBaseUrl` | `https://app.buzzkeepr.com` |
| `Google__ClientIds__0` | prod OAuth client id |
| `Apple__ClientIds__0` | iOS bundle id (e.g. `com.buzzkeepr.app`) |
| `Persona__ApiKey` | live key |
| `Persona__InquiryTemplateId` | live `itmpl_...` |
| `Persona__ThemeSetId` | live `theset_...` (optional — leave empty to use the template's dashboard default) |
| `Persona__WebhookSecrets__0` | prod webhook secret |
| `CheckrTrust__ClientId` | live prod creds |
| `CheckrTrust__ClientSecret` | live prod creds |
| `CheckrTrust__RulesetIds__0` | `08f2b453-...` (felonies, all-time) |
| `CheckrTrust__RulesetIds__1` | `40b1e7c2-...` (misdemeanors, 7-year) |
| `Auth__AppApiKey` | a long random string the prod frontend embeds |
| `RevenueCat__SecretApiKey` | prod RevenueCat project's secret API key |
| `RevenueCat__WebhookAuthorizationToken` | long random string mirroring the Authorization Header set in the RevenueCat webhook config |
| `Apns__KeyContents` | full contents of the `.p8` APNs auth key, BEGIN/END lines inclusive |
| `Apns__KeyId` | 10-char Key ID from the Apple Developer portal (e.g. `UKBU4495CF`) |
| `Apns__TeamId` | 10-char Team ID (e.g. `37PSB2TKJ2`) |
| `Apns__BundleId` | iOS bundle id (same value as `Apple__ClientIds__0`) |
| `Apns__UseSandbox` | `false` for App Store builds. Set `true` only if pointing at TestFlight or dev builds — those issue sandbox-endpoint device tokens that get rejected as `BadDeviceToken` against the production endpoint. |
| `Sentry__Dsn` | prod project DSN |
| `Sentry__Environment` | `production` (optional — defaults to the lowercased ASP.NET env name) |
| `Cors__AllowedOrigins__0` | `https://app.buzzkeepr.com` |

## Notes

- **Service runs as `ASPNETCORE_ENVIRONMENT=Production`** — `appsettings.Production.json` is the env-overlay file. No Swagger, no relaxed CORS, no dev shortcuts.

- **Migrations apply on startup** via `Database__ApplyMigrationsOnStartup=true`. Cleanest for our scale. If you ever need a separate migration step (e.g. running migrations against a different DB user), we can split that out into a `predeploy.sh` Render command.

- **Cold starts** — on Render's `starter` plan, services don't spin down. Free tier does. Pay for `starter` from day one for any user-facing service.

- **APNs sandbox vs production** — TestFlight and Xcode dev builds issue device tokens that only work against `api.sandbox.push.apple.com`. App Store builds issue tokens that only work against `api.push.apple.com`. The prod service is set to production (`Apns__UseSandbox=false`) — TestFlight tokens will hit it with `BadDeviceToken` and the notifier will prune them. If you need to keep TestFlight working alongside prod, either (a) flip `Apns__UseSandbox` to `true` temporarily, or (b) stand up a sandbox-pointed service for the TestFlight cohort.
