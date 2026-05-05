using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Billing;
using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.Users;
using BuzzKeepr.Infrastructure.IdentityVerification;
using BuzzKeepr.IntegrationTests.Common.Fakes;
using BuzzKeepr.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BuzzKeepr.IntegrationTests.Common;

public sealed class BuzzKeeprApiFactory(PostgresFixture postgres, string? appApiKey = null) : WebApplicationFactory<Program>
{
    public FakeEmailSignInSender FakeEmailSender { get; } = new();
    public FakeWelcomeEmailSender FakeWelcomeSender { get; } = new();
    public FakeGoogleTokenVerifier FakeGoogleVerifier { get; } = new();
    public FakePersonaClient FakePersona { get; } = new();
    public FakeCheckrTrustClient FakeCheckrTrust { get; } = new();
    public FakeRevenueCatClient FakeRevenueCat { get; } = new();

    public const string RevenueCatWebhookToken = "rc_test_dummy_auth";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            var inMemoryConfig = new Dictionary<string, string?>
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
                ["CheckrTrust:RulesetIds:0"] = "08f2b453-c8d9-481b-8f7b-93f767b7fa1f",
                ["CheckrTrust:RulesetIds:1"] = "40b1e7c2-f6fc-4fe2-856b-dccabc4c56b3",
                ["RevenueCat:ApiBaseUrl"] = "https://revenuecat.test.invalid",
                ["RevenueCat:SecretApiKey"] = "rc_test_secret",
                ["RevenueCat:WebhookAuthorizationToken"] = RevenueCatWebhookToken,
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000"
            };

            if (appApiKey is not null)
                inMemoryConfig["Auth:AppApiKey"] = appApiKey;

            configuration.AddInMemoryCollection(inMemoryConfig);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSignInSender>();
            services.RemoveAll<IWelcomeEmailSender>();
            services.RemoveAll<IGoogleTokenVerifier>();
            services.RemoveAll<IPersonaClient>();
            services.RemoveAll<ICheckrTrustClient>();
            services.RemoveAll<IRevenueCatClient>();

            services.AddSingleton(FakeEmailSender);
            services.AddSingleton(FakeWelcomeSender);
            services.AddSingleton(FakeGoogleVerifier);
            services.AddSingleton(FakePersona);
            services.AddSingleton(FakeCheckrTrust);
            services.AddSingleton(FakeRevenueCat);

            services.AddScoped<IEmailSignInSender>(sp => sp.GetRequiredService<FakeEmailSignInSender>());
            services.AddScoped<IWelcomeEmailSender>(sp => sp.GetRequiredService<FakeWelcomeEmailSender>());
            services.AddScoped<IGoogleTokenVerifier>(sp => sp.GetRequiredService<FakeGoogleTokenVerifier>());
            services.AddScoped<IPersonaClient>(sp => sp.GetRequiredService<FakePersonaClient>());
            services.AddScoped<ICheckrTrustClient>(sp => sp.GetRequiredService<FakeCheckrTrustClient>());
            services.AddScoped<IRevenueCatClient>(sp => sp.GetRequiredService<FakeRevenueCatClient>());

            // Strip the auto-running renewal sweeper. Each test that exercises the sweeper
            // constructs its own instance and invokes a single sweep deterministically. Leaving
            // the registered IHostedService in place would race with test seed on host startup.
            var renewalRegistration = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(BackgroundCheckRenewalBackgroundService));
            if (renewalRegistration is not null)
                services.Remove(renewalRegistration);
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
