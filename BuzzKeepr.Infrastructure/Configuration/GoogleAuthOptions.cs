namespace BuzzKeepr.Infrastructure.Configuration;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "Google";

    public string[] ClientIds { get; init; } = [];
}
