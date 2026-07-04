namespace BuzzKeepr.Domain.Enums;

// The friendship relationship between a search result and the viewer doing the searching.
// Used to drive which action button the frontend renders (Add / Pending / Accept / Friends).
public enum ViewerFriendshipState
{
    None,
    RequestSent,
    RequestReceived,
    Friends
}
