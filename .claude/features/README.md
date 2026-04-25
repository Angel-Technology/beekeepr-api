# Features Index

One file per feature. Each file describes what the feature does, what tables it touches, what GraphQL/REST surface it exposes, and exactly which files to watch when changing it.

Read [`../architectural-patterns.md`](../architectural-patterns.md) first for the layering rules every feature follows.

| Feature | Status | Doc |
| ------- | ------ | --- |
| Email sign-in (passwordless) | Complete | [authentication-email-signin.md](authentication-email-signin.md) |
| Google sign-in (OAuth ID token) | Complete | [authentication-google-signin.md](authentication-google-signin.md) |
| User management (create, profile, terms) | Complete | [user-management.md](user-management.md) |
| Identity verification — Persona (KYC) | In-flight | [identity-verification-persona.md](identity-verification-persona.md) |
| Identity verification — Checkr Trust (instant criminal) | In-flight | [identity-verification-checkr.md](identity-verification-checkr.md) |

## Adding a new feature doc

Use one of the existing files as a template. Sections to keep (in order):

1. **Status** — Complete / In-flight / Not started.
2. **What it does** — plain English, in numbered steps.
3. **Database tables** — table name, purpose, key columns.
4. **GraphQL surface** — operation name, type, input, output, auth-required.
5. **REST surface (webhooks)** — only if applicable.
6. **External services** — vendor + required `appsettings.json` keys.
7. **Files to watch** — grouped by layer (Domain / Application / Infrastructure / Presentation).
8. **Conventions specific to this feature**.
9. **Common changes and where they live**.
10. **Known TODOs / sharp edges** — the things future-you needs to know.

When you add a new doc, also add it to the table above.
