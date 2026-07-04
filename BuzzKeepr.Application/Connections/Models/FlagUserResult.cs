namespace BuzzKeepr.Application.Connections.Models;

public sealed class FlagUserResult
{
    public bool SelfTarget { get; init; }

    public bool TargetNotFound { get; init; }

    public bool Success { get; init; }
}
