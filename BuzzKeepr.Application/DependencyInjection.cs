using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<Auth.IAuthService, Auth.AuthService>();
        services.AddScoped<Billing.IBillingService, Billing.BillingService>();
        services.AddScoped<IdentityVerification.IIdentityVerificationService, IdentityVerification.IdentityVerificationService>();
        services.AddScoped<Users.IUserService, Users.UserService>();

        return services;
    }
}
