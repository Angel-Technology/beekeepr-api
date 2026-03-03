using Microsoft.Extensions.DependencyInjection;

namespace BuzzKeepr.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<Users.IUserService, Users.UserService>();

        return services;
    }
}
