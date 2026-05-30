using EmailBroker.Api.Endpoints;
using EmailBroker.Providers;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// Email providers
builder.Services.AddEmailProviders(builder.Configuration);

// Validation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Middleware
app.UseSwagger();
app.UseSwaggerUI();

// Endpoints
app.MapEmailEndpoints();
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        };
        await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, response);
    }
});

app.Run();

// Make Program public for WebApplicationFactory in integration tests
public partial class Program { }
