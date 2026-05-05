using BuzzKeepr.Application.Users.Models;

namespace BuzzKeepr.Application.Auth.Models;

public sealed class CurrentUserResult
{
    public UserDto? User { get; init; }

    public DateTime? RefreshedSessionExpiresAtUtc { get; init; }
}
