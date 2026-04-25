using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuzzKeepr.Application.Users;
using BuzzKeepr.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure.Auth;

public sealed class ResendWelcomeEmailSender(
    HttpClient httpClient,
    IOptions<EmailDeliveryOptions> emailOptions) : IWelcomeEmailSender
{
    public async Task SendWelcomeAsync(string email, string? displayName, CancellationToken cancellationToken)
    {
        var options = emailOptions.Value;

        if (string.IsNullOrWhiteSpace(options.WelcomeTemplateId))
            throw new InvalidOperationException("Email:WelcomeTemplateId is not configured.");

        var firstName = ExtractFirstName(displayName);

        var payload = new
        {
            to = new[] { email },
            template = new
            {
                id = options.WelcomeTemplateId,
                variables = new
                {
                    firstname = firstName,
                    email
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.ResendBaseUrl.TrimEnd('/')}/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ResendApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Resend welcome email failed with status {(int)response.StatusCode}: {body}");
    }

    private static string ExtractFirstName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "there";

        var first = displayName.Trim().Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrEmpty(first) ? "there" : first;
    }
}
