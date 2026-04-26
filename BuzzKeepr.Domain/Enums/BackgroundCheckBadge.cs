namespace BuzzKeepr.Domain.Enums;

/// <summary>
/// User-visible classification derived from the most recent Checkr Trust criminal check.
/// The classification logic itself lives in the Checkr ruleset (configured per environment via
/// <c>CheckrTrust:RulesetIds</c>) — the ruleset filters out records that don't disqualify
/// (e.g. minor traffic). Anything that survives the ruleset and shows up in the response is
/// treated as disqualifying, hence <see cref="Denied"/>. A clean response is <see cref="Approved"/>.
///
/// Badges have an associated <c>BackgroundCheckBadgeExpiresAtUtc</c> (currently 6 months out
/// from the check), but the backend does NOT auto-transition the badge to anything when it
/// passes — the frontend compares the timestamp to <c>now</c> to decide whether to show a
/// "verified" badge or prompt the user to renew (paid action, future feature). So this enum
/// only ever holds the immutable result of the last actual check.
/// </summary>
public enum BackgroundCheckBadge
{
    None = 0,
    Approved = 1,
    Denied = 2
}
