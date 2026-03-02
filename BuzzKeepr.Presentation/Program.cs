using BuzzKeepr.Application;
using BuzzKeepr.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Layer registrations
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () =>
    {
        return Results.Ok(new
        {
            Name = "BuzzKeepr API",
            Version = "v1",
            Environment = app.Environment.EnvironmentName,
            OpenApi = app.Environment.IsDevelopment() ? "/openapi/v1.json" : null,
            Swagger = app.Environment.IsDevelopment() ? "/swagger" : null,
            Health = "/health"
        });
    })
    .WithName("GetRoot");

app.MapHealthChecks("/health");

app.Run();
