using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuzzKeepr.Application.IdentityVerification.Models;
using BuzzKeepr.Domain.Enums;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.IntegrationTests.IdentityVerification;

[Collection(IntegrationTestCollection.Name)]
public sealed class PersonaTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string WebhookSecret = "wbhsec_test_dummy";

    private readonly BuzzKeeprApiFactory factory = new(postgres);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Webhook_WithValidSignatureForApprovedInquiry_UpdatesUserAndPersistsVerifiedFields()
    {
        const string inquiryId = "inq_persona_approved";
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = inquiryId,
            InquiryStatus = "created"
        };
        factory.FakePersona.NextGovernmentIdDataResult = new PersonaGovernmentIdDataResult
        {
            Success = true,
            FirstName = "Jane",
            MiddleName = "Quinn",
            LastName = "Doe",
            Birthdate = "1990-01-01",
            LicenseState = "ca"
        };

        var (token, userId, _) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation { startPersonaInquiry { success inquiryId error } }");

        var webhookBody = BuildInquiryWebhookBody(inquiryId, "approved");
        var signatureHeader = BuildSignatureHeader(WebhookSecret, webhookBody);

        var webhookResponse = await PostWebhookAsync(webhookBody, signatureHeader);
        Assert.Equal(HttpStatusCode.NoContent, webhookResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        Assert.Equal(IdentityVerificationStatus.Approved, user.IdentityVerificationStatus);
        Assert.Equal(PersonaInquiryStatus.Approved, user.PersonaInquiryStatus);
        Assert.Equal("Jane", user.VerifiedFirstName);
        Assert.Equal("Quinn", user.VerifiedMiddleName);
        Assert.Equal("Doe", user.VerifiedLastName);
        Assert.Equal("1990-01-01", user.VerifiedBirthdate);
        Assert.Equal("CA", user.VerifiedLicenseState);
        Assert.NotNull(user.PersonaVerifiedAtUtc);
    }

    [Fact]
    public async Task Webhook_ApprovedInquiryForEmailSignInUser_TriggersDeferredWelcomeWithVerifiedFirstName()
    {
        const string inquiryId = "inq_persona_deferred_welcome";
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = inquiryId,
            InquiryStatus = "created"
        };
        factory.FakePersona.NextGovernmentIdDataResult = new PersonaGovernmentIdDataResult
        {
            Success = true,
            FirstName = "Sienna",
            LastName = "Park",
            Birthdate = "1992-05-12",
            LicenseState = "ny"
        };

        var (token, userId, email) = await SignInAsync();

        Assert.DoesNotContain(factory.FakeWelcomeSender.Sent, w => w.Email == email);

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation { startPersonaInquiry { success error } }");

        var webhookBody = BuildInquiryWebhookBody(inquiryId, "approved");
        var webhookResponse = await PostWebhookAsync(webhookBody, BuildSignatureHeader(WebhookSecret, webhookBody));
        Assert.Equal(HttpStatusCode.NoContent, webhookResponse.StatusCode);

        var welcome = factory.FakeWelcomeSender.Sent.Single(w => w.Email == email);
        Assert.Equal("Sienna", welcome.DisplayName);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.NotNull(user.WelcomeEmailSentAtUtc);
    }

    [Fact]
    public async Task StartPersonaInquiry_ReusesPendingInquiry_RecreatesAfterRetryableStatus()
    {
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = "inq_first",
            InquiryStatus = "pending"
        };

        var (token, userId, _) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        var firstStart = await graphql.SendAsync<StartInquiryData>(
            "mutation { startPersonaInquiry { success createdNewInquiry inquiryId error } }");
        var firstPayload = firstStart.RequireData().StartPersonaInquiry;
        Assert.Null(firstPayload.Error);
        Assert.True(firstPayload.CreatedNewInquiry);
        Assert.Equal("inq_first", firstPayload.InquiryId);
        Assert.Single(factory.FakePersona.CreateInquiryCalls);

        var reuseStart = await graphql.SendAsync<StartInquiryData>(
            "mutation { startPersonaInquiry { success createdNewInquiry inquiryId error } }");
        var reusePayload = reuseStart.RequireData().StartPersonaInquiry;
        Assert.False(reusePayload.CreatedNewInquiry);
        Assert.Equal("inq_first", reusePayload.InquiryId);
        Assert.Single(factory.FakePersona.CreateInquiryCalls);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
            await dbContext.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.IdentityVerificationStatus, IdentityVerificationStatus.Failed));
        }

        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = "inq_second",
            InquiryStatus = "pending"
        };

        var retryStart = await graphql.SendAsync<StartInquiryData>(
            "mutation { startPersonaInquiry { success createdNewInquiry inquiryId error } }");
        var retryPayload = retryStart.RequireData().StartPersonaInquiry;
        Assert.True(retryPayload.CreatedNewInquiry);
        Assert.Equal("inq_second", retryPayload.InquiryId);
        Assert.Equal(2, factory.FakePersona.CreateInquiryCalls.Count);
    }

    [Fact]
    public async Task Webhook_StaleEventArrivingAfterApproval_IsIgnored()
    {
        const string inquiryId = "inq_stale_test";
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = inquiryId,
            InquiryStatus = "created"
        };
        factory.FakePersona.NextGovernmentIdDataResult = new PersonaGovernmentIdDataResult
        {
            Success = true,
            FirstName = "Approved",
            LastName = "User"
        };

        var (token, userId, _) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation { startPersonaInquiry { success error } }");

        var approvedAt = DateTime.UtcNow;
        var approvedBody = BuildInquiryWebhookBody(inquiryId, "approved", approvedAt);
        var approvedResponse = await PostWebhookAsync(approvedBody, BuildSignatureHeader(WebhookSecret, approvedBody));
        Assert.Equal(HttpStatusCode.NoContent, approvedResponse.StatusCode);

        var staleCompletedAt = approvedAt.AddSeconds(-30);
        var staleBody = BuildInquiryWebhookBody(inquiryId, "completed", staleCompletedAt);
        var staleResponse = await PostWebhookAsync(staleBody, BuildSignatureHeader(WebhookSecret, staleBody));
        Assert.Equal(HttpStatusCode.NoContent, staleResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        Assert.Equal(IdentityVerificationStatus.Approved, user.IdentityVerificationStatus);
        Assert.Equal(PersonaInquiryStatus.Approved, user.PersonaInquiryStatus);

        Assert.Single(factory.FakePersona.GetGovernmentIdDataCalls);
    }

    [Fact]
    public async Task StartPersonaInquiry_WithoutActiveSubscription_BlocksAndReturnsSubscriptionRequired()
    {
        var (token, _, _) = await SignInAsync(withActiveSubscription: false);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        var response = await graphql.SendAsync<StartInquiryData>(
            "mutation { startPersonaInquiry { success createdNewInquiry inquiryId error subscriptionRequired } }");

        var payload = response.RequireData().StartPersonaInquiry;
        Assert.False(payload.Success);
        Assert.True(payload.SubscriptionRequired);
        Assert.Equal("Active subscription required to start identity verification.", payload.Error);
        Assert.Empty(factory.FakePersona.CreateInquiryCalls);
    }

    [Fact]
    public async Task StartPersonaInquiry_WithActiveSubscription_CreatesInquiry()
    {
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = "inq_gated_pass",
            InquiryStatus = "created"
        };

        var (token, _, _) = await SignInAsync(); // default: subscription seeded as active
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        var response = await graphql.SendAsync<StartInquiryData>(
            "mutation { startPersonaInquiry { success createdNewInquiry inquiryId error subscriptionRequired } }");

        var payload = response.RequireData().StartPersonaInquiry;
        Assert.True(payload.Success);
        Assert.False(payload.SubscriptionRequired);
        Assert.Null(payload.Error);
        Assert.Equal("inq_gated_pass", payload.InquiryId);
        Assert.Single(factory.FakePersona.CreateInquiryCalls);
    }

    [Fact]
    public async Task StartPersonaInquiry_ReuseExistingInquiry_BypassesSubscriptionGate()
    {
        // Seed a user who already has a Persona inquiry in a non-retryable state. Even with no
        // subscription, they should be able to fetch the existing inquiry's status — we only gate
        // the path that would actually create a new (paid) inquiry.
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = "inq_existing",
            InquiryStatus = "pending"
        };

        var (token, userId, _) = await SignInAsync(); // start with active sub so we can create
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<StartInquiryData>(
            "mutation { startPersonaInquiry { success } }");
        Assert.Single(factory.FakePersona.CreateInquiryCalls);

        // Now lapse the subscription. The existing inquiry should still be reachable.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
            await dbContext.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.SubscriptionStatus, SubscriptionStatus.Expired));
        }

        var reuse = await graphql.SendAsync<StartInquiryData>(
            "mutation { startPersonaInquiry { success createdNewInquiry inquiryId subscriptionRequired error } }");

        var payload = reuse.RequireData().StartPersonaInquiry;
        Assert.True(payload.Success);
        Assert.False(payload.CreatedNewInquiry);
        Assert.False(payload.SubscriptionRequired);
        Assert.Equal("inq_existing", payload.InquiryId);
        Assert.Single(factory.FakePersona.CreateInquiryCalls); // no second call
    }

    [Fact]
    public async Task StartPersonaInquiry_RetryAfterFailedInquiry_RequiresActiveSubscription()
    {
        // Seed a user with a Failed inquiry (retryable status) and no active sub. A retry attempt
        // would burn a fresh Persona call, so the gate fires.
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = "inq_first_attempt",
            InquiryStatus = "pending"
        };

        var (token, userId, _) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<StartInquiryData>(
            "mutation { startPersonaInquiry { success } }");

        // Mark the inquiry as Failed and lapse the subscription.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
            await dbContext.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.IdentityVerificationStatus, IdentityVerificationStatus.Failed)
                    .SetProperty(u => u.SubscriptionStatus, SubscriptionStatus.Expired));
        }

        var retry = await graphql.SendAsync<StartInquiryData>(
            "mutation { startPersonaInquiry { success createdNewInquiry inquiryId subscriptionRequired error } }");

        var payload = retry.RequireData().StartPersonaInquiry;
        Assert.False(payload.Success);
        Assert.True(payload.SubscriptionRequired);
        Assert.Single(factory.FakePersona.CreateInquiryCalls); // no second Persona call attempted
    }

    [Fact]
    public async Task Webhook_DeclinedInquiry_SetsDeclinedAndDoesNotFetchVerifiedData()
    {
        const string inquiryId = "inq_persona_declined";
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = inquiryId,
            InquiryStatus = "created"
        };
        // If our handler ever wrongly fetched on Declined, this would leak into the user row.
        factory.FakePersona.NextGovernmentIdDataResult = new PersonaGovernmentIdDataResult
        {
            Success = true,
            FirstName = "ShouldNotBeUsed",
            LastName = "ShouldNotBeUsed"
        };

        var (token, userId, _) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation { startPersonaInquiry { success error } }");

        var webhookBody = BuildInquiryWebhookBody(inquiryId, "declined");
        var response = await PostWebhookAsync(webhookBody, BuildSignatureHeader(WebhookSecret, webhookBody));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        Assert.Equal(IdentityVerificationStatus.Declined, user.IdentityVerificationStatus);
        Assert.Equal(PersonaInquiryStatus.Declined, user.PersonaInquiryStatus);
        Assert.Null(user.VerifiedFirstName);
        Assert.Null(user.VerifiedLastName);
        Assert.Null(user.PersonaVerifiedAtUtc);
        Assert.Empty(factory.FakePersona.GetGovernmentIdDataCalls);
    }

    [Fact]
    public async Task Webhook_ApprovedButGovernmentIdFetchFails_LeavesVerifiedFieldsNull()
    {
        // Pins the documented sharp edge: if Persona's GET /verifications/government-id call fails
        // at webhook time, we still flip the user to Approved but VerifiedFirstName stays null and
        // PersonaVerifiedAtUtc is not stamped. A reconciliation job (not yet built) would fix this.
        const string inquiryId = "inq_persona_approved_fetch_fails";
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = inquiryId,
            InquiryStatus = "created"
        };
        factory.FakePersona.NextGovernmentIdDataResult = new PersonaGovernmentIdDataResult
        {
            Success = false
        };

        var (token, userId, _) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation { startPersonaInquiry { success error } }");

        var webhookBody = BuildInquiryWebhookBody(inquiryId, "approved");
        var response = await PostWebhookAsync(webhookBody, BuildSignatureHeader(WebhookSecret, webhookBody));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        Assert.Equal(IdentityVerificationStatus.Approved, user.IdentityVerificationStatus);
        Assert.Equal(PersonaInquiryStatus.Approved, user.PersonaInquiryStatus);
        Assert.Null(user.VerifiedFirstName);
        Assert.Null(user.VerifiedLastName);
        Assert.Null(user.PersonaVerifiedAtUtc);
        Assert.Single(factory.FakePersona.GetGovernmentIdDataCalls);
    }

    [Fact]
    public async Task Webhook_CompletedThenApproved_PersistsVerifiedDataOnceAndDoesNotRefetch()
    {
        const string inquiryId = "inq_persona_completed_then_approved";
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = inquiryId,
            InquiryStatus = "created"
        };
        factory.FakePersona.NextGovernmentIdDataResult = new PersonaGovernmentIdDataResult
        {
            Success = true,
            FirstName = "Marcus",
            MiddleName = null,
            LastName = "Hill",
            Birthdate = "1988-07-04",
            LicenseState = "wa"
        };

        var (token, userId, _) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation { startPersonaInquiry { success error } }");

        var completedAt = DateTime.UtcNow;
        var completedBody = BuildInquiryWebhookBody(inquiryId, "completed", completedAt);
        var completedResponse = await PostWebhookAsync(completedBody, BuildSignatureHeader(WebhookSecret, completedBody));
        Assert.Equal(HttpStatusCode.NoContent, completedResponse.StatusCode);

        await using (var checkScope = factory.Services.CreateAsyncScope())
        {
            var checkDb = checkScope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
            var afterCompleted = await checkDb.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
            Assert.Equal(IdentityVerificationStatus.Completed, afterCompleted.IdentityVerificationStatus);
            Assert.Equal("Marcus", afterCompleted.VerifiedFirstName);
            Assert.Null(afterCompleted.VerifiedMiddleName);
            Assert.Equal("WA", afterCompleted.VerifiedLicenseState);
        }

        // Swap the fake's payload — if our handler ever re-fetched on the approved transition,
        // these "Should*" values would leak into the user row, replacing the Marcus/Hill data.
        factory.FakePersona.NextGovernmentIdDataResult = new PersonaGovernmentIdDataResult
        {
            Success = true,
            FirstName = "ShouldNotOverwrite",
            LastName = "ShouldNotOverwrite"
        };

        var approvedBody = BuildInquiryWebhookBody(inquiryId, "approved", completedAt.AddSeconds(5));
        var approvedResponse = await PostWebhookAsync(approvedBody, BuildSignatureHeader(WebhookSecret, approvedBody));
        Assert.Equal(HttpStatusCode.NoContent, approvedResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        Assert.Equal(IdentityVerificationStatus.Approved, user.IdentityVerificationStatus);
        Assert.Equal("Marcus", user.VerifiedFirstName);
        Assert.Equal("Hill", user.VerifiedLastName);
        Assert.Single(factory.FakePersona.GetGovernmentIdDataCalls);
    }

    [Fact]
    public async Task Webhook_WithInvalidSignature_Returns401AndDoesNotMutateUser()
    {
        const string inquiryId = "inq_persona_unauthorized";
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = inquiryId,
            InquiryStatus = "created"
        };

        var (token, userId, _) = await SignInAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation { startPersonaInquiry { success inquiryId error } }");

        var webhookBody = BuildInquiryWebhookBody(inquiryId, "approved");
        var forgedSignature = BuildSignatureHeader("wbhsec_attacker_guess", webhookBody);

        var response = await PostWebhookAsync(webhookBody, forgedSignature);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        var user = await dbContext.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.Equal(IdentityVerificationStatus.Created, user.IdentityVerificationStatus);
        Assert.Null(user.PersonaVerifiedAtUtc);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string body, string signatureHeader)
    {
        var http = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/persona")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Persona-Signature", signatureHeader);
        return await http.SendAsync(request);
    }

    private static string BuildInquiryWebhookBody(string inquiryId, string status, DateTime? inquiryUpdatedAtUtc = null)
    {
        var updatedAt = (inquiryUpdatedAtUtc ?? DateTime.UtcNow).ToString("o");
        var payload = new
        {
            data = new
            {
                attributes = new
                {
                    name = $"inquiry.{status}",
                    payload = new
                    {
                        data = new
                        {
                            type = "inquiry",
                            id = inquiryId,
                            attributes = new
                            {
                                status,
                                updated_at = updatedAt
                            }
                        }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string BuildSignatureHeader(string secret, string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{body}.{timestamp}"));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"t={timestamp},v1={hex}";
    }

    private async Task<(string Token, Guid UserId, string Email)> SignInAsync(bool withActiveSubscription = true)
    {
        var email = $"persona-{Guid.NewGuid():N}@buzzkeepr.test";
        var http = factory.CreateClient();
        var graphql = new GraphQLClient(http);

        await graphql.SendAsync<JsonElement>(
            "mutation($input: RequestEmailSignInInput!) { requestEmailSignIn(input: $input) { success } }",
            new { input = new { email } });
        var code = factory.FakeEmailSender.RequireLatestFor(email).Code;
        var verify = await graphql.SendAsync<VerifyData>(
            "mutation($input: VerifyEmailSignInInput!) { verifyEmailSignIn(input: $input) { user { id } session { token } } }",
            new { input = new { email, code } });
        var data = verify.RequireData().VerifyEmailSignIn;

        if (withActiveSubscription)
            await SeedActiveSubscriptionAsync(data.User!.Id);

        return (data.Session!.Token, data.User!.Id, email);
    }

    private async Task SeedActiveSubscriptionAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        await dbContext.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.SubscriptionStatus, SubscriptionStatus.Trialing)
                .SetProperty(u => u.SubscriptionEntitlement, "premium")
                .SetProperty(u => u.SubscriptionProductId, "premium_monthly")
                .SetProperty(u => u.SubscriptionStore, (SubscriptionStore?)SubscriptionStore.AppStore)
                .SetProperty(u => u.SubscriptionCurrentPeriodEndUtc, (DateTime?)DateTime.UtcNow.AddDays(7))
                .SetProperty(u => u.SubscriptionWillRenew, (bool?)true));
    }

    private sealed record StartInquiryData(StartInquiryPayload StartPersonaInquiry);
    private sealed record StartInquiryPayload(bool Success, bool CreatedNewInquiry, string? InquiryId, string? Error, bool SubscriptionRequired);

    private sealed record VerifyData(VerifyPayload VerifyEmailSignIn);
    private sealed record VerifyPayload(VerifyUser? User, VerifySession? Session);
    private sealed record VerifyUser(Guid Id);
    private sealed record VerifySession(string Token);
}
