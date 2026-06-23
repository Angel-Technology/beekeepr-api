namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class UnblockUserInput
{
    public Guid TargetUserId { get; init; }
}
