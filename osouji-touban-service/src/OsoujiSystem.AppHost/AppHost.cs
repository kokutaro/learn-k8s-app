using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var pgUserName = builder.AddParameter("pgUserName", secret: true);
var pgPassword = builder.AddParameter("pgPassword", secret: true);

var postgres = builder.AddPostgres("osouji-postgres", pgUserName, pgPassword)
    .WithDataVolume("osouji-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);
var db = postgres.AddDatabase("osouji-db", "osouji");

var redis = builder.AddRedis("osouji-redis")
    .WithDataVolume("osouji-redis-data");
var rabbitMq = builder.AddRabbitMQ("osouji-rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume("osouji-rabbitmq-data");

var api = builder.AddProject<OsoujiSystem_WebApi>("OsoujiSystem-WebApi")
    .WithReference(db)
    .WaitFor(db)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitMq)
    .WaitFor(rabbitMq);

builder.AddViteApp("frontend", "../../osouji-system-frontend")
    .WithEndpoint("http", annotation =>
    {
        annotation.Port = 5173;
    })
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();