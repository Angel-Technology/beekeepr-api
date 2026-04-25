# Testing Philosophy

> "The more your tests resemble the way your software is used, the more confidence they can give you."
> — Kent C. Dodds

This codebase follows Kent C. Dodds' testing approach: the **Testing Trophy**, not the pyramid. Most of our value comes from a **smaller number of high-fidelity integration tests** that exercise real services through real boundaries — not a swarm of tiny unit tests against private mocks.

If a test wouldn't catch a real regression that affects a user, don't write it.

---

## The Testing Trophy applied to BuzzKeepr

```
        ┌─────────┐   E2E (only when truly needed)
        └─────────┘
     ┌───────────────┐
     │  INTEGRATION  │   ← the bulk of our tests live here
     │  (the bulk)   │
     └───────────────┘
   ┌───────────────────┐
   │   Unit (small)    │   ← only for pure logic with branches
   └───────────────────┘
 ┌───────────────────────┐
 │  Static (free wins)   │   ← nullable refs, build warnings, EF model snapshot
 └───────────────────────┘
```

### Static (the floor — already on)

We get this for free; don't fight it.
- `<Nullable>enable</Nullable>` is on every project.
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is the long-term goal — turn it on per-project as we clean up.
- EF model snapshot is a static "test" that catches schema drift between code and migrations.
- The C# compiler catches the kinds of bugs JS shops write unit tests for.

### Unit tests — small and few

In `BuzzKeepr.UnitTests`. Use these only for:
- Pure functions with non-trivial branching (e.g. token parsing, normalization, mapping).
- Algorithms that would be expensive to set up in an integration test.
- Edge cases of a specific helper that the integration tests can't easily reach (e.g. `CsrfOriginAllowlist.IsAllowed` with malformed input).

**Do not** unit-test:
- A method that just delegates to a repository.
- Result-object mapping that has no logic.
- A handler that "just calls the service" — let the integration test cover it.
- Anything that requires more than ~3 lines of mock setup. That's a smell; the unit isn't a unit anymore.

### Integration tests — where most tests live

In `BuzzKeepr.IntegrationTests`. **These are the default.**

Each integration test:
1. Boots the real ASP.NET Core pipeline via `BuzzKeeprApiFactory : WebApplicationFactory<Program>`.
2. Uses a **real PostgreSQL database** (Testcontainers, started once per test run).
3. Replaces only the **outbound HTTP boundaries** with deterministic fakes:
   - `FakeEmailSignInSender` — captures sent codes in memory, never hits Resend.
   - `FakeGoogleTokenVerifier` — returns a preset identity instead of validating a real Google JWT.
   - `FakePersonaClient` — returns canned inquiry / government-id data.
   - `FakeCheckrTrustClient` — returns canned check / profile responses.
4. Calls the API the same way the frontend will — POST `/graphql` with JSON.

This is the high-fidelity layer. A green integration test means: GraphQL parsing works, auth middleware ran, CSRF middleware ran, the DB constraints accepted the write, the business rule fired, and the response shape matches what the frontend expects. That's a lot of confidence per test.

### E2E — basically never

We don't run a separate E2E layer today. Integration tests already exercise the full server stack. E2E only makes sense once we have a frontend that can be driven, and even then only for the 2–3 most critical user flows (sign in, run a check). Don't pre-build it.

---

## What we mock and what we don't

Mock at the **process boundary**, never inside the process.

| Boundary | Real or fake? | Why |
| --- | --- | --- |
| PostgreSQL | **Real** (Testcontainers) | EF behavior, snake_case columns, unique indexes, `ExecuteUpdateAsync`, JSON serialization — all of these have Postgres-specific behavior |
| Resend (email) | **Fake** | We don't want to send real emails or burn credits |
| Persona API | **Fake** | Sandbox is rate-limited and unreliable for repeatable tests |
| Checkr Trust API | **Fake** | Each call is potentially billed; not deterministic |
| Google ID-token verification | **Fake** | Real verification requires actual Google-issued tokens |
| Time | Real `DateTime.UtcNow` for the happy path; pass an explicit cutoff/now value when testing windows (sliding TTL, expiry, cleanup grace). Don't introduce an `IClock` until at least the second test forces the issue. |
| `HttpContext` / cookies | **Real** — go through `WebApplicationFactory.CreateClient()` and let the framework handle them |
| Session token generation | **Real** — `RandomNumberGenerator` is fine in tests; just capture the value out of the response |

