using BuzzKeepr.API.Auth;
using BuzzKeepr.API.GraphQL.Mutations;
using BuzzKeepr.API.GraphQL.Queries;
using BuzzKeepr.Application;
using BuzzKeepr.Application.IdentityVerification;
using BuzzKeepr.Infrastructure;
using BuzzKeepr.Infrastructure.IdentityVerification;
using BuzzKeepr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT");
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var isDevelopment = builder.Environment.IsDevelopment();

// Sentry: stays a no-op when Sentry:Dsn is blank (the dev default), so this can run in any env.
// Sentry's SDK throws ArgumentNullException on null Dsn but treats empty string as "disabled".
builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["Sentry:Dsn"] ?? string.Empty;
    // Env tag: explicit Sentry:Environment wins (so the develop Render service can tag events as
    // "develop" while still running ASPNETCORE_ENVIRONMENT=Production). Falls back to .NET's env
    // name when nothing is configured. Always lowercased so "Develop"/"develop" don't both show
    // up as separate environments in Sentry's filter.
    options.Environment = (builder.Configuration["Sentry:Environment"] ?? builder.Environment.EnvironmentName)
        .ToLowerInvariant();
    options.Release = typeof(Program).Assembly.GetName().Version?.ToString();
    options.Debug = isDevelopment; // print SDK chatter to console in dev so we can see it phoning home
    options.SendDefaultPii = false;
    options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None; // GraphQL bodies contain emails / verification codes
    options.TracesSampleRate = isDevelopment ? 1.0 : 0.1;
    options.AttachStacktrace = true;
});

if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Layer registrations
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(serviceProvider => new CsrfOriginAllowlist(
    serviceProvider.GetRequiredService<IHostEnvironment>(),
    allowedOrigins));
builder.Services.AddSingleton<AppApiKeyValidator>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (isDevelopment)
        {
            policy
                .SetIsOriginAllowed(origin =>
                {
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                        return false;

                    return uri.Host is "localhost" or "127.0.0.1";
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();

            return;
        }

        if (allowedOrigins.Length == 0)
            return;

        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services
    .AddGraphQLServer()
    .ModifyRequestOptions(options =>
    {
        options.IncludeExceptionDetails = isDevelopment;
    })
    .AddDiagnosticEventListener<BuzzKeepr.API.GraphQL.SentryGraphQLDiagnosticListener>()
    .AddQueryType<UserQueries>()
    .AddMutationType<UserMutations>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BuzzKeepr.API",
        Version = "v1",
        Description = "BuzzKeepr REST API"
    });
});
builder.Services.AddHealthChecks();

var app = builder.Build();
var applyMigrationsOnStartup = app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");

if (applyMigrationsOnStartup)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BuzzKeeprDbContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "BuzzKeepr.API";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
    });
}

app.UseCors("Frontend");
app.UseMiddleware<CsrfProtectionMiddleware>();

app.MapGet("/", () =>
    {
        return Results.Ok(new
        {
            Name = "BuzzKeepr.API",
            Version = "v1",
            Environment = app.Environment.EnvironmentName,
            OpenApi = app.Environment.IsDevelopment() ? "/swagger/v1/swagger.json" : null,
            Swagger = app.Environment.IsDevelopment() ? "/swagger" : null,
            GraphQL = "/graphql",
            Health = "/health"
        });
    })
    .WithName("GetRoot");

app.MapHealthChecks("/health");
app.MapGraphQL();
app.MapPost("/webhooks/persona", async (
    HttpContext httpContext,
    PersonaWebhookSignatureVerifier signatureVerifier,
    IIdentityVerificationService identityVerificationService,
    CancellationToken cancellationToken) =>
{
    httpContext.Request.EnableBuffering();

    using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
    var rawRequestBody = await reader.ReadToEndAsync(cancellationToken);
    httpContext.Request.Body.Position = 0;

    var signatureHeader = httpContext.Request.Headers["Persona-Signature"].ToString();

    if (!signatureVerifier.IsValid(signatureHeader, rawRequestBody))
        return Results.Unauthorized();

    await identityVerificationService.ProcessPersonaWebhookAsync(rawRequestBody, cancellationToken);

    return Results.NoContent();
});

app.Run();

public partial class Program;
