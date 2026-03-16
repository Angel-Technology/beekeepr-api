using BuzzKeepr.Application.Auth.Models;

namespace BuzzKeepr.Application.Auth;

public interface IAuthService
{
    Task<CurrentUserResult> GetCurrentUserAsync(string? sessionToken, CancellationToken cancellationToken);

    Task<SignOutResult> SignOutAsync(string? sessionToken, CancellationToken cancellationToken);

    Task<RequestEmailSignInResult> RequestEmailSignInAsync(RequestEmailSignInInput input,
        CancellationToken cancellationToken);

    Task<VerifyEmailSignInResult> VerifyEmailSignInAsync(VerifyEmailSignInInput input,
        CancellationToken cancellationToken);

    Task<SignInWithGoogleResult>
        SignInWithGoogleAsync(SignInWithGoogleInput input, CancellationToken cancellationToken);
}