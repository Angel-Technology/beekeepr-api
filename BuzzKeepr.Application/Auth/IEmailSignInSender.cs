namespace BuzzKeepr.Application.Auth;

public interface IEmailSignInSender
{
    Task SendSignInCodeAsync(string email, string code, DateTime expiresAtUtc, CancellationToken cancellationToken);
}
