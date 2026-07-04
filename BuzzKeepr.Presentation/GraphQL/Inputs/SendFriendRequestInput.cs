namespace BuzzKeepr.API.GraphQL.Inputs;

public sealed class SendFriendRequestInput
{
    public Guid TargetUserId { get; init; }
}
