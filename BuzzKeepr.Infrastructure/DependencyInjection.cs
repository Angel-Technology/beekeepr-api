using BuzzKeepr.Infrastructure.Auth;
using BuzzKeepr.Infrastructure.Configuration;
using BuzzKeepr.Infrastructure.Persistence;
using BuzzKeepr.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        var emailOptions = configuration
            .GetSection(EmailDeliveryOptions.SectionName)
            .Get<EmailDeliveryOptions>() ?? new EmailDeliveryOptions();

        if (string.IsNullOrWhiteSpace(emailOptions.ResendApiKey))
        {
            throw new InvalidOperationException(
                "Email:ResendApiKey is required. Set it in user secrets or environment variables.");
        }

        services.AddDbContext<BuzzKeeprDbContext>(options =>
        {
            options.UseNpgsql(
                databaseOptions.ConnectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(BuzzKeeprDbContext).Assembly.FullName);
                });
        });

        services.AddHttpClient<ResendEmailSignInSender>();
        services.AddScoped<Application.Auth.IGoogleTokenVerifier, GoogleTokenVerifier>();
        services.AddScoped<Application.Auth.IEmailSignInSender, ResendEmailSignInSender>();
        services.AddScoped<Application.Auth.IAuthRepository, AuthRepository>();
        services.AddScoped<Application.Users.IUserRepository, UserRepository>();

        return services;
    }
}
