using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WindowsScriptRunner.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDurableLocalHostInventoryReports : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "JobReports",
            schema: "wsr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScriptDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScriptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PackageId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                PackageVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ReportType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                SchemaVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                Format = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                WorkerNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FencingToken = table.Column<long>(type: "bigint", nullable: false),
                PowerShellExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                CollectedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                Sha256 = table.Column<string>(type: "char(64)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobReports", x => x.Id);
                table.CheckConstraint("CK_JobReports_FencingToken", "[FencingToken] > 0");
                table.CheckConstraint("CK_JobReports_Identifiers", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [JobId] <> '00000000-0000-0000-0000-000000000000' AND [ScriptDefinitionId] <> '00000000-0000-0000-0000-000000000000' AND [ScriptVersionId] <> '00000000-0000-0000-0000-000000000000' AND [WorkerNodeId] <> '00000000-0000-0000-0000-000000000000' AND [LeaseId] <> '00000000-0000-0000-0000-000000000000' AND [PowerShellExecutionId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JobReports_Sha256", "LEN([Sha256]) = 64 AND [Sha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                table.CheckConstraint("CK_JobReports_SupportedType", "[PackageId] COLLATE Latin1_General_100_BIN2 = 'windows.local-host-inventory' AND [PackageVersion] COLLATE Latin1_General_100_BIN2 = '1.0.0' AND [ReportType] COLLATE Latin1_General_100_BIN2 = 'LocalHostInventory' AND [SchemaVersion] COLLATE Latin1_General_100_BIN2 = '1.0' AND [Format] COLLATE Latin1_General_100_BIN2 = 'Json'");
                table.CheckConstraint("CK_JobReports_Timestamps", "[CollectedUtc] <= DATEADD(second, 5, [CreatedUtc])");
                table.ForeignKey(
                    name: "FK_JobReports_Jobs_JobId",
                    column: x => x.JobId,
                    principalSchema: "wsr",
                    principalTable: "Jobs",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_JobReports_ScriptDefinitions_ScriptDefinitionId",
                    column: x => x.ScriptDefinitionId,
                    principalSchema: "wsr",
                    principalTable: "ScriptDefinitions",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_JobReports_ScriptVersions_ScriptDefinitionId_ScriptVersionId",
                    columns: x => new { x.ScriptDefinitionId, x.ScriptVersionId },
                    principalSchema: "wsr",
                    principalTable: "ScriptVersions",
                    principalColumns: new[] { "ScriptDefinitionId", "Id" });
                table.ForeignKey(
                    name: "FK_JobReports_WorkerNodes_WorkerNodeId",
                    column: x => x.WorkerNodeId,
                    principalSchema: "wsr",
                    principalTable: "WorkerNodes",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "LocalHostInventoryReports",
            schema: "wsr",
            columns: table => new
            {
                ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ComputerName = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: false),
                OsDescription = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                OsVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                OsArchitecture = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                PowerShellVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LocalHostInventoryReports", x => x.ReportId);
                table.CheckConstraint("CK_LocalHostInventoryReports_Architecture", "[OsArchitecture] COLLATE Latin1_General_100_BIN2 IN ('X86','X64','Arm','Arm64')");
                table.CheckConstraint("CK_LocalHostInventoryReports_ComputerName", "LEN([ComputerName]) BETWEEN 1 AND 63 AND [ComputerName] NOT LIKE '%[^A-Za-z0-9-]%' COLLATE Latin1_General_100_BIN2 AND [ComputerName] NOT LIKE '-%' AND [ComputerName] NOT LIKE '%-'");
                table.CheckConstraint("CK_LocalHostInventoryReports_OsDescription", "LEN([OsDescription]) BETWEEN 1 AND 256");
                table.CheckConstraint("CK_LocalHostInventoryReports_ReportId", "[ReportId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_LocalHostInventoryReports_Versions", "LEN([OsVersion]) BETWEEN 5 AND 32 AND LEN([PowerShellVersion]) BETWEEN 5 AND 32 AND [OsVersion] NOT LIKE '%[^0-9.]%' AND [PowerShellVersion] NOT LIKE '%[^0-9.]%' AND [OsVersion] NOT LIKE '.%' AND [OsVersion] NOT LIKE '%.' AND [PowerShellVersion] NOT LIKE '.%' AND [PowerShellVersion] NOT LIKE '%.' AND [OsVersion] NOT LIKE '%..%' AND [PowerShellVersion] NOT LIKE '%..%'");
                table.ForeignKey(
                    name: "FK_LocalHostInventoryReports_JobReports_ReportId",
                    column: x => x.ReportId,
                    principalSchema: "wsr",
                    principalTable: "JobReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_JobReports_ScriptDefinitionId_ScriptVersionId",
            schema: "wsr",
            table: "JobReports",
            columns: new[] { "ScriptDefinitionId", "ScriptVersionId" });

        migrationBuilder.CreateIndex(
            name: "IX_JobReports_WorkerNodeId",
            schema: "wsr",
            table: "JobReports",
            column: "WorkerNodeId");

        migrationBuilder.CreateIndex(
            name: "UX_JobReports_Job_Package_Schema",
            schema: "wsr",
            table: "JobReports",
            columns: new[] { "JobId", "PackageId", "SchemaVersion" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_JobReports_LeaseId",
            schema: "wsr",
            table: "JobReports",
            column: "LeaseId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_JobReports_PowerShellExecutionId",
            schema: "wsr",
            table: "JobReports",
            column: "PowerShellExecutionId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "LocalHostInventoryReports",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "JobReports",
            schema: "wsr");
    }
}
