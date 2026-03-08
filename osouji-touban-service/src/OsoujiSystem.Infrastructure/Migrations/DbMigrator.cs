using DbUp;
using DbUp.Engine;

namespace OsoujiSystem.Infrastructure.Migrations;

internal static class DbMigrator
{
    public static DatabaseUpgradeResult Migrate(string connectionString)
    {
        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbMigrator).Assembly, script => script.Contains(".Migrations."))
            .LogToTrace()
            .Build();

        return upgrader.PerformUpgrade();
    }
}
