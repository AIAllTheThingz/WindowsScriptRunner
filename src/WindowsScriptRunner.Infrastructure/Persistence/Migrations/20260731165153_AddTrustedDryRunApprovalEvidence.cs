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
            sql: "([AcceptedDryRunEvidenceWorkKind] IS NULL AND [AcceptedDryRunEvidenceSource] IS NULL AND [AcceptedDryRunEvidenceWorkerNodeId] IS NULL AND [AcceptedDryRunEvidenceLeaseId] IS NULL AND [AcceptedDryRunEvidenceFencingToken] IS NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] IS NULL AND [AcceptedDryRunEvidenceCompletedUtc] IS NULL) OR ([AcceptedDryRunEvidenceWorkKind] = 'DryRun' AND [AcceptedDryRunEvidenceSource] = 'InternalLifecycle' AND [AcceptedDryRunEvidenceWorkerNodeId] IS NULL AND [AcceptedDryRunEvidenceLeaseId] IS NULL AND [AcceptedDryRunEvidenceFencingToken] IS NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceCompletedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] <= [AcceptedDryRunEvidenceCompletedUtc]) OR ([AcceptedDryRunEvidenceWorkKind] = 'DryRun' AND [AcceptedDryRunEvidenceSource] = 'LeasedWorker' AND [AcceptedDryRunEvidenceWorkerNodeId] IS NOT NULL AND [AcceptedDryRunEvidenceLeaseId] IS NOT NULL AND [AcceptedDryRunEvidenceFencingToken] > 0 AND [AcceptedDryRunEvidenceWindowOpenedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceCompletedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] <= [AcceptedDryRunEvidenceCompletedUtc])");

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
