using BuzzKeepr.API.Auth;
using BuzzKeepr.API.GraphQL.Inputs;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Notifications;
using HotChocolate.Types;
using ApplicationRegisterPushTokenInput = BuzzKeepr.Application.Notifications.Models.RegisterPushTokenInput;

namespace BuzzKeepr.API.GraphQL.Mutations;

// Push-notification token management. The frontend calls these on app launch (register) and
// sign-out (unregister). Composed onto the root Mutation type via [ExtendObjectType] +
// AddTypeExtension<>() in Program.cs — same pattern as ConnectionsMutations.
[ExtendObjectType(typeof(UserMutations))]
public sealed class NotificationMutations
{
    public async Task<RegisterPushTokenPayload> RegisterPushTokenAsync(
        RegisterPushTokenInput input,
        [Service] IAuthService authService,
        [Service] IPushTokenService pushTokenService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for registerPushToken.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return new RegisterPushTokenPayload { Error = "Authentication is required." };

        var result = await pushTokenService.RegisterAsync(
            current.User.Id,
            new ApplicationRegisterPushTokenInput
            {
                Token = input.Token,
                Platform = input.Platform
            },
            cancellationToken);

        if (result.TokenRequired)
            return new RegisterPushTokenPayload { Error = "Push token is required." };

        return new RegisterPushTokenPayload { Success = result.Success };
    }

    public async Task<UnregisterPushTokenPayload> UnregisterPushTokenAsync(
        UnregisterPushTokenInput input,
        [Service] IAuthService authService,
        [Service] IPushTokenService pushTokenService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for unregisterPushToken.");

        var current = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);
        if (current.User is null)
            return new UnregisterPushTokenPayload { Error = "Authentication is required." };

        var result = await pushTokenService.UnregisterAsync(current.User.Id, input.Token, cancellationToken);

        if (result.TokenRequired)
            return new UnregisterPushTokenPayload { Error = "Push token is required." };

        return new UnregisterPushTokenPayload { Success = result.Success };
    }
}
