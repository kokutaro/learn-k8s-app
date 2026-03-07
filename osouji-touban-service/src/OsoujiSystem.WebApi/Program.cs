using OsoujiSystem.Application.DependencyInjection;
using OsoujiSystem.Infrastructure.DependencyInjection;
using OsoujiSystem.Infrastructure.Observability;
using OsoujiSystem.WebApi.Endpoints;
using OsoujiSystem.WebApi.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
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
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Osouji System API"));
}

app.UseHttpsRedirection();
app.UseMiddleware<HttpMetricsMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");
app.MapOsoujiApi();
app.MapPrometheusScrapingEndpoint();

app.Run();