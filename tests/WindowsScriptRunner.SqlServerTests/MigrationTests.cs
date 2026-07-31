using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class MigrationTests
{
    private const string Phase7Migration = "20260730221709_AddDurableLocalHostInventoryReports";

    private static readonly string[] ExpectedTables =
    [
        "AuditEventProperties",
        "AuditEvents",
        "CredentialReferences",
        "JobApprovals",
        "JobExecutions",
        "JobLeases",
        "JobParameters",
        "JobReports",
        "Jobs",
        "JobTargets",
        "LocalHostInventoryReports",
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
                OBJECT_ID(N'[wsr].[JobReports]'),
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
        var reportIndexOrdering = await context.Database.SqlQueryRaw<string>(
            """
            SELECT CONCAT([columns].[name], N':', CONVERT(nvarchar(1), [indexColumns].[is_descending_key])) AS [Value]
            FROM [sys].[index_columns] AS [indexColumns]
            INNER JOIN [sys].[columns] AS [columns]
                ON [columns].[object_id] = [indexColumns].[object_id]
               AND [columns].[column_id] = [indexColumns].[column_id]
            INNER JOIN [sys].[indexes] AS [indexes]
                ON [indexes].[object_id] = [indexColumns].[object_id]
               AND [indexes].[index_id] = [indexColumns].[index_id]
            WHERE [indexColumns].[object_id] = OBJECT_ID(N'[wsr].[JobReports]')
              AND [indexes].[name] = N'IX_JobReports_ReportType_CreatedUtc_Id'
              AND [indexColumns].[key_ordinal] > 0
            ORDER BY [indexColumns].[key_ordinal]
            """).ToListAsync();
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
        var acceptedDryRunEvidenceConstraintCount = await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM [sys].[check_constraints]
            WHERE [parent_object_id] = OBJECT_ID(N'[wsr].[Jobs]')
              AND [name] = N'CK_Jobs_AcceptedDryRunEvidence'
            """).SingleAsync();

        Assert.Equal(4, appliedBefore.Count());
        Assert.Equal(appliedBefore, appliedAfter);
        Assert.Equal(ExpectedTables, tables);
        Assert.Equal("wsr", historySchema);
        Assert.Contains("UX_JobExecutions_OneActivePerJob", indexes);
        Assert.Contains("IX_Jobs_Status_UpdatedUtc", indexes);
        Assert.Contains("UX_ScriptDefinitions_NormalizedName", indexes);
        Assert.Contains("UX_WorkerNodes_NormalizedName", indexes);
        Assert.Contains("IX_JobReports_ReportType_CreatedUtc_Id", indexes);
        Assert.Equal(["ReportType:0", "CreatedUtc:1", "Id:0"], reportIndexOrdering);
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
        Assert.Equal(1, acceptedDryRunEvidenceConstraintCount);
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
        Assert.Equal(4, (await restoredContext.Database.GetAppliedMigrationsAsync()).Count());
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
    public async Task Phase8MigrationCancelsLegacyPreExecutionStatesWithoutFabricatingEvidence()
    {
        await using var database = await SqlServerDatabase.CreateAsync(
            applyMigrations: false);
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Phase7Migration);

        var definitionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var submittedJobId = Guid.NewGuid();
        var validatedJobId = Guid.NewGuid();
        var dryRunQueuedJobId = Guid.NewGuid();
        var dryRunRunningJobId = Guid.NewGuid();
        var dryRunCompletedJobId = Guid.NewGuid();
        var awaitingApprovalJobId = Guid.NewGuid();
        var approvedJobId = Guid.NewGuid();
        var executionQueuedJobId = Guid.NewGuid();
        var claimedJobId = Guid.NewGuid();
        var workerNodeId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [wsr].[ScriptDefinitions]
                ([Id], [Name], [NormalizedName], [DisplayName], [Description], [RiskLevel], [IsEnabled], [CreatedBy], [CreatedUtc], [UpdatedUtc])
            VALUES
                ({definitionId}, N'legacy-phase8-migration', N'LEGACY-PHASE8-MIGRATION', N'Legacy Phase 8 Migration', N'Legacy migration test definition.', N'Medium', {true}, N'system:migration-test', {timestamp}, {timestamp});

            INSERT INTO [wsr].[ScriptVersions]
                ([Id], [ScriptDefinitionId], [Major], [Minor], [Patch], [RelativeScriptPath], [Sha256], [GitCommitSha], [MinimumPowerShellVersion], [DefaultTimeoutMinutes], [IsPublished], [CreatedUtc], [CreatedBy])
            VALUES
                ({versionId}, {definitionId}, 1, 0, 0, N'legacy/Phase8Migration.ps1', {new string('a', 64)}, N'abcdef1', N'7.4.0', 30, {false}, {timestamp}, N'system:migration-test');

            INSERT INTO [wsr].[Jobs]
                ([Id], [ScriptDefinitionId], [ScriptVersionId], [RequestedPhase], [Status], [RequestedBy], [LastActingUser], [CreatedUtc], [UpdatedUtc], [SubmittedUtc], [PolicyScriptDefinitionId], [PolicyScriptVersionId], [PolicyRiskLevel], [PolicySupportsExecute], [PolicySupportsPostValidation])
            VALUES
                ({submittedJobId}, {definitionId}, {versionId}, N'Execute', N'Submitted', N'sid:legacy-requester', N'sid:legacy-requester', {timestamp}, {timestamp}, {timestamp}, {definitionId}, {versionId}, N'Medium', {true}, {false}),
                ({validatedJobId}, {definitionId}, {versionId}, N'Execute', N'Validated', N'sid:legacy-requester', N'sid:legacy-requester', {timestamp}, {timestamp}, {timestamp}, {definitionId}, {versionId}, N'Medium', {true}, {false}),
                ({dryRunQueuedJobId}, {definitionId}, {versionId}, N'Execute', N'DryRunQueued', N'sid:legacy-requester', N'sid:legacy-requester', {timestamp}, {timestamp}, {timestamp}, {definitionId}, {versionId}, N'Medium', {true}, {false}),
                ({dryRunRunningJobId}, {definitionId}, {versionId}, N'Execute', N'DryRunRunning', N'sid:legacy-requester', N'sid:legacy-requester', {timestamp}, {timestamp}, {timestamp}, {definitionId}, {versionId}, N'Medium', {true}, {false}),
                ({dryRunCompletedJobId}, {definitionId}, {versionId}, N'Execute', N'DryRunCompleted', N'sid:legacy-requester', N'sid:legacy-requester', {timestamp}, {timestamp}, {timestamp}, {definitionId}, {versionId}, N'Medium', {true}, {false}),
                ({awaitingApprovalJobId}, {definitionId}, {versionId}, N'Execute', N'AwaitingApproval', N'sid:legacy-requester', N'sid:legacy-requester', {timestamp}, {timestamp}, {timestamp}, {definitionId}, {versionId}, N'Medium', {true}, {false}),
                ({approvedJobId}, {definitionId}, {versionId}, N'Execute', N'Approved', N'sid:legacy-requester', N'sid:legacy-requester', {timestamp}, {timestamp}, {timestamp}, {definitionId}, {versionId}, N'Medium', {true}, {false}),
                ({executionQueuedJobId}, {definitionId}, {versionId}, N'Execute', N'ExecutionQueued', N'sid:legacy-requester', N'sid:legacy-requester', {timestamp}, {timestamp}, {timestamp}, {definitionId}, {versionId}, N'Medium', {true}, {false}),
                ({claimedJobId}, {definitionId}, {versionId}, N'Execute', N'Claimed', N'sid:legacy-requester', N'sid:legacy-requester', {timestamp}, {timestamp}, {timestamp}, {definitionId}, {versionId}, N'Medium', {true}, {false});

            INSERT INTO [wsr].[WorkerNodes]
                ([Id], [Name], [NormalizedName], [IsEnabled], [RegisteredUtc], [LastHeartbeatUtc])
            VALUES
                ({workerNodeId}, N'legacy-phase8-migration-worker', N'LEGACY-PHASE8-MIGRATION-WORKER', {true}, {timestamp}, {timestamp});

            INSERT INTO [wsr].[JobLeases]
                ([JobId], [LeaseId], [WorkerNodeId], [WorkKind], [FencingToken], [AcquiredUtc], [LastRenewedUtc], [ExpiresUtc])
            VALUES
                ({claimedJobId}, {leaseId}, {workerNodeId}, N'Execute', 1, {timestamp}, {timestamp}, {timestamp.AddMinutes(1)});
            """);

        await migrator.MigrateAsync();

        var statuses = await context.Database.SqlQueryRaw<string>(
            """
            SELECT [Status] AS [Value]
            FROM [wsr].[Jobs]
            WHERE [Id] IN ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})
            ORDER BY [Id]
            """,
            submittedJobId,
            validatedJobId,
            dryRunQueuedJobId,
            dryRunRunningJobId,
            dryRunCompletedJobId,
            awaitingApprovalJobId,
            approvedJobId,
            executionQueuedJobId,
            claimedJobId).ToListAsync();
        var auditCount = await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM [wsr].[AuditEvents]
            WHERE [EventType] = N'LegacyDryRunEvidenceUnavailable'
              AND [Actor] = N'system:phase-8-evidence-migration'
            """).SingleAsync();

        Assert.Equal(["Cancelled", "Cancelled", "Cancelled", "Cancelled", "Cancelled", "Cancelled", "Cancelled", "Cancelled", "Cancelled"], statuses);
        Assert.Equal(9, auditCount);
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM [wsr].[JobLeases]
            WHERE [JobId] = {0}
            """,
            claimedJobId).SingleAsync());

        await migrator.MigrateAsync(Phase7Migration);
        Assert.Equal(9, await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM [wsr].[Jobs]
            WHERE [Id] IN ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})
              AND [Status] = N'Cancelled'
            """,
            submittedJobId,
            validatedJobId,
            dryRunQueuedJobId,
            dryRunRunningJobId,
            dryRunCompletedJobId,
            awaitingApprovalJobId,
            approvedJobId,
            executionQueuedJobId,
            claimedJobId).SingleAsync());
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

            var auditEventId = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO [wsr].[AuditEvents]
                    ([Id], [EventType], [EntityType], [EntityId], [Actor], [OccurredUtc], [Summary])
                VALUES
                    ({auditEventId}, N'JobLeaseAcquired', N'Job', N'rollback-test', N'worker:test',
                     {DateTimeOffset.UtcNow}, N'Phase 4 rollback fencing-token test.');

                INSERT INTO [wsr].[AuditEventProperties]
                    ([AuditEventId], [NormalizedKey], [Key], [Value])
                VALUES
                    ({auditEventId}, N'FENCINGTOKEN', N'FencingToken', N'42');
                """);

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
            Assert.Equal(
                1,
                await context.Database.SqlQueryRaw<int>(
                    """
                    SELECT COUNT(*) AS [Value]
                    FROM [wsr].[AuditEvents]
                    WHERE [Id] = {0}
                    """,
                    auditEventId).SingleAsync());
            Assert.Equal(
                0,
                await context.Database.SqlQueryRaw<int>(
                    """
                    SELECT COUNT(*) AS [Value]
                    FROM [wsr].[AuditEventProperties]
                    WHERE [AuditEventId] = {0}
                    """,
                    auditEventId).SingleAsync());

            await migrator.MigrateAsync();
            Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync()).Count());
        }
    }
}
