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
        "JobLeases",
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
                OBJECT_ID(N'[wsr].[JobLeases]'),
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
                OBJECT_ID(N'[wsr].[JobLeases]'),
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
        var pinnedVersionNoActionCount = await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM [sys].[foreign_keys]
            WHERE [name] = N'FK_Jobs_ScriptVersions_ScriptDefinitionId_ScriptVersionId'
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

        Assert.Equal(2, appliedBefore.Count());
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
            ["CredentialReferences", "JobLeases", "Jobs", "ScriptDefinitions", "WorkerNodes"],
            rowVersionTables);
        Assert.Equal(1, ownedCascadeCount);
        Assert.Equal(1, scriptNoActionCount);
        Assert.Equal(1, pinnedVersionNoActionCount);
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
        Assert.Equal(2, (await restoredContext.Database.GetAppliedMigrationsAsync()).Count());
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

    [Fact]
    public async Task Phase4MigrationCreatesLeaseSchemaAndRollsBackToPhase3()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            var sequence = await context.Database.SqlQueryRaw<string>(
                """
                SELECT CONCAT(
                    SCHEMA_NAME([schema_id]),
                    N'.',
                    [name],
                    N'|',
                    CONVERT(nvarchar(30), [start_value]),
                    N'|',
                    CONVERT(nvarchar(30), [increment]),
                    N'|',
                    CONVERT(nvarchar(30), [minimum_value])) AS [Value]
                FROM [sys].[sequences]
                WHERE [name] = N'JobLeaseFencingSequence'
                  AND [schema_id] = SCHEMA_ID(N'wsr')
                """).SingleAsync();
            var indexes = await context.Database.SqlQueryRaw<string>(
                """
                SELECT [name] AS [Value]
                FROM [sys].[indexes]
                WHERE [object_id] = OBJECT_ID(N'[wsr].[JobLeases]')
                  AND [name] IS NOT NULL
                ORDER BY [name]
                """).ToListAsync();
            var checks = await context.Database.SqlQueryRaw<string>(
                """
                SELECT [name] AS [Value]
                FROM [sys].[check_constraints]
                WHERE [parent_object_id] = OBJECT_ID(N'[wsr].[JobLeases]')
                ORDER BY [name]
                """).ToListAsync();
            var foreignKeys = await context.Database.SqlQueryRaw<string>(
                """
                SELECT CONCAT(
                    [name] COLLATE DATABASE_DEFAULT,
                    N'|',
                    [delete_referential_action_desc] COLLATE DATABASE_DEFAULT) AS [Value]
                FROM [sys].[foreign_keys]
                WHERE [parent_object_id] = OBJECT_ID(N'[wsr].[JobLeases]')
                ORDER BY [name]
                """).ToListAsync();
            var rowVersionCount = await context.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS [Value]
                FROM [sys].[columns]
                WHERE [object_id] = OBJECT_ID(N'[wsr].[JobLeases]')
                  AND [name] = N'RowVersion'
                  AND [system_type_id] = 189
                """).SingleAsync();

            Assert.Equal("wsr.JobLeaseFencingSequence|1|1|1", sequence);
            Assert.Contains("PK_JobLeases", indexes);
            Assert.Contains("UX_JobLeases_LeaseId", indexes);
            Assert.Contains("IX_JobLeases_ExpiresUtc", indexes);
            Assert.Contains("IX_JobLeases_WorkerNodeId_ExpiresUtc", indexes);
            Assert.Contains("IX_JobLeases_WorkKind_ExpiresUtc", indexes);
            Assert.Contains("CK_JobLeases_WorkKind", checks);
            Assert.Contains("CK_JobLeases_FencingToken", checks);
            Assert.Contains("CK_JobLeases_Timestamps", checks);
            Assert.Contains("FK_JobLeases_Jobs_JobId|CASCADE", foreignKeys);
            Assert.Contains("FK_JobLeases_WorkerNodes_WorkerNodeId|NO_ACTION", foreignKeys);
            Assert.Equal(1, rowVersionCount);

            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20260729175606_InitialSqlServerPersistence");
            Assert.Single(await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(
                0,
                await context.Database.SqlQueryRaw<int>(
                    """
                    SELECT COUNT(*) AS [Value]
                    FROM [sys].[tables]
                    WHERE [object_id] = OBJECT_ID(N'[wsr].[JobLeases]')
                    """).SingleAsync());
            Assert.Equal(
                0,
                await context.Database.SqlQueryRaw<int>(
                    """
                    SELECT COUNT(*) AS [Value]
                    FROM [sys].[sequences]
                    WHERE [name] = N'JobLeaseFencingSequence'
                      AND [schema_id] = SCHEMA_ID(N'wsr')
                    """).SingleAsync());

            await migrator.MigrateAsync();
            Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        }
    }
}
