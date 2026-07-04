namespace BuzzKeepr.API.GraphQL.Inputs;

// Shared input for accept / decline / cancel — `OtherUserId` is the requester for accept/decline
// and the addressee for cancel.
public sealed class RespondToFriendRequestInput
{
    public Guid OtherUserId { get; init; }
}
