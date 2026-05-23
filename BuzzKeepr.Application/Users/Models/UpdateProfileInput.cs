namespace BuzzKeepr.Application.Users.Models;

public sealed class UpdateProfileInput
{
    public string? Nickname { get; init; }

    public string? Handle { get; init; }
}
