namespace BuzzKeepr.Application.Users.Models;

public sealed class HandleAvailabilityResult
{
    public bool Available { get; init; }

    // Stable snake_case code so clients can switch on it; null when Available is true.
    // Known values: too_short, too_long, invalid_format, taken, authentication_required.
    public string? Reason { get; init; }

    public static HandleAvailabilityResult Ok() => new() { Available = true };

    public static HandleAvailabilityResult Unavailable(string reason) => new()
    {
        Available = false,
        Reason = reason,
    };
}

public static class HandleAvailabilityReasons
{
    public const string TooShort = "too_short";
    public const string TooLong = "too_long";
    public const string InvalidFormat = "invalid_format";
    public const string Taken = "taken";
    public const string AuthenticationRequired = "authentication_required";
}
