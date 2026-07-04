namespace BuzzKeepr.Application.IdentityVerification.Models;

public sealed class StartInstantCriminalCheckInput
{
    public string? PhoneNumber { get; init; }

    // Two-letter US state code where the caller currently resides. Only consulted on the
    // first Checkr call for a user (renewals reuse ProfileId). Frontend prompts for this
    // when the Persona inquiry didn't yield one — passport-verified users have no license
    // state on file, so Checkr would otherwise miss the residency signal.
    public string? LicenseState { get; init; }
}
