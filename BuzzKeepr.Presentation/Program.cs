var builder = WebApplication.CreateBuilder(args);

// Register services the app needs before the HTTP pipeline is built.
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Development-only tooling lives behind the environment check.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// A simple root endpoint makes it obvious the API is alive.
app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        Name = "BuzzKeepr API",
        Environment = app.Environment.EnvironmentName,
        OpenApi = app.Environment.IsDevelopment() ? "/openapi/v1.json" : (string?)null,
        Health = "/health"
    });
})
.WithName("GetRoot");

// Health checks are a standard readiness endpoint for APIs and deployments.
app.MapHealthChecks("/health");

app.Run();
