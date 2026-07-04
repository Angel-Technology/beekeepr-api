namespace BuzzKeepr.Domain.Enums;

// Controls whether a user appears in search results / public profile views in the Buzz Badge
// Community. Private users effectively disappear from discovery — they can still send and
// accept friend requests with users who already know their handle.
public enum ProfileVisibility
{
    Public,
    Private
}
