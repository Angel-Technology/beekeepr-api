namespace BuzzKeepr.Application.Users;

public interface IWelcomeEmailSender
{
    Task SendWelcomeAsync(string email, string? displayName, CancellationToken cancellationToken);
}
