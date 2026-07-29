using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WindowsScriptRunner.Infrastructure.Persistence;

namespace WindowsScriptRunner.SqlServerTests;

internal sealed class SqlServerDatabase : IAsyncDisposable
{
    private readonly bool deleteOnDispose;

    private SqlServerDatabase(
        string connectionString,
        string runtimeName,
        bool deleteOnDispose)
    {
        ConnectionString = connectionString;
        RuntimeName = runtimeName;
        this.deleteOnDispose = deleteOnDispose;
    }

    internal string ConnectionString { get; }
    public string RuntimeName { get; }

    public static async Task<SqlServerDatabase> CreateAsync(
        bool applyMigrations = true,
        CancellationToken cancellationToken = default,
        int? connectionTimeoutSeconds = null,
        string? baseConnectionString = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var supplied = baseConnectionString ??
            Environment.GetEnvironmentVariable("WINDOWSSCRIPTRUNNER_TEST_SQLSERVER");
        var runtimeName = "SQL Server LocalDB MSSQLLocalDB";
        if (baseConnectionString is not null)
        {
            runtimeName = "explicit test SQL Server endpoint";
        }
        else if (!string.IsNullOrWhiteSpace(supplied))
        {
            runtimeName = "WINDOWSSCRIPTRUNNER_TEST_SQLSERVER";
        }

        var baseConnection = string.IsNullOrWhiteSpace(supplied)
            ? "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;Encrypt=false"
            : supplied;
        var builder = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"WindowsScriptRunner_Test_{Guid.NewGuid():N}",
        };
        if (connectionTimeoutSeconds is not null)
        {
            builder.ConnectTimeout = connectionTimeoutSeconds.Value;
        }

        var database = new SqlServerDatabase(
            builder.ConnectionString,
            runtimeName,
            applyMigrations);
        try
        {
            if (applyMigrations)
            {
                await using var context = database.CreateContext();
                await context.Database.MigrateAsync(cancellationToken);
            }

            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public WindowsScriptRunnerDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WindowsScriptRunnerDbContext>()
            .UseSqlServer(
                ConnectionString,
                sql =>
                {
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", "wsr");
                    sql.CommandTimeout(60);
                })
            .EnableDetailedErrors(false)
            .EnableSensitiveDataLogging(false);
        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new WindowsScriptRunnerDbContext(optionsBuilder.Options);
    }

    public async Task ApplySqlScriptAsync(
        string script,
        CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            foreach (var batch in SplitBatches(script))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                command.CommandTimeout = 60;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!deleteOnDispose)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    private static IEnumerable<string> SplitBatches(string script) =>
        Regex.Split(
                script,
                @"^\s*GO\s*(?:--.*)?$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Where(batch => !string.IsNullOrWhiteSpace(batch));
}
