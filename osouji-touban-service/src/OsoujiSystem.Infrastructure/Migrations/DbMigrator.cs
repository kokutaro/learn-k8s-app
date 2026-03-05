using DbUp;
using DbUp.Engine;
using DbUp.Helpers;

namespace OsoujiSystem.Infrastructure.Migrations;

internal static class DbMigrator
{
    public static DatabaseUpgradeResult Migrate(string connectionString)
    {
        EnsureDatabase.For.SqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbMigrator).Assembly, script => script.Contains(".Migrations."))
            .LogToTrace()
            .Build();

        return upgrader.PerformUpgrade();
    }
}
