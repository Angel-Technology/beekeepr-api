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
            LastName = "Doe",
            Birthdate = "1990-01-01",
            AddressStreet1 = "123 Main St",
            AddressCity = "San Francisco",
            AddressSubdivision = "CA",
            AddressPostalCode = "94105",
            CountryCode = "US",
            LicenseNumberLast4 = "1234",
            LicenseExpirationDate = "2030-06-30"
        };

        var (token, userId) = await SignInAsync();
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
        Assert.Equal("Doe", user.VerifiedLastName);
        Assert.Equal("94105", user.VerifiedAddressPostalCode);
        Assert.Equal("1234", user.VerifiedLicenseLast4);
        Assert.NotNull(user.PersonaVerifiedAtUtc);
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

        var (token, userId) = await SignInAsync();
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

        var (token, userId) = await SignInAsync();
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
    public async Task Webhook_WithInvalidSignature_Returns401AndDoesNotMutateUser()
    {
        const string inquiryId = "inq_persona_unauthorized";
        factory.FakePersona.NextCreateInquiryResult = new CreatePersonaInquiryResult
        {
            Success = true,
            InquiryId = inquiryId,
            InquiryStatus = "created"
        };

        var (token, userId) = await SignInAsync();
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

    private async Task<(string Token, Guid UserId)> SignInAsync()
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
        return (data.Session!.Token, data.User!.Id);
    }

    private sealed record StartInquiryData(StartInquiryPayload StartPersonaInquiry);
    private sealed record StartInquiryPayload(bool Success, bool CreatedNewInquiry, string? InquiryId, string? Error);

    private sealed record VerifyData(VerifyPayload VerifyEmailSignIn);
    private sealed record VerifyPayload(VerifyUser? User, VerifySession? Session);
    private sealed record VerifyUser(Guid Id);
    private sealed record VerifySession(string Token);
}
