using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WindowsScriptRunner.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddWorkerQueueLeases : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_AuditEventProperties_NonSensitiveKey",
            schema: "wsr",
            table: "AuditEventProperties");

        migrationBuilder.CreateSequence(
            name: "JobLeaseFencingSequence",
            schema: "wsr",
            minValue: 1L);

        migrationBuilder.Sql(
            """
            DECLARE @MigrationUtc datetimeoffset(7) =
                TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
            DECLARE @ActiveExecutionJobs TABLE
            (
                [JobId] uniqueidentifier NOT NULL PRIMARY KEY
            );

            INSERT INTO @ActiveExecutionJobs ([JobId])
            SELECT DISTINCT jobRow.[Id]
            FROM [wsr].[Jobs] AS jobRow
            INNER JOIN [wsr].[JobExecutions] AS executionRow
                ON executionRow.[JobId] = jobRow.[Id]
            WHERE jobRow.[Status] IN (N'Claimed', N'Executing', N'PostValidation')
              AND executionRow.[StartedUtc] IS NOT NULL
              AND executionRow.[CompletedUtc] IS NULL;

            INSERT INTO [wsr].[AuditEvents]
                ([Id], [EventType], [EntityType], [EntityId], [Actor], [OccurredUtc], [Summary])
            SELECT
                NEWID(),
                N'LegacyWorkerStateRecovered',
                N'Job',
                CONVERT(nvarchar(36), [Id]),
                N'system:migration',
                @MigrationUtc,
                N'Pre-lease worker-controlled state was normalized during the Phase 4 migration.'
            FROM [wsr].[Jobs]
            WHERE [Status] IN (N'DryRunRunning', N'Claimed', N'Executing', N'PostValidation');

            UPDATE executionRow
            SET
                [CompletedUtc] =
                    CASE
                        WHEN executionRow.[StartedUtc] > @MigrationUtc
                            THEN executionRow.[StartedUtc]
                        ELSE @MigrationUtc
                    END,
                [Outcome] = N'TimedOut',
                [ExitCode] = NULL,
                [Summary] = N'Active pre-lease work was timed out by the Phase 4 migration.'
            FROM [wsr].[JobExecutions] AS executionRow
            INNER JOIN [wsr].[Jobs] AS jobRow
                ON jobRow.[Id] = executionRow.[JobId]
            WHERE jobRow.[Status] IN (N'Claimed', N'Executing', N'PostValidation')
              AND executionRow.[StartedUtc] IS NOT NULL
              AND executionRow.[CompletedUtc] IS NULL;

            UPDATE jobRow
            SET
                [Status] =
                    CASE
                        WHEN jobRow.[Status] = N'Claimed'
                             AND NOT EXISTS
                             (
                                 SELECT 1
                                 FROM @ActiveExecutionJobs AS activeExecution
                                 WHERE activeExecution.[JobId] = jobRow.[Id]
                             )
                            THEN N'ExecutionQueued'
                        ELSE N'TimedOut'
                    END,
                [LastActingUser] = N'system:migration',
                [UpdatedUtc] =
                    CASE
                        WHEN jobRow.[UpdatedUtc] > @MigrationUtc
                            THEN jobRow.[UpdatedUtc]
                        ELSE @MigrationUtc
                    END
            FROM [wsr].[Jobs] AS jobRow
            WHERE jobRow.[Status] IN (N'DryRunRunning', N'Claimed', N'Executing', N'PostValidation');
            """);

        migrationBuilder.CreateTable(
            name: "JobLeases",
            schema: "wsr",
            columns: table => new
            {
                JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkerNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                FencingToken = table.Column<long>(type: "bigint", nullable: false),
                AcquiredUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                LastRenewedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobLeases", x => x.JobId);
                table.CheckConstraint("CK_JobLeases_FencingToken", "[FencingToken] > 0");
                table.CheckConstraint("CK_JobLeases_JobId", "[JobId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JobLeases_LeaseId", "[LeaseId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JobLeases_Timestamps", "[LastRenewedUtc] >= [AcquiredUtc] AND [ExpiresUtc] > [LastRenewedUtc]");
                table.CheckConstraint("CK_JobLeases_WorkerNodeId", "[WorkerNodeId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JobLeases_WorkKind", "[WorkKind] IN ('DryRun','Execute')");
                table.ForeignKey(
                    name: "FK_JobLeases_Jobs_JobId",
                    column: x => x.JobId,
                    principalSchema: "wsr",
                    principalTable: "Jobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_JobLeases_WorkerNodes_WorkerNodeId",
                    column: x => x.WorkerNodeId,
                    principalSchema: "wsr",
                    principalTable: "WorkerNodes",
                    principalColumn: "Id");
            });

        migrationBuilder.AddCheckConstraint(
            name: "CK_AuditEventProperties_NonSensitiveKey",
            schema: "wsr",
            table: "AuditEventProperties",
            sql: "[NormalizedKey] NOT LIKE '%PASSWORD%' AND [NormalizedKey] NOT LIKE '%SECRET%' AND ([NormalizedKey] NOT LIKE '%TOKEN%' OR [NormalizedKey] = 'FENCINGTOKEN')");

        migrationBuilder.CreateIndex(
            name: "IX_JobLeases_ExpiresUtc",
            schema: "wsr",
            table: "JobLeases",
            column: "ExpiresUtc");

        migrationBuilder.CreateIndex(
            name: "IX_JobLeases_WorkerNodeId_ExpiresUtc",
            schema: "wsr",
            table: "JobLeases",
            columns: new[] { "WorkerNodeId", "ExpiresUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_JobLeases_WorkKind_ExpiresUtc",
            schema: "wsr",
            table: "JobLeases",
            columns: new[] { "WorkKind", "ExpiresUtc" });

        migrationBuilder.CreateIndex(
            name: "UX_JobLeases_LeaseId",
            schema: "wsr",
            table: "JobLeases",
            column: "LeaseId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "JobLeases",
            schema: "wsr");

        migrationBuilder.DropCheckConstraint(
            name: "CK_AuditEventProperties_NonSensitiveKey",
            schema: "wsr",
            table: "AuditEventProperties");

        migrationBuilder.Sql(
            """
            DELETE FROM [wsr].[AuditEventProperties]
            WHERE [NormalizedKey] = N'FENCINGTOKEN';
            """);

        migrationBuilder.DropSequence(
            name: "JobLeaseFencingSequence",
            schema: "wsr");

        migrationBuilder.AddCheckConstraint(
            name: "CK_AuditEventProperties_NonSensitiveKey",
            schema: "wsr",
            table: "AuditEventProperties",
            sql: "[NormalizedKey] NOT LIKE '%PASSWORD%' AND [NormalizedKey] NOT LIKE '%SECRET%' AND [NormalizedKey] NOT LIKE '%TOKEN%'");
    }
}
