using BuzzKeepr.API.Auth;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Users;
using BuzzKeepr.API.GraphQL.Types;

namespace BuzzKeepr.API.GraphQL.Queries;

public sealed class UserQueries
{
    public async Task<UserGraph?> GetUserByIdAsync(Guid id,
        [Service] IUserService userService,
        CancellationToken cancellationToken)
    {
        var user = await userService.GetByIdAsync(id, cancellationToken);

        return user is null
            ? null
            : new UserGraph
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                EmailVerified = user.EmailVerified,
                IdentityVerificationStatus = user.IdentityVerificationStatus,
                PersonaInquiryId = user.PersonaInquiryId,
                PersonaInquiryStatus = user.PersonaInquiryStatus,
                TermsAcceptedAtUtc = user.TermsAcceptedAtUtc,
                CreatedAtUtc = user.CreatedAtUtc
            };
    }

    public async Task<UserGraph?> GetCurrentUserAsync(
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for session lookup.");

        var result = await SessionRefresher.ResolveAsync(httpContext, authService, cancellationToken);

        return result.User is null
            ? null
            : new UserGraph
            {
                Id = result.User.Id,
                Email = result.User.Email,
                DisplayName = result.User.DisplayName,
                EmailVerified = result.User.EmailVerified,
                IdentityVerificationStatus = result.User.IdentityVerificationStatus,
                PersonaInquiryId = result.User.PersonaInquiryId,
                PersonaInquiryStatus = result.User.PersonaInquiryStatus,
                TermsAcceptedAtUtc = result.User.TermsAcceptedAtUtc,
                CreatedAtUtc = result.User.CreatedAtUtc
            };
    }
}
