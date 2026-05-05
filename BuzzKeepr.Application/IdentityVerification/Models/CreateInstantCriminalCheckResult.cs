namespace BuzzKeepr.Application.IdentityVerification.Models;

public sealed class CreateInstantCriminalCheckResult
{
    public bool Success { get; init; }

    public string? CheckId { get; init; }

    public string? ProfileId { get; init; }

    public int? ResultCount { get; init; }

    public bool? HasPossibleMatches { get; init; }

    public string? Error { get; init; }
}
