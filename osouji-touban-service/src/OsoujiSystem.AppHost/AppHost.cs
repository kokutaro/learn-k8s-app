using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var pgUserName = builder.AddParameter("pgUserName", secret: true);
var pgPassword = builder.AddParameter("pgPassword", secret: true);
var postgres = builder.AddPostgres("osouji-postgres", pgUserName, pgPassword);
var db = postgres.AddDatabase("osouji-db", "osouji");

var redis = builder.AddRedis("osouji-redis");
var rabbitMq = builder.AddRabbitMQ("osouji-rabbitmq");

builder.AddProject<OsoujiSystem_WebApi>("OsoujiSystem-WebApi")
    .WithReference(db)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["INFRASTRUCTURE__POSTGRES__CONNECTIONSTRING"] =
            db.Resource.ConnectionStringExpression;
    })
    .WaitFor(db)
    .WithReference(redis)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["INFRASTRUCTURE__REDIS__CONNECTIONSTRING"] =
            redis.Resource.ConnectionStringExpression;
    })
    .WaitFor(redis)
    .WithReference(rabbitMq)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["INFRASTRUCTURE__RABBITMQ__HOST"] =
            rabbitMq.Resource.Host;
        context.EnvironmentVariables["INFRASTRUCTURE__RABBITMQ__USERNAME"] = "guest";
        context.EnvironmentVariables["INFRASTRUCTURE__RABBITMQ__PASSWORD"] =
            rabbitMq.Resource.PasswordParameter;
        context.EnvironmentVariables["INFRASTRUCTURE__RABBITMQ__VIRTUALHOST"] = "/";
        context.EnvironmentVariables["INFRASTRUCTURE__RABBITMQ__PORT"] =
            rabbitMq.Resource.Port;
        context.EnvironmentVariables["INFRASTRUCTURE__RABBITMQ__USETLS"] = "false";
    })
    .WaitFor(rabbitMq);

builder.Build().Run();