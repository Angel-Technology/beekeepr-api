# Observability

The plan: don't over-tool, but cover the three distinct concerns with the right specialist for each.

| Concern | Tool | Status |
| --- | --- | --- |
| **Errors / crashes** (backend + mobile, correlated) | **Sentry** | ✅ wired on backend; mobile SDK to be added when the app exists |
| **Server logs + uptime + status page** | **BetterStack** | ⏳ defer until we go public |
| **Product analytics** (mobile/web) | **PostHog** | ⏳ defer until launch |

`ILogger<T>` console output is the dev baseline and stays as-is — Render captures it for free during pre-launch testing.

---

## Sentry — what's wired up

### Configuration

`Sentry:Dsn` in `appsettings.json` (blank default) and `appsettings.Production.json` (placeholder). The Sentry SDK is a **no-op when the DSN is blank** — local dev and integration tests don't send anything anywhere.

### What it captures (defaults from `Sentry.AspNetCore`)
- Unhandled exceptions in any request pipeline (GraphQL resolvers, REST endpoints, hosted services)
- ASP.NET Core middleware errors
- EF Core query traces (sampled)
- HTTP client request traces (Persona/Checkr/Resend/Google)
- Structured log properties — anything you pass via `_logger.LogInformation("... {UserId}", userId)` lands as a tag

### What it does NOT send
- **`SendDefaultPii = false`** — no IP addresses, no auth headers
- **`MaxRequestBodySize = None`** — request bodies are NEVER captured. GraphQL bodies contain emails and verification codes; we don't want them in Sentry.
- **No session tokens, codes, or external API keys** — these are never logged anywhere in our code, so they can't leak via Sentry either

### Sample rates
- **Errors**: 100% (all exceptions go up)
- **Performance traces**: 100% in Development, 10% in Production. Adjust `TracesSampleRate` if traffic grows.

### Setting up

```bash
cd BuzzKeepr.Presentation
dotnet user-secrets set "Sentry:Dsn" "https://...@...ingest.sentry.io/..."
```

Get the DSN from Sentry → Settings → Projects → BuzzKeepr-API → Client Keys.

In production, set `Sentry__Dsn` as an environment variable on Render.

### Verifying it works

After setting the DSN, restart the API and trigger a deliberate exception. Easiest way: pick any auth-required mutation and call it without a bearer token — actually that won't throw, it just returns null. So instead:

```bash
curl -X POST http://localhost:5158/graphql \
  -H 'Content-Type: application/json' \
  -d '{"query":"query { __schema { directives { name args { type { kind } } } } }"}'
```
…doesn't throw either. To actually trigger a Sentry event during testing, add a temporary throw in any handler, run it, and watch your Sentry dashboard for the event. Remove the throw.

In production you'll see real exceptions show up automatically — first time something breaks, it's there with a stack trace + request context within seconds.

---

## When to add BetterStack

**Trigger**: when you start letting non-team users in, OR when log volume is high enough that scrolling Render's log viewer is no longer practical.

What we'd add:
- Switch the default logger to **Serilog** (`Serilog.AspNetCore` + `Serilog.Sinks.BetterStack`)
- Console sink stays for dev
- BetterStack sink kicks in when `BetterStack:SourceToken` is configured (same no-op-when-blank pattern as Sentry)
- Existing log call sites are already structured (`{UserId}`, `{InquiryId}`, etc.) — they work as-is with Serilog

Plus an uptime monitor on `GET /health` — that's a one-click setup in their dashboard, no code changes.

---

## When to add PostHog

**Trigger**: actual users on the app.

Goes on the **mobile/web client**, not the backend. The signup-completion / Persona-conversion / criminal-check funnels are all client-side flows — PostHog autocapture handles the bulk for free. The backend would only emit signal for events that happen *after* the client closes a session (Persona webhook approval, etc.) — that's maybe 2 server-side `posthog.capture()` calls and not worth wiring until needed.

---

## Things we're NOT doing (and why)

| Tool | Why not |
| --- | --- |
| **Datadog / New Relic / Splunk** | Wildly overpriced for our stage |
| **Self-hosted Seq / Grafana / Loki stack** | Great for SRE-staffed shops; not where we are |
| **OpenTelemetry as a destination** | OTel is the *protocol* — Sentry already speaks it. We don't need a separate OTel collector. |
| **Render's built-in metrics** | Fine for now, no separate setup. Revisit if we move off Render. |

---

## If we move off Render later

Sentry is host-agnostic — the DSN works from anywhere. BetterStack same. PostHog same. None of this is locked to a hosting choice, so the migration story stays clean.
