using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WindowsScriptRunner.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory :
    IDesignTimeDbContextFactory<WindowsScriptRunnerDbContext>
{
    public WindowsScriptRunnerDbContext CreateDbContext(string[] args)
    {
        const string localDesignTimeConnection =
            "Server=(localdb)\\MSSQLLocalDB;Database=WindowsScriptRunner_DesignTime;" +
            "Integrated Security=true;Encrypt=false";
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__WindowsScriptRunner") ??
            localDesignTimeConnection;
        var options = new DbContextOptionsBuilder<WindowsScriptRunnerDbContext>()
            .UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", "wsr");
                    sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                    sql.CommandTimeout(30);
                })
            .EnableDetailedErrors(false)
            .EnableSensitiveDataLogging(false)
            .Options;

        return new WindowsScriptRunnerDbContext(options);
    }
}
