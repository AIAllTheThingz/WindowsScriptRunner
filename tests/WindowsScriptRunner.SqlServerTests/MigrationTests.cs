using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class MigrationTests
{
    private static readonly string[] ExpectedTables =
    [
        "AuditEventProperties",
        "AuditEvents",
        "CredentialReferences",
        "JobApprovals",
        "JobExecutions",
        "JobParameters",
        "Jobs",
        "JobTargets",
        "ScriptDefinitions",
        "ScriptParameterAllowedValues",
        "ScriptParameterDefinitions",
        "ScriptVersionPhases",
        "ScriptVersionReportFormats",
        "ScriptVersions",
        "WorkerCapabilities",
        "WorkerNodes",
    ];

    [Fact]
    public async Task InitialMigrationAppliesTwiceAndCreatesExpectedSqlServerSchema()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var appliedBefore = await context.Database.GetAppliedMigrationsAsync();
        await context.Database.MigrateAsync();
        var appliedAfter = await context.Database.GetAppliedMigrationsAsync();
        var tables = await context.Database.SqlQueryRaw<string>(
            """
            SELECT [TABLE_NAME] AS [Value]
            FROM [INFORMATION_SCHEMA].[TABLES]
            WHERE [TABLE_SCHEMA] = N'wsr'
              AND [TABLE_TYPE] = N'BASE TABLE'
              AND [TABLE_NAME] <> N'__EFMigrationsHistory'
            ORDER BY [TABLE_NAME]
            """).ToListAsync();
        var historySchema = await context.Database.SqlQueryRaw<string>(
            """
            SELECT [TABLE_SCHEMA] AS [Value]
            FROM [INFORMATION_SCHEMA].[TABLES]
            WHERE [TABLE_NAME] = N'__EFMigrationsHistory'
            """).SingleAsync();
        var indexes = await context.Database.SqlQueryRaw<string>(
            """
            SELECT [name] AS [Value]
            FROM [sys].[indexes]
            WHERE [object_id] IN
            (
                OBJECT_ID(N'[wsr].[JobExecutions]'),
                OBJECT_ID(N'[wsr].[Jobs]'),
                OBJECT_ID(N'[wsr].[ScriptDefinitions]'),
                OBJECT_ID(N'[wsr].[WorkerNodes]')
            )
              AND [name] IS NOT NULL
            """).ToListAsync();
        var filteredIndex = await context.Database.SqlQueryRaw<string>(
            """
            SELECT [filter_definition] AS [Value]
            FROM [sys].[indexes]
            WHERE [object_id] = OBJECT_ID(N'[wsr].[JobExecutions]')
              AND [name] = N'UX_JobExecutions_OneActivePerJob'
            """).SingleAsync();
        var rowVersionTables = await context.Database.SqlQueryRaw<string>(
            """
            SELECT OBJECT_NAME([object_id], DB_ID()) AS [Value]
            FROM [sys].[columns]
            WHERE [object_id] IN
            (
                OBJECT_ID(N'[wsr].[Jobs]'),
                OBJECT_ID(N'[wsr].[ScriptDefinitions]'),
                OBJECT_ID(N'[wsr].[WorkerNodes]'),
                OBJECT_ID(N'[wsr].[CredentialReferences]')
            )
              AND [name] = N'RowVersion'
              AND [system_type_id] = 189
            ORDER BY [Value]
            """).ToListAsync();
        var ownedCascadeCount = await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM [sys].[foreign_keys]
            WHERE [name] = N'FK_JobTargets_Jobs_JobId'
              AND [delete_referential_action_desc] = N'CASCADE'
            """).SingleAsync();
        var scriptNoActionCount = await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM [sys].[foreign_keys]
            WHERE [name] = N'FK_Jobs_ScriptDefinitions_ScriptDefinitionId'
              AND [delete_referential_action_desc] = N'NO_ACTION'
            """).SingleAsync();
        var workerNoActionCount = await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM [sys].[foreign_keys]
            WHERE [name] = N'FK_JobExecutions_WorkerNodes_WorkerNodeId'
              AND [delete_referential_action_desc] = N'NO_ACTION'
            """).SingleAsync();
        var triggers = await context.Database.SqlQueryRaw<string>(
            """
            SELECT [name] AS [Value]
            FROM [sys].[triggers]
            WHERE OBJECT_SCHEMA_NAME([object_id]) = N'wsr'
            ORDER BY [name]
            """).ToListAsync();

        Assert.Single(appliedBefore);
        Assert.Equal(appliedBefore, appliedAfter);
        Assert.Equal(ExpectedTables, tables);
        Assert.Equal("wsr", historySchema);
        Assert.Contains("UX_JobExecutions_OneActivePerJob", indexes);
        Assert.Contains("IX_Jobs_Status_UpdatedUtc", indexes);
        Assert.Contains("UX_ScriptDefinitions_NormalizedName", indexes);
        Assert.Contains("UX_WorkerNodes_NormalizedName", indexes);
        Assert.Equal(
            "([StartedUtc] IS NOT NULL AND [CompletedUtc] IS NULL)",
            filteredIndex);
        Assert.Equal(
            ["CredentialReferences", "Jobs", "ScriptDefinitions", "WorkerNodes"],
            rowVersionTables);
        Assert.Equal(1, ownedCascadeCount);
        Assert.Equal(1, scriptNoActionCount);
        Assert.Equal(1, workerNoActionCount);
        Assert.Contains(
            "TR_ScriptVersionPhases_RequireDryRunForPublishedExecute",
            triggers);
        Assert.Contains("TR_ScriptParameterAllowedValues_RequireEnum", triggers);
    }

    [Fact]
    public async Task MigrationRollsBackToZeroAndIdempotentScriptRestoresDatabase()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("0");
            Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
        }

        string script;
        await using (var scriptContext = database.CreateContext())
        {
            script = scriptContext.GetService<IMigrator>().GenerateScript(
                options: MigrationsSqlGenerationOptions.Idempotent);
        }

        Assert.DoesNotContain("Password=", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User ID=", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MSSQLLocalDB", script, StringComparison.OrdinalIgnoreCase);
        await database.ApplySqlScriptAsync(script);
        await database.ApplySqlScriptAsync(script);

        await using var restoredContext = database.CreateContext();
        Assert.Single(await restoredContext.Database.GetAppliedMigrationsAsync());
        Assert.Equal(
            ExpectedTables,
            await restoredContext.Database.SqlQueryRaw<string>(
                """
                SELECT [TABLE_NAME] AS [Value]
                FROM [INFORMATION_SCHEMA].[TABLES]
                WHERE [TABLE_SCHEMA] = N'wsr'
                  AND [TABLE_TYPE] = N'BASE TABLE'
                  AND [TABLE_NAME] <> N'__EFMigrationsHistory'
                ORDER BY [TABLE_NAME]
                """).ToListAsync());
        Assert.Equal(
            1,
            await restoredContext.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS [Value]
                FROM [sys].[indexes]
                WHERE [object_id] = OBJECT_ID(N'[wsr].[WorkerNodes]')
                  AND [name] = N'UX_WorkerNodes_NormalizedName'
                  AND [is_unique] = 1
                """).SingleAsync());
    }
}
