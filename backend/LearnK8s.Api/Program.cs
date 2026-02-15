using LearnK8s.Api.Contexts;
using LearnK8s.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .UseSnakeCaseNamingConvention();
});
builder.Services.AddOpenTelemetry()
    .WithTracing(p =>
    {
        p.AddNpgsql();
    });
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Seed the database with some initial data
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.Migrate();
    if (!context.Set<User>().Any())
    {
        for (var i = 1; i <= 10; i++)
        {
            context.Set<User>().Add(new User
            {
                Id = Guid.CreateVersion7(),
                Name = $"User {i}",
                Email = $"user_{i}@example.com"
            });
        }

        context.SaveChanges();
    }
}

app.MapGet("/api/v1/users", async ([FromServices] AppDbContext context) =>
    {
        var users = await context.Set<User>().ToListAsync();
        return users;
    })
    .WithName("GetUsers");

app.Run();