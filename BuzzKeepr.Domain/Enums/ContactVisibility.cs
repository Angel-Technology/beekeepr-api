namespace BuzzKeepr.Domain.Enums;

// Two-way control over who can see the user's contact information (phone numbers, social
// handles). Drives the "Share with connections / Don't share" toggle on the profile page.
// `Public` was removed in PR 3 — we no longer let users broadcast contact info to strangers;
// the only opt-in path is "friends only."
public enum ContactVisibility
{
    ConnectionsOnly,
    Private
}
