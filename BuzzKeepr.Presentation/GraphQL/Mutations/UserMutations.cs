using BuzzKeepr.API.Auth;
using BuzzKeepr.API.GraphQL.Inputs;
using BuzzKeepr.API.GraphQL.Types;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Users;
using ApplicationRequestEmailSignInInput = BuzzKeepr.Application.Auth.Models.RequestEmailSignInInput;
using ApplicationSignInWithGoogleInput = BuzzKeepr.Application.Auth.Models.SignInWithGoogleInput;
using ApplicationCreateUserInput = BuzzKeepr.Application.Users.Models.CreateUserInput;
using ApplicationVerifyEmailSignInInput = BuzzKeepr.Application.Auth.Models.VerifyEmailSignInInput;

namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class UserMutations
{
    public async Task<CreateUserPayload> CreateUserAsync(
        CreateUserInput input,
        [Service] IUserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.CreateAsync(new ApplicationCreateUserInput
        {
            Email = input.Email,
            DisplayName = input.DisplayName
        }, cancellationToken);

        if (result.EmailRequired)
        {
            return new CreateUserPayload
            {
                Error = "Email is required."
            };
        }

        if (result.EmailAlreadyExists)
        {
            return new CreateUserPayload
            {
                Error = "A user with that email already exists."
            };
        }

        if (!result.Success || result.User is null)
        {
            return new CreateUserPayload
            {
                Error = "User creation failed."
            };
        }

        return new CreateUserPayload
        {
            User = new UserGraph
            {
                Id = result.User.Id,
                Email = result.User.Email,
                DisplayName = result.User.DisplayName,
                EmailVerified = result.User.EmailVerified,
                CreatedAtUtc = result.User.CreatedAtUtc
            }
        };
    }

    public async Task<RequestEmailSignInPayload> RequestEmailSignInAsync(
        RequestEmailSignInInput input,
        [Service] IAuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.RequestEmailSignInAsync(new ApplicationRequestEmailSignInInput
        {
            Email = input.Email
        }, cancellationToken);

        if (result.EmailRequired)
        {
            return new RequestEmailSignInPayload
            {
                Error = "Email is required."
            };
        }

        if (result.EmailDeliveryFailed)
        {
            return new RequestEmailSignInPayload
            {
                Error = "Email delivery failed. Check Resend configuration and sender verification."
            };
        }

        return new RequestEmailSignInPayload
        {
            Success = result.Success,
            Email = result.Email,
            ExpiresAtUtc = result.ExpiresAtUtc
        };
    }

    public async Task<VerifyEmailSignInPayload> VerifyEmailSignInAsync(
        VerifyEmailSignInInput input,
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var result = await authService.VerifyEmailSignInAsync(new ApplicationVerifyEmailSignInInput
        {
            Email = input.Email,
            Code = input.Code
        }, cancellationToken);

        if (result.InvalidToken)
        {
            return new VerifyEmailSignInPayload
            {
                Error = "Invalid or expired token."
            };
        }

        if (!result.Success || result.User is null || result.SessionToken is null || !result.ExpiresAtUtc.HasValue)
        {
            return new VerifyEmailSignInPayload
            {
                Error = "Email sign-in failed."
            };
        }

        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for session cookie issuance.");

        SessionCookieManager.WriteSessionCookie(httpContext, result.SessionToken, result.ExpiresAtUtc.Value);

        return new VerifyEmailSignInPayload
        {
            User = new UserGraph
            {
                Id = result.User.Id,
                Email = result.User.Email,
                DisplayName = result.User.DisplayName,
                EmailVerified = result.User.EmailVerified,
                CreatedAtUtc = result.User.CreatedAtUtc
            }
        };
    }

    public async Task<SignInWithGooglePayload> SignInWithGoogleAsync(
        SignInWithGoogleInput input,
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var result = await authService.SignInWithGoogleAsync(new ApplicationSignInWithGoogleInput
        {
            Email = input.Email,
            ProviderAccountId = input.ProviderAccountId,
            DisplayName = input.DisplayName
        }, cancellationToken);

        if (result.InvalidInput)
        {
            return new SignInWithGooglePayload
            {
                Error = "Email and provider account ID are required."
            };
        }

        if (!result.Success || result.User is null || result.SessionToken is null || !result.ExpiresAtUtc.HasValue)
        {
            return new SignInWithGooglePayload
            {
                Error = "Google sign-in failed."
            };
        }

        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for session cookie issuance.");

        SessionCookieManager.WriteSessionCookie(httpContext, result.SessionToken, result.ExpiresAtUtc.Value);

        return new SignInWithGooglePayload
        {
            User = new UserGraph
            {
                Id = result.User.Id,
                Email = result.User.Email,
                DisplayName = result.User.DisplayName,
                EmailVerified = result.User.EmailVerified,
                CreatedAtUtc = result.User.CreatedAtUtc
            }
        };
    }

    public async Task<SignOutPayload> SignOutAsync(
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for sign out.");

        await authService.SignOutAsync(
            SessionCookieManager.ReadSessionCookie(httpContext),
            cancellationToken);

        SessionCookieManager.ClearSessionCookie(httpContext);

        return new SignOutPayload
        {
            Success = true
        };
    }
}
