using BuzzKeepr.Infrastructure.Auth;
using BuzzKeepr.Infrastructure.Configuration;
using BuzzKeepr.Infrastructure.IdentityVerification;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var databaseOptions = configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>() ?? new DatabaseOptions();

        if (string.IsNullOrWhiteSpace(databaseOptions.ConnectionString))
        {
            throw new InvalidOperationException(
                "Database:ConnectionString is required. Set it in appsettings, user secrets, or environment variables.");
        }

        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<EmailDeliveryOptions>(
            configuration.GetSection(EmailDeliveryOptions.SectionName));
        services.Configure<GoogleAuthOptions>(
            configuration.GetSection(GoogleAuthOptions.SectionName));
        services.Configure<PersonaOptions>(
            configuration.GetSection(PersonaOptions.SectionName));
        services.Configure<CheckrTrustOptions>(
            configuration.GetSection(CheckrTrustOptions.SectionName));

        var emailOptions = configuration
            .GetSection(EmailDeliveryOptions.SectionName)
            .Get<EmailDeliveryOptions>() ?? new EmailDeliveryOptions();

        if (string.IsNullOrWhiteSpace(emailOptions.ResendApiKey))
        {
            throw new InvalidOperationException(
                "Email:ResendApiKey is required. Set it in user secrets or environment variables.");
        }

        services.AddDbContext<BuzzKeeprDbContext>((serviceProvider, options) =>
        {
            // Resolve options from DI rather than closing over the local `databaseOptions` —
            // the latter snapshots the connection string at AddInfrastructure call time, which
            // is BEFORE WebApplicationFactory's ConfigureAppConfiguration overrides apply in
            // integration tests. Reading via IOptions defers to DbContext-instantiation time,
            // which is after all config sources are merged.
            var resolvedDatabaseOptions = serviceProvider
                .GetRequiredService<IOptions<DatabaseOptions>>()
                .Value;

            options.UseNpgsql(
                resolvedDatabaseOptions.ConnectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(BuzzKeeprDbContext).Assembly.FullName);
                });
        });

        services.AddMemoryCache();
        services.AddHttpClient<ResendEmailSignInSender>();
        services.AddHttpClient<ResendWelcomeEmailSender>();
        services.AddHttpClient<PersonaClient>((serviceProvider, httpClient) =>
        {
            var personaOptions = serviceProvider
                .GetRequiredService<IOptions<PersonaOptions>>()
                .Value;

            if (Uri.TryCreate(personaOptions.ApiBaseUrl, UriKind.Absolute, out var baseUri))
                httpClient.BaseAddress = baseUri;
        });
        services.AddHttpClient<CheckrTrustClient>((serviceProvider, httpClient) =>
        {
            var checkrTrustOptions = serviceProvider
                .GetRequiredService<IOptions<CheckrTrustOptions>>()
                .Value;

            if (Uri.TryCreate(checkrTrustOptions.ApiBaseUrl, UriKind.Absolute, out var baseUri))
                httpClient.BaseAddress = baseUri;
        });
        services.AddScoped<Application.Auth.IGoogleTokenVerifier, GoogleTokenVerifier>();
        services.AddScoped<Application.Auth.IEmailSignInSender, ResendEmailSignInSender>();
        services.AddScoped<Application.Users.IWelcomeEmailSender, ResendWelcomeEmailSender>();
        services.AddScoped<Application.Auth.IAuthRepository, AuthRepository>();
        services.AddScoped<Application.IdentityVerification.IIdentityVerificationRepository, IdentityVerificationRepository>();
        services.AddScoped<Application.IdentityVerification.ICheckrTrustClient, CheckrTrustClient>();
        services.AddScoped<Application.IdentityVerification.IPersonaClient, PersonaClient>();
        services.AddScoped<Application.Users.IUserRepository, UserRepository>();
        services.AddScoped<PersonaWebhookSignatureVerifier>();
        services.AddHostedService<Auth.SessionCleanupBackgroundService>();
        services.AddHostedService<Auth.WelcomeEmailSweeperBackgroundService>();

        return services;
    }
}
