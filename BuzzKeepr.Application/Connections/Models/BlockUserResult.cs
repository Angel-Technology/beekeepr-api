namespace BuzzKeepr.Application.Connections.Models;

public sealed class BlockUserResult
{
    public bool SelfTarget { get; init; }

    public bool TargetNotFound { get; init; }

    public bool Success { get; init; }
}
