using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WindowsScriptRunner.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTrustedDryRunApprovalEvidence : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AcceptedDryRunEvidenceCompletedUtc",
            schema: "wsr",
            table: "Jobs",
            type: "datetimeoffset(7)",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "AcceptedDryRunEvidenceFencingToken",
            schema: "wsr",
            table: "Jobs",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "AcceptedDryRunEvidenceLeaseId",
            schema: "wsr",
            table: "Jobs",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AcceptedDryRunEvidenceSource",
            schema: "wsr",
            table: "Jobs",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AcceptedDryRunEvidenceWindowOpenedUtc",
            schema: "wsr",
            table: "Jobs",
            type: "datetimeoffset(7)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AcceptedDryRunEvidenceWorkKind",
            schema: "wsr",
            table: "Jobs",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "AcceptedDryRunEvidenceWorkerNodeId",
            schema: "wsr",
            table: "Jobs",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Jobs_AcceptedDryRunEvidence",
            schema: "wsr",
            table: "Jobs",
            sql: "([AcceptedDryRunEvidenceWorkKind] IS NULL AND [AcceptedDryRunEvidenceSource] IS NULL AND [AcceptedDryRunEvidenceWorkerNodeId] IS NULL AND [AcceptedDryRunEvidenceLeaseId] IS NULL AND [AcceptedDryRunEvidenceFencingToken] IS NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] IS NULL AND [AcceptedDryRunEvidenceCompletedUtc] IS NULL) OR ([AcceptedDryRunEvidenceWorkKind] IS NOT NULL AND [AcceptedDryRunEvidenceWorkKind] = 'DryRun' AND [AcceptedDryRunEvidenceSource] IS NOT NULL AND [AcceptedDryRunEvidenceSource] = 'InternalLifecycle' AND [AcceptedDryRunEvidenceWorkerNodeId] IS NULL AND [AcceptedDryRunEvidenceLeaseId] IS NULL AND [AcceptedDryRunEvidenceFencingToken] IS NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceCompletedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] <= [AcceptedDryRunEvidenceCompletedUtc]) OR ([AcceptedDryRunEvidenceWorkKind] IS NOT NULL AND [AcceptedDryRunEvidenceWorkKind] = 'DryRun' AND [AcceptedDryRunEvidenceSource] IS NOT NULL AND [AcceptedDryRunEvidenceSource] = 'LeasedWorker' AND [AcceptedDryRunEvidenceWorkerNodeId] IS NOT NULL AND [AcceptedDryRunEvidenceWorkerNodeId] <> '00000000-0000-0000-0000-000000000000' AND [AcceptedDryRunEvidenceLeaseId] IS NOT NULL AND [AcceptedDryRunEvidenceLeaseId] <> '00000000-0000-0000-0000-000000000000' AND [AcceptedDryRunEvidenceFencingToken] IS NOT NULL AND [AcceptedDryRunEvidenceFencingToken] > 0 AND [AcceptedDryRunEvidenceWindowOpenedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceCompletedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] <= [AcceptedDryRunEvidenceCompletedUtc])");

        migrationBuilder.Sql(
            """
            DECLARE @OccurredUtc datetimeoffset(7) = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
            DECLARE @Actor nvarchar(256) = N'system:phase-8-evidence-migration';

            INSERT INTO [wsr].[AuditEvents]
                ([Id], [EventType], [EntityType], [EntityId], [Actor], [OccurredUtc], [Summary])
            SELECT NEWID(),
                   N'LegacyDryRunEvidenceUnavailable',
                   N'Job',
                   CONVERT(nvarchar(36), [Id]),
                   @Actor,
                   @OccurredUtc,
                   N'Cancelled legacy Execute job because trusted accepted DryRun evidence was not persisted before Phase 8.'
            FROM [wsr].[Jobs]
            WHERE [RequestedPhase] = N'Execute'
              AND [Status] IN (N'DryRunCompleted', N'AwaitingApproval', N'Approved', N'ExecutionQueued', N'Claimed');

            DELETE [wsr].[JobLeases]
            WHERE [JobId] IN
            (
                SELECT [Id]
                FROM [wsr].[Jobs]
                WHERE [RequestedPhase] = N'Execute'
                  AND [Status] IN (N'DryRunCompleted', N'AwaitingApproval', N'Approved', N'ExecutionQueued', N'Claimed')
            );

            UPDATE [wsr].[Jobs]
            SET [Status] = N'Cancelled',
                [LastActingUser] = @Actor,
                [UpdatedUtc] = CASE
                    WHEN [UpdatedUtc] > @OccurredUtc THEN [UpdatedUtc]
                    ELSE @OccurredUtc
                END
            WHERE [RequestedPhase] = N'Execute'
              AND [Status] IN (N'DryRunCompleted', N'AwaitingApproval', N'Approved', N'ExecutionQueued', N'Claimed');
            """);

        migrationBuilder.CreateIndex(
            name: "IX_JobReports_ReportType_CreatedUtc_Id",
            schema: "wsr",
            table: "JobReports",
            columns: new[] { "ReportType", "CreatedUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Jobs_AcceptedDryRunEvidence",
            schema: "wsr",
            table: "Jobs");

        migrationBuilder.DropIndex(
            name: "IX_JobReports_ReportType_CreatedUtc_Id",
            schema: "wsr",
            table: "JobReports");

        migrationBuilder.DropColumn(
            name: "AcceptedDryRunEvidenceCompletedUtc",
            schema: "wsr",
            table: "Jobs");

        migrationBuilder.DropColumn(
            name: "AcceptedDryRunEvidenceFencingToken",
            schema: "wsr",
            table: "Jobs");

        migrationBuilder.DropColumn(
            name: "AcceptedDryRunEvidenceLeaseId",
            schema: "wsr",
            table: "Jobs");

        migrationBuilder.DropColumn(
            name: "AcceptedDryRunEvidenceSource",
            schema: "wsr",
            table: "Jobs");

        migrationBuilder.DropColumn(
            name: "AcceptedDryRunEvidenceWindowOpenedUtc",
            schema: "wsr",
            table: "Jobs");

        migrationBuilder.DropColumn(
            name: "AcceptedDryRunEvidenceWorkKind",
            schema: "wsr",
            table: "Jobs");

        migrationBuilder.DropColumn(
            name: "AcceptedDryRunEvidenceWorkerNodeId",
            schema: "wsr",
            table: "Jobs");
    }
}
