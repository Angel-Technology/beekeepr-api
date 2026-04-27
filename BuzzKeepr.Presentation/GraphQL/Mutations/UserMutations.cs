using BuzzKeepr.API.Auth;
using BuzzKeepr.API.GraphQL.Types;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Application.Users;
using BuzzKeepr.API.GraphQL.Inputs;
using ApplicationRequestEmailSignInInput = BuzzKeepr.Application.Auth.Models.RequestEmailSignInInput;
using ApplicationSignInWithGoogleInput = BuzzKeepr.Application.Auth.Models.SignInWithGoogleInput;
using ApplicationCreateUserInput = BuzzKeepr.Application.Users.Models.CreateUserInput;
using ApplicationVerifyEmailSignInInput = BuzzKeepr.Application.Auth.Models.VerifyEmailSignInInput;

namespace BuzzKeepr.API.GraphQL.Mutations;

public sealed class UserMutations
{
    private static string? ResolveClientIpAddress(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var first = forwarded.ToString().Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        return httpContext.Connection.RemoteIpAddress?.ToString();
    }


    public async Task<CreateUserPayload> CreateUserAsync(
        CreateUserInput input,
        [Service] IUserService userService,
        [Service] AppApiKeyValidator appApiKeyValidator,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for createUser.");

        if (!appApiKeyValidator.IsValid(httpContext))
        {
            return new CreateUserPayload
            {
                Error = "Forbidden: missing or invalid X-App-Api-Key header."
            };
        }

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
            User = UserGraph.From(result.User)
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
        var httpContextEarly = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for sign-in.");

        var result = await authService.VerifyEmailSignInAsync(new ApplicationVerifyEmailSignInInput
        {
            Email = input.Email,
            Code = input.Code,
            IpAddress = ResolveClientIpAddress(httpContextEarly),
            UserAgent = httpContextEarly.Request.Headers.UserAgent.ToString()
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

        SessionCookieManager.WriteSessionCookie(httpContextEarly, result.SessionToken, result.ExpiresAtUtc.Value);

        return new VerifyEmailSignInPayload
        {
            User = UserGraph.From(result.User),
            Session = new AuthSessionGraph
            {
                Token = result.SessionToken,
                ExpiresAtUtc = result.ExpiresAtUtc.Value
            }
        };
    }

    public async Task<SignInWithGooglePayload> SignInWithGoogleAsync(
        SignInWithGoogleInput input,
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContextEarly = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for sign-in.");

        var result = await authService.SignInWithGoogleAsync(new ApplicationSignInWithGoogleInput
        {
            IdToken = input.IdToken,
            IpAddress = ResolveClientIpAddress(httpContextEarly),
            UserAgent = httpContextEarly.Request.Headers.UserAgent.ToString()
        }, cancellationToken);

        if (result.InvalidInput)
        {
            return new SignInWithGooglePayload
            {
                Error = "Google ID token is required."
            };
        }

        if (result.InvalidToken)
        {
            return new SignInWithGooglePayload
            {
                Error = "Invalid Google ID token."
            };
        }

        if (!result.Success || result.User is null || result.SessionToken is null || !result.ExpiresAtUtc.HasValue)
        {
            return new SignInWithGooglePayload
            {
                Error = "Google sign-in failed."
            };
        }

        SessionCookieManager.WriteSessionCookie(httpContextEarly, result.SessionToken, result.ExpiresAtUtc.Value);

        return new SignInWithGooglePayload
        {
            User = UserGraph.From(result.User),
            Session = new AuthSessionGraph
            {
                Token = result.SessionToken,
                ExpiresAtUtc = result.ExpiresAtUtc.Value
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
            SessionTokenResolver.Resolve(httpContext),
            cancellationToken);

        SessionCookieManager.ClearSessionCookie(httpContext);

        return new SignOutPayload
        {
            Success = true
        };
    }

    public async Task<AcceptTermsPayload> AcceptTermsAsync(
        [Service] IAuthService authService,
        [Service] IUserService userService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for terms acceptance.");

        var currentUser = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);

        if (currentUser.User is null)
        {
            return new AcceptTermsPayload
            {
                Error = "Authentication is required."
            };
        }

        var result = await userService.AcceptTermsAsync(
            currentUser.User.Id,
            cancellationToken);

        if (result.UserNotFound || !result.Success || result.User is null)
        {
            return new AcceptTermsPayload
            {
                Error = "Unable to accept terms."
            };
        }

        return new AcceptTermsPayload
        {
            User = UserGraph.From(result.User)
        };
    }

    public async Task<StartPersonaInquiryPayload> StartPersonaInquiryAsync(
        [Service] IAuthService authService,
        [Service] IIdentityVerificationService identityVerificationService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for Persona inquiry creation.");

        var currentUser = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);

        if (currentUser.User is null)
        {
            return new StartPersonaInquiryPayload
            {
                Error = "Authentication is required."
            };
        }

        var result = await identityVerificationService.StartPersonaInquiryAsync(
            currentUser.User.Id,
            cancellationToken);

        return new StartPersonaInquiryPayload
        {
            Success = result.Success,
            CreatedNewInquiry = result.CreatedNewInquiry,
            InquiryId = result.InquiryId,
            IdentityVerificationStatus = result.IdentityVerificationStatus,
            PersonaInquiryStatus = result.PersonaInquiryStatus,
            SubscriptionRequired = result.SubscriptionRequired,
            Error = result.Error
        };
    }

    public async Task<StartInstantCriminalCheckPayload> StartInstantCriminalCheckAsync(
        StartInstantCriminalCheckInput input,
        [Service] IAuthService authService,
        [Service] IIdentityVerificationService identityVerificationService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for instant criminal checks.");

        var currentUser = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);

        if (currentUser.User is null)
        {
            return new StartInstantCriminalCheckPayload
            {
                Error = "Authentication is required."
            };
        }

        var result = await identityVerificationService.CreateInstantCriminalCheckAsync(
            currentUser.User.Id,
            new Application.IdentityVerification.Models.StartInstantCriminalCheckInput
            {
                PhoneNumber = input.PhoneNumber
            },
            cancellationToken);

        return new StartInstantCriminalCheckPayload
        {
            Success = result.Success,
            CheckId = result.CheckId,
            ProfileId = result.ProfileId,
            ResultCount = result.ResultCount,
            HasPossibleMatches = result.HasPossibleMatches,
            Error = result.Error
        };
    }
}
