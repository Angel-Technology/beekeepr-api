using Microsoft.Extensions.Hosting;

namespace BuzzKeepr.API.Auth;

public sealed class CsrfOriginAllowlist(IHostEnvironment hostEnvironment, string[] allowedOrigins)
{
    private readonly bool isDevelopment = hostEnvironment.IsDevelopment();
    private readonly HashSet<string> allowedOrigins = new(
        allowedOrigins.Select(NormalizeOrigin).Where(value => !string.IsNullOrEmpty(value))!,
        StringComparer.OrdinalIgnoreCase);

    public bool IsAllowed(string? originOrReferer)
    {
        var normalized = NormalizeOrigin(originOrReferer);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (allowedOrigins.Contains(normalized))
            return true;

        if (isDevelopment && Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return uri.Host is "localhost" or "127.0.0.1";

        return false;
    }

    private static string? NormalizeOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return null;

        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{uri.Scheme}://{uri.Host}{port}";
    }
}
