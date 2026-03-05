using OsoujiSystem.Application.DependencyInjection;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.WebApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddOsoujiApplication();

// Placeholder repositories for bootstrapping the application layer wiring.
builder.Services.AddScoped<ICleaningAreaRepository, StubCleaningAreaRepository>();
builder.Services.AddScoped<IWeeklyDutyPlanRepository, StubWeeklyDutyPlanRepository>();
builder.Services.AddScoped<IAssignmentHistoryRepository, StubAssignmentHistoryRepository>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.Run();
