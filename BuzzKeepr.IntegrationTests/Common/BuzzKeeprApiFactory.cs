using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.Users;
using BuzzKeepr.IntegrationTests.Common.Fakes;
using BuzzKeepr.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuzzKeepr.IntegrationTests.Common;

public sealed class BuzzKeeprApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    public FakeEmailSignInSender FakeEmailSender { get; } = new();
    public FakeWelcomeEmailSender FakeWelcomeSender { get; } = new();
    public FakeGoogleTokenVerifier FakeGoogleVerifier { get; } = new();
    public FakePersonaClient FakePersona { get; } = new();
    public FakeCheckrTrustClient FakeCheckrTrust { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Postgres",
                ["Database:ConnectionString"] = postgres.ConnectionString,
                ["Database:ApplyMigrationsOnStartup"] = "true",
                ["Email:FrontendBaseUrl"] = "http://localhost:3000",
                ["Email:ResendApiKey"] = "re_test_dummy_key",
                ["Email:SignInTemplateId"] = "test-signin-template-id",
                ["Email:WelcomeTemplateId"] = "test-welcome-template-id",
                ["Google:ClientIds:0"] = "test-google-client-id.apps.googleusercontent.com",
                ["Persona:ApiBaseUrl"] = "https://persona.test.invalid",
                ["Persona:ApiKey"] = "persona_test_dummy",
                ["Persona:InquiryTemplateId"] = "itmpl_test",
                ["Persona:WebhookSecrets:0"] = "wbhsec_test_dummy",
                ["CheckrTrust:ApiBaseUrl"] = "https://checkrtrust.test.invalid",
                ["CheckrTrust:ClientId"] = "checkr_test_client",
                ["CheckrTrust:ClientSecret"] = "checkr_test_secret",
                ["CheckrTrust:RulesetId"] = "08f2b453-c8d9-481b-8f7b-93f767b7fa1f",
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSignInSender>();
            services.RemoveAll<IWelcomeEmailSender>();
            services.RemoveAll<IGoogleTokenVerifier>();
            services.RemoveAll<IPersonaClient>();
            services.RemoveAll<ICheckrTrustClient>();

            services.AddSingleton(FakeEmailSender);
            services.AddSingleton(FakeWelcomeSender);
            services.AddSingleton(FakeGoogleVerifier);
            services.AddSingleton(FakePersona);
            services.AddSingleton(FakeCheckrTrust);

            services.AddScoped<IEmailSignInSender>(sp => sp.GetRequiredService<FakeEmailSignInSender>());
            services.AddScoped<IWelcomeEmailSender>(sp => sp.GetRequiredService<FakeWelcomeEmailSender>());
            services.AddScoped<IGoogleTokenVerifier>(sp => sp.GetRequiredService<FakeGoogleTokenVerifier>());
            services.AddScoped<IPersonaClient>(sp => sp.GetRequiredService<FakePersonaClient>());
            services.AddScoped<ICheckrTrustClient>(sp => sp.GetRequiredService<FakeCheckrTrustClient>());
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
        await dbContext.Sessions.ExecuteDeleteAsync();
        await dbContext.VerificationTokens.ExecuteDeleteAsync();
        await dbContext.ExternalAccounts.ExecuteDeleteAsync();
        await dbContext.Users.ExecuteDeleteAsync();
    }
}
