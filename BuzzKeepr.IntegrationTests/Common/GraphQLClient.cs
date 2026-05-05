using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuzzKeepr.IntegrationTests.Common;

public sealed class GraphQLClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HttpClient Http => httpClient;

    public async Task<GraphQLResponse<TData>> SendAsync<TData>(string query, object? variables = null, CancellationToken cancellationToken = default)
    {
        var response = await SendRawAsync(query, variables, cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GraphQL request failed ({(int)response.StatusCode}): {rawBody}");

        return JsonSerializer.Deserialize<GraphQLResponse<TData>>(rawBody, SerializerOptions)
               ?? throw new InvalidOperationException("Empty GraphQL response.");
    }

    public Task<HttpResponseMessage> SendRawAsync(string query, object? variables = null, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = JsonContent.Create(new { query, variables }, options: SerializerOptions)
        };
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:3000");
        return httpClient.SendAsync(request, cancellationToken);
    }
}

public sealed record GraphQLResponse<TData>(
    [property: JsonPropertyName("data")] TData? Data,
    [property: JsonPropertyName("errors")] List<GraphQLError>? Errors)
{
    public TData RequireData()
    {
        if (Errors is { Count: > 0 })
            throw new InvalidOperationException("GraphQL errors: " + string.Join("; ", Errors.Select(e => e.Message)));
        return Data ?? throw new InvalidOperationException("GraphQL response had no data.");
    }
}

public sealed record GraphQLError(
    [property: JsonPropertyName("message")] string Message);
