using OsoujiSystem.Application.DependencyInjection;
using OsoujiSystem.Infrastructure.DependencyInjection;
using OsoujiSystem.Infrastructure.Observability;
using OsoujiSystem.WebApi.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddOsoujiApplication();
builder.Services.AddOsoujiInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddTransient<HttpMetricsMiddleware>();

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("osouji-system-webapi"))
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(OsoujiTelemetry.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter();
        }
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(OsoujiTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter();
        }
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseMiddleware<HttpMetricsMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");
app.MapPrometheusScrapingEndpoint();

app.Run();
