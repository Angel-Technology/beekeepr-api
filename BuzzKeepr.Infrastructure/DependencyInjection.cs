using BuzzKeepr.Application.Auth;
using BuzzKeepr.Infrastructure.Auth;
using BuzzKeepr.Infrastructure.Billing;
using BuzzKeepr.Infrastructure.Configuration;
using BuzzKeepr.Infrastructure.IdentityVerification;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

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
        services.Configure<AuthOptions>(
            configuration.GetSection(AuthOptions.SectionName));
        services.Configure<GoogleAuthOptions>(
            configuration.GetSection(GoogleAuthOptions.SectionName));
        services.Configure<AppleAuthOptions>(
            configuration.GetSection(AppleAuthOptions.SectionName));
        services.Configure<PersonaOptions>(
            configuration.GetSection(PersonaOptions.SectionName));
        services.Configure<CheckrTrustOptions>(
            configuration.GetSection(CheckrTrustOptions.SectionName));
        services.Configure<RevenueCatOptions>(
            configuration.GetSection(RevenueCatOptions.SectionName));
        services.Configure<ApnsOptions>(
            configuration.GetSection(ApnsOptions.SectionName));
        services.Configure<FcmOptions>(
            configuration.GetSection(FcmOptions.SectionName));

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
                NormalizePostgresConnectionString(resolvedDatabaseOptions.ConnectionString),
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
        services.AddHttpClient<RevenueCatClient>((serviceProvider, httpClient) =>
        {
            var revenueCatOptions = serviceProvider
                .GetRequiredService<IOptions<RevenueCatOptions>>()
                .Value;

            if (Uri.TryCreate(revenueCatOptions.ApiBaseUrl, UriKind.Absolute, out var baseUri))
                httpClient.BaseAddress = baseUri;
        });
        services.AddScoped<Application.Auth.IGoogleTokenVerifier, GoogleTokenVerifier>();
        services.AddSingleton<Application.Auth.IAppleTokenVerifier, AppleTokenVerifier>();
        services.AddScoped<Application.Auth.IEmailSignInSender, ResendEmailSignInSender>();
        services.AddScoped<Application.Users.IWelcomeEmailSender, ResendWelcomeEmailSender>();
        services.AddScoped<Application.Auth.IAuthRepository, AuthRepository>();
        services.AddScoped<Application.IdentityVerification.IIdentityVerificationRepository, IdentityVerificationRepository>();
        services.AddScoped<Application.IdentityVerification.ICheckrTrustClient, CheckrTrustClient>();
        services.AddScoped<Application.IdentityVerification.IPersonaClient, PersonaClient>();
        services.AddScoped<Application.Users.IUserRepository, UserRepository>();
        services.AddScoped<Application.Billing.IBillingRepository, BillingRepository>();
        services.AddScoped<Application.Billing.IPromoCodeRepository, PromoCodeRepository>();
        services.AddScoped<Application.Billing.IRevenueCatClient, RevenueCatClient>();
        services.AddScoped<Application.Connections.IConnectionsRepository, ConnectionsRepository>();
        services.AddScoped<Application.Notifications.IPushTokenRepository, PushTokenRepository>();
        services.AddScoped<Application.Notifications.INotificationProfileLookup, NotificationProfileLookup>();
        services.AddHttpClient<Application.Notifications.IApnsPushClient, Notifications.ApnsPushClient>((serviceProvider, httpClient) =>
        {
            // Production vs sandbox switch — issued device tokens only work against one of them.
            // Dev/TestFlight builds need sandbox; App Store builds need production. Mixing
            // gets BadDeviceToken / Unregistered receipts.
            var apnsOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ApnsOptions>>()
                .Value;

            httpClient.BaseAddress = new Uri(apnsOptions.UseSandbox
                ? "https://api.sandbox.push.apple.com"
                : "https://api.push.apple.com");
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            httpClient.DefaultRequestVersion = System.Net.HttpVersion.Version20;
            httpClient.DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact;
        });
        // FCM client is a singleton — FirebaseApp is process-wide and initialization parses the
        // service-account JSON. Scoped lifetime would recreate the SDK on every request.
        services.AddSingleton<Application.Notifications.IFcmPushClient, Notifications.FcmPushClient>();
        services.AddScoped<PersonaWebhookSignatureVerifier>();
        services.AddScoped<RevenueCatWebhookAuthorizer>();
        services.AddHostedService<Auth.SessionCleanupBackgroundService>();
        services.AddHostedService<Auth.VerificationTokenCleanupBackgroundService>();
        services.AddHostedService<IdentityVerification.BackgroundCheckRenewalBackgroundService>();
        services.AddHostedService<Users.AccountDeletionPurgeBackgroundService>();

        return services;
    }

    /// <summary>
    /// Neon, Render, Heroku and friends typically expose Postgres credentials as a URI
    /// (<c>postgresql://user:pass@host:5432/db?sslmode=require</c>). Npgsql's parser only
    /// accepts the keyword=value form, so this helper detects URI input and converts it.
    /// Inputs already in keyword form are returned unchanged.
    /// </summary>
    private static string NormalizePostgresConnectionString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            // Already keyword=value form — still apply cold-start friendly timeouts if the
            // operator hasn't set them explicitly.
            var keywordBuilder = new NpgsqlConnectionStringBuilder(trimmed);
            ApplyNeonColdStartDefaults(keywordBuilder);
            return keywordBuilder.ConnectionString;
        }

        var uri = new Uri(trimmed);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : null,
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null
        };

        // Map any URI query params onto the builder. Npgsql is forgiving about keyword
        // aliases (sslmode, channel_binding, etc.). Skip anything it doesn't recognize
        // rather than failing the whole startup.
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = Uri.UnescapeDataString(parts[0]).Trim();
            var value = Uri.UnescapeDataString(parts[1]).Trim();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;

            try { builder[key] = value; } catch { /* unknown keyword — ignore */ }
        }

        ApplyNeonColdStartDefaults(builder);
        return builder.ConnectionString;
    }

    private static void ApplyNeonColdStartDefaults(NpgsqlConnectionStringBuilder builder)
    {
        // Npgsql defaults: Timeout=15s (connect), CommandTimeout=30s. Neon compute cold starts
        // + pooler routing can push connect past 15s under load. Only widen when the operator
        // hasn't explicitly opted into a value.
        if (!builder.ContainsKey("Timeout"))
            builder.Timeout = 30;
        if (!builder.ContainsKey("Command Timeout"))
            builder.CommandTimeout = 60;
    }
}
