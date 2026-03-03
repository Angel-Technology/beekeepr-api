using BuzzKeepr.API.GraphQL.Mutations;
using BuzzKeepr.API.GraphQL.Queries;
using BuzzKeepr.Application;
using BuzzKeepr.Infrastructure;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Layer registrations
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddGraphQLServer()
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "BuzzKeepr.API";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
    });
}

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

app.Run();
