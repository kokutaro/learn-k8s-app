using OsoujiSystem.Application.DependencyInjection;
using OsoujiSystem.Application.Observability;
using OsoujiSystem.Infrastructure.DependencyInjection;
using OsoujiSystem.Infrastructure.Observability;
using OsoujiSystem.WebApi.Endpoints;
using OsoujiSystem.WebApi.Endpoints.Support;
using OsoujiSystem.WebApi.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Scalar.AspNetCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddOsoujiApplication();
builder.Services.AddOsoujiInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddTransient<HttpMetricsMiddleware>();
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("osouji-system-webapi"))
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(OsoujiTelemetry.MeterName)
            .AddPrometheusExporter();
    })
    .WithTracing(tracing =>
    {
        tracing.AddSource(OsoujiTelemetry.ActivitySourceName)
            .AddSource(ApplicationTelemetry.ActivitySourceName);
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Osouji System API"));
}

app.UseHttpsRedirection();
app.UseMiddleware<HttpMetricsMiddleware>();

app.MapGet("/health", () => TypedResults.Ok(new ApiResponse<HealthResponse>(new HealthResponse("ok"))))
    .WithName("Health")
    .Produces<ApiResponse<HealthResponse>>();
app.MapOsoujiApi()
    .MapPrometheusScrapingEndpoint();

app.Run();

public sealed record HealthResponse(string Status);