**Never** swap an internal interface (`IAuthRepository`, `IUserService`, `IIdentityVerificationService`) in an integration test. If you find yourself wanting to, you're writing a unit test in the wrong place.

---

## Test naming and structure

```
BuzzKeepr.UnitTests/
  Auth/
    AuthServiceTests.cs
  Common/
    {tiny helpers — keep this folder thin}

BuzzKeepr.IntegrationTests/
  Common/
    BuzzKeeprApiFactory.cs        # WebApplicationFactory<Program> + container wiring
    PostgresFixture.cs            # Testcontainers Postgres lifecycle
    GraphQLClient.cs              # POST /graphql helper, returns parsed JSON
    Fakes/
      FakeEmailSignInSender.cs
      FakeGoogleTokenVerifier.cs
      FakePersonaClient.cs
      FakeCheckrTrustClient.cs
  Auth/
    EmailSignInTests.cs
    GoogleSignInTests.cs
    SessionTests.cs               # sliding TTL, sign out, CSRF
  Users/
    UserManagementTests.cs
  IdentityVerification/
    PersonaTests.cs
    CheckrTrustTests.cs
```

**Test naming convention:** `Method_Scenario_ExpectedOutcome`.
- `RequestEmailSignIn_WithValidEmail_SendsCodeAndPersistsToken`
- `CurrentUser_WithSessionOlderThan24h_ExtendsExpiry`
- `Graphql_WithCookieAndNoOrigin_Returns403`

The class name is the *thing under test* (`SessionTests`, `EmailSignInTests`); the method names are the *behaviors*.

---

## Concrete rules

1. **One assertion concept per test.** Multiple `Assert.Equal` calls are fine if they're verifying the same outcome ("the user was created with the right fields"). If they're verifying multiple unrelated things, that's two tests.

2. **Type the response shape.** When parsing GraphQL responses, deserialize into typed records (`record VerifyEmailSignInResponse(...)`), not `JsonElement`. Anonymous types and `dynamic` decay quickly.

3. **No "happy path + 3 sad paths" inflation.** If a sad path is enforced by the type system or a DB constraint, you don't need a test for it. Test the sad paths that humans can actually reach.

4. **No setup helpers that hide intent.** Don't write a `CreateValidUser()` fixture if the test reads more clearly with the literal payload inline. Inline > DRY when it's about *test readability*.

5. **Don't share mutable state between tests.** The `BuzzKeeprApiFactory` is shared (it's expensive to spin up Postgres), but each test should clean up after itself or use unique data (random emails, etc.) so order doesn't matter.

6. **Don't snapshot-test JSON.** Snapshots are noise generators. Assert on the specific fields that matter.

7. **A failing test should tell you what broke in one line.** If you have to read the test source to understand what it was checking, the test is wrong.

8. **Coverage isn't a goal.** Confidence is. A 100%-covered file with all-path tests is worse than a 60%-covered file where the integration tests exercise the real flows.

---

## Running tests

```bash
# All tests
dotnet test

# Unit only (fast, no Docker)
dotnet test BuzzKeepr.UnitTests

# Integration only (needs Docker for Testcontainers)
dotnet test BuzzKeepr.IntegrationTests

# A single class
dotnet test --filter "FullyQualifiedName~EmailSignInTests"

# A single test
dotnet test --filter "FullyQualifiedName~EmailSignInTests.RequestEmailSignIn_WithValidEmail_SendsCode"
```

Integration tests need Docker running (Testcontainers boots a Postgres container per test session). The container is shared across the whole test run via xUnit's `ICollectionFixture`, so the cost is paid once.

---

## Adding a test for a new feature

1. **Start integration.** Write the GraphQL flow as the frontend will use it. Boot the factory, set up any fake-client preconditions (e.g. "FakeCheckrTrustClient will return this profile_id"), POST the mutation, assert on the typed response and on the DB state if it matters.
2. **Drop to unit only when forced.** If a branch is too awkward to reach through the integration test (e.g. an internal normalization that several callers share), then write a focused unit test against that single function.
3. **Update this doc.** If the new feature changes the testing approach (e.g. a new external integration that needs a fake), add it to the table above so the pattern stays explicit.
