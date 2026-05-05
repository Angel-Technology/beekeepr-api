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

        if (string.IsNullOrWhiteSpace(options.SignInTemplateId))
            throw new InvalidOperationException("Email:SignInTemplateId is not configured.");

        var expiresInMinutes = Math.Max(1, (int)Math.Round((expiresAtUtc - DateTime.UtcNow).TotalMinutes));

        var payload = new
        {
            to = new[] { email },
            template = new
            {
                id = options.SignInTemplateId,
                variables = new
                {
                    code,
                    expires_in_minutes = expiresInMinutes,
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
            $"Resend sign-in email failed with status {(int)response.StatusCode}: {body}");
    }
}
