using BuzzKeepr.Infrastructure.Auth;
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
            return raw;
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

        return builder.ConnectionString;
    }
}
