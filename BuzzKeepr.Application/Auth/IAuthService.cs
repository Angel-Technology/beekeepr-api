using BuzzKeepr.Application.Auth.Models;

namespace BuzzKeepr.Application.Auth;

public interface IAuthService
{
    Task<RequestEmailSignInResult> RequestEmailSignInAsync(RequestEmailSignInInput input,
        CancellationToken cancellationToken);

    Task<VerifyEmailSignInResult> VerifyEmailSignInAsync(VerifyEmailSignInInput input,
        CancellationToken cancellationToken);

    Task<SignInWithGoogleResult>
        SignInWithGoogleAsync(SignInWithGoogleInput input, CancellationToken cancellationToken);
}