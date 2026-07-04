namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class RemoveFriendInput
{
    public Guid OtherUserId { get; init; }
}
