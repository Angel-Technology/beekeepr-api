namespace BuzzKeepr.Application.Users.Models;

// Email-only at signup. The user fills in everything else (DisplayName, Nickname, Handle,
// contact info) through the post-signup profile-completion flow — App Store reviewers flag
// apps that auto-populate user-facing profile fields from third-party identity providers.
public sealed class CreateUserInput
{
    public string Email { get; init; } = string.Empty;
}
