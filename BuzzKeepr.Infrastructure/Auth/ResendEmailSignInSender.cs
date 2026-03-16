using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BuzzKeepr.Infrastructure.Auth;

public sealed class ResendEmailSignInSender(
    HttpClient httpClient,
    IOptions<EmailDeliveryOptions> emailOptions) : IEmailSignInSender
{
    public async Task SendSignInCodeAsync(string email, string code, DateTime expiresAtUtc, CancellationToken cancellationToken)
    {
        var options = emailOptions.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.ResendBaseUrl.TrimEnd('/')}/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ResendApiKey);

        var payload = new
        {
            from = options.FromEmail,
            to = new[] { email },
            subject = "Your BuzzKeepr sign-in code",
            html =
                $"<p>Your BuzzKeepr sign-in code is:</p><h1>{code}</h1><p>This code expires at {expiresAtUtc:u}.</p>",
            text =
                $"Your BuzzKeepr sign-in code is {code}. This code expires at {expiresAtUtc:u}."
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
