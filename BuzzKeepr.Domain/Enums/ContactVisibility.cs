namespace BuzzKeepr.Domain.Enums;

// Three-way control over who can see the user's contact information (phone numbers, social
// handles). Drives the "Share with everyone / Share with connections / Don't share" toggle on
// the profile page.
public enum ContactVisibility
{
    Public,
    ConnectionsOnly,
    Private
}
