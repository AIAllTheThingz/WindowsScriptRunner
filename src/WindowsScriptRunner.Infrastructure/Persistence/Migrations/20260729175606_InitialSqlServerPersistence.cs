using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WindowsScriptRunner.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialSqlServerPersistence : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "wsr");

        migrationBuilder.CreateTable(
            name: "AuditEvents",
            schema: "wsr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EventType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                EntityType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                EntityId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Actor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                OccurredUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditEvents", x => x.Id);
                table.CheckConstraint("CK_AuditEvents_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
            });

        migrationBuilder.CreateTable(
            name: "CredentialReferences",
            schema: "wsr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProviderType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                NormalizedProviderType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ExternalIdentifier = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ExternalIdentifierHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CredentialReferences", x => x.Id);
                table.CheckConstraint("CK_CredentialReferences_Hash", "DATALENGTH([ExternalIdentifierHash]) = 32");
                table.CheckConstraint("CK_CredentialReferences_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
            });

        migrationBuilder.CreateTable(
            name: "ScriptDefinitions",
            schema: "wsr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                RiskLevel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScriptDefinitions", x => x.Id);
                table.CheckConstraint("CK_ScriptDefinitions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ScriptDefinitions_RiskLevel", "[RiskLevel] IN ('ReadOnly','Low','Medium','High','Critical')");
                table.CheckConstraint("CK_ScriptDefinitions_Timestamps", "[CreatedUtc] <= [UpdatedUtc]");
            });

        migrationBuilder.CreateTable(
            name: "WorkerNodes",
            schema: "wsr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                RegisteredUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                LastHeartbeatUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkerNodes", x => x.Id);
                table.CheckConstraint("CK_WorkerNodes_Heartbeat", "[LastHeartbeatUtc] IS NULL OR [LastHeartbeatUtc] >= [RegisteredUtc]");
                table.CheckConstraint("CK_WorkerNodes_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
            });

        migrationBuilder.CreateTable(
            name: "AuditEventProperties",
            schema: "wsr",
            columns: table => new
            {
                AuditEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NormalizedKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditEventProperties", x => new { x.AuditEventId, x.NormalizedKey });
                table.CheckConstraint("CK_AuditEventProperties_NonSensitiveKey", "[NormalizedKey] NOT LIKE '%PASSWORD%' AND [NormalizedKey] NOT LIKE '%SECRET%' AND [NormalizedKey] NOT LIKE '%TOKEN%'");
                table.ForeignKey(
                    name: "FK_AuditEventProperties_AuditEvents_AuditEventId",
                    column: x => x.AuditEventId,
                    principalSchema: "wsr",
                    principalTable: "AuditEvents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ScriptVersions",
            schema: "wsr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScriptDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Major = table.Column<int>(type: "int", nullable: false),
                Minor = table.Column<int>(type: "int", nullable: false),
                Patch = table.Column<int>(type: "int", nullable: false),
                RelativeScriptPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Sha256 = table.Column<string>(type: "char(64)", nullable: false),
                GitCommitSha = table.Column<string>(type: "varchar(64)", nullable: true),
                MinimumPowerShellVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                DefaultTimeoutMinutes = table.Column<int>(type: "int", nullable: false),
                IsPublished = table.Column<bool>(type: "bit", nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScriptVersions", x => x.Id);
                table.UniqueConstraint(
                    "AK_ScriptVersions_ScriptDefinitionId_Id",
                    x => new { x.ScriptDefinitionId, x.Id });
                table.CheckConstraint("CK_ScriptVersions_GitCommitSha", "[GitCommitSha] IS NULL OR (LEN([GitCommitSha]) BETWEEN 7 AND 64 AND [GitCommitSha] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)");
                table.CheckConstraint("CK_ScriptVersions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ScriptVersions_Sha256", "LEN([Sha256]) = 64 AND [Sha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                table.CheckConstraint("CK_ScriptVersions_Timeout", "[DefaultTimeoutMinutes] BETWEEN 1 AND 480");
                table.CheckConstraint("CK_ScriptVersions_Version", "[Major] >= 0 AND [Minor] >= 0 AND [Patch] >= 0");
                table.ForeignKey(
                    name: "FK_ScriptVersions_ScriptDefinitions_ScriptDefinitionId",
                    column: x => x.ScriptDefinitionId,
                    principalSchema: "wsr",
                    principalTable: "ScriptDefinitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WorkerCapabilities",
            schema: "wsr",
            columns: table => new
            {
                WorkerNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkerCapabilities", x => new { x.WorkerNodeId, x.NormalizedName });
                table.ForeignKey(
                    name: "FK_WorkerCapabilities_WorkerNodes_WorkerNodeId",
                    column: x => x.WorkerNodeId,
                    principalSchema: "wsr",
                    principalTable: "WorkerNodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Jobs",
            schema: "wsr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScriptDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScriptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestedPhase = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                RequestedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                LastActingUser = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                SubmittedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                ChangeReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                PolicyScriptDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PolicyScriptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PolicyRiskLevel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                PolicySupportsExecute = table.Column<bool>(type: "bit", nullable: true),
                PolicySupportsPostValidation = table.Column<bool>(type: "bit", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Jobs", x => x.Id);
                table.CheckConstraint("CK_Jobs_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Jobs_PolicyRiskLevel", "[PolicyRiskLevel] IS NULL OR [PolicyRiskLevel] IN ('ReadOnly','Low','Medium','High','Critical')");
                table.CheckConstraint("CK_Jobs_PolicySnapshot_AllOrNone", "([PolicyScriptDefinitionId] IS NULL AND [PolicyScriptVersionId] IS NULL AND [PolicyRiskLevel] IS NULL AND [PolicySupportsExecute] IS NULL AND [PolicySupportsPostValidation] IS NULL AND [SubmittedUtc] IS NULL AND [Status] = 'Draft') OR ([PolicyScriptDefinitionId] IS NOT NULL AND [PolicyScriptVersionId] IS NOT NULL AND [PolicyRiskLevel] IS NOT NULL AND [PolicySupportsExecute] IS NOT NULL AND [PolicySupportsPostValidation] IS NOT NULL AND [SubmittedUtc] IS NOT NULL AND [Status] <> 'Draft')");
                table.CheckConstraint("CK_Jobs_PolicySnapshot_Ids", "[PolicyScriptDefinitionId] IS NULL OR ([PolicyScriptDefinitionId] = [ScriptDefinitionId] AND [PolicyScriptVersionId] = [ScriptVersionId])");
                table.CheckConstraint("CK_Jobs_RequestedPhase", "[RequestedPhase] IN ('Discovery','Validation','DryRun','Report','Execute','PostValidation')");
                table.CheckConstraint("CK_Jobs_Status", "[Status] IN ('Draft','Submitted','Validated','DryRunQueued','DryRunRunning','DryRunCompleted','AwaitingApproval','Approved','ExecutionQueued','Claimed','Executing','PostValidation','Completed','CompletedWithWarnings','Failed','Rejected','Cancelled','TimedOut','Blocked','NotRun')");
                table.CheckConstraint("CK_Jobs_Timestamps", "[CreatedUtc] <= [UpdatedUtc] AND ([SubmittedUtc] IS NULL OR ([SubmittedUtc] >= [CreatedUtc] AND [SubmittedUtc] <= [UpdatedUtc]))");
                table.ForeignKey(
                    name: "FK_Jobs_ScriptDefinitions_ScriptDefinitionId",
                    column: x => x.ScriptDefinitionId,
                    principalSchema: "wsr",
                    principalTable: "ScriptDefinitions",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Jobs_ScriptVersions_ScriptDefinitionId_ScriptVersionId",
                    columns: x => new { x.ScriptDefinitionId, x.ScriptVersionId },
                    principalSchema: "wsr",
                    principalTable: "ScriptVersions",
                    principalColumns: new[] { "ScriptDefinitionId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "ScriptParameterDefinitions",
            schema: "wsr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScriptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                ParameterType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                IsRequired = table.Column<bool>(type: "bit", nullable: false),
                DefaultValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                IsSensitive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScriptParameterDefinitions", x => x.Id);
                table.CheckConstraint("CK_ScriptParameterDefinitions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ScriptParameterDefinitions_SecureReference", "[ParameterType] <> 'SecureReference' OR [IsSensitive] = 1");
                table.CheckConstraint("CK_ScriptParameterDefinitions_SensitiveDefault", "[IsSensitive] = 0 OR [DefaultValue] IS NULL");
                table.CheckConstraint("CK_ScriptParameterDefinitions_Type", "[ParameterType] IN ('String','StringArray','Integer','Boolean','DateTime','Enum','SecureReference')");
                table.ForeignKey(
                    name: "FK_ScriptParameterDefinitions_ScriptVersions_ScriptVersionId",
                    column: x => x.ScriptVersionId,
                    principalSchema: "wsr",
                    principalTable: "ScriptVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ScriptVersionPhases",
            schema: "wsr",
            columns: table => new
            {
                ScriptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Phase = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScriptVersionPhases", x => new { x.ScriptVersionId, x.Phase });
                table.CheckConstraint("CK_ScriptVersionPhases_Phase", "[Phase] IN ('Discovery','Validation','DryRun','Report','Execute','PostValidation')");
                table.ForeignKey(
                    name: "FK_ScriptVersionPhases_ScriptVersions_ScriptVersionId",
                    column: x => x.ScriptVersionId,
                    principalSchema: "wsr",
                    principalTable: "ScriptVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ScriptVersionReportFormats",
            schema: "wsr",
            columns: table => new
            {
                ScriptVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ReportFormat = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScriptVersionReportFormats", x => new { x.ScriptVersionId, x.ReportFormat });
                table.CheckConstraint("CK_ScriptVersionReportFormats_Format", "[ReportFormat] IN ('Text','Csv','Json','Html')");
                table.ForeignKey(
                    name: "FK_ScriptVersionReportFormats_ScriptVersions_ScriptVersionId",
                    column: x => x.ScriptVersionId,
                    principalSchema: "wsr",
                    principalTable: "ScriptVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "JobApprovals",
            schema: "wsr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Decision = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                Approver = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                DecisionUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                ApprovalFingerprint = table.Column<string>(type: "char(64)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobApprovals", x => x.Id);
                table.CheckConstraint("CK_JobApprovals_Decision", "[Decision] IN ('Approved','Rejected')");
                table.CheckConstraint("CK_JobApprovals_Fingerprint", "LEN([ApprovalFingerprint]) = 64 AND [ApprovalFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                table.CheckConstraint("CK_JobApprovals_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.ForeignKey(
                    name: "FK_JobApprovals_Jobs_JobId",
                    column: x => x.JobId,
                    principalSchema: "wsr",
                    principalTable: "Jobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "JobExecutions",
            schema: "wsr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AttemptNumber = table.Column<int>(type: "int", nullable: false),
                WorkerNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                StartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CompletedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                ExitCode = table.Column<int>(type: "int", nullable: true),
                Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobExecutions", x => x.Id);
                table.CheckConstraint("CK_JobExecutions_Attempt", "[AttemptNumber] > 0");
                table.CheckConstraint("CK_JobExecutions_Completion", "([CompletedUtc] IS NULL AND [Outcome] IS NULL AND [ExitCode] IS NULL AND [Summary] IS NULL) OR ([CompletedUtc] IS NOT NULL AND [StartedUtc] IS NOT NULL AND [CompletedUtc] >= [StartedUtc] AND [Outcome] IS NOT NULL)");
                table.CheckConstraint("CK_JobExecutions_ExitCode", "[Outcome] IS NULL OR [ExitCode] IS NOT NULL OR [Outcome] IN ('Cancelled','TimedOut','Blocked','NotRun')");
                table.CheckConstraint("CK_JobExecutions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JobExecutions_Outcome", "[Outcome] IS NULL OR [Outcome] IN ('Succeeded','SucceededWithWarnings','Failed','Cancelled','TimedOut','Blocked','NotRun')");
                table.CheckConstraint("CK_JobExecutions_Start", "[StartedUtc] IS NULL OR [StartedUtc] >= [CreatedUtc]");
                table.ForeignKey(
                    name: "FK_JobExecutions_Jobs_JobId",
                    column: x => x.JobId,
                    principalSchema: "wsr",
                    principalTable: "Jobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_JobExecutions_WorkerNodes_WorkerNodeId",
                    column: x => x.WorkerNodeId,
                    principalSchema: "wsr",
                    principalTable: "WorkerNodes",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "JobParameters",
            schema: "wsr",
            columns: table => new
            {
                JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                SerializedValue = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobParameters", x => new { x.JobId, x.NormalizedName });
                table.CheckConstraint("CK_JobParameters_PresentValue", "LEN(LTRIM(RTRIM([SerializedValue]))) > 0");
                table.ForeignKey(
                    name: "FK_JobParameters_Jobs_JobId",
                    column: x => x.JobId,
                    principalSchema: "wsr",
                    principalTable: "Jobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "JobTargets",
            schema: "wsr",
            columns: table => new
            {
                JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                AddedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                AddedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobTargets", x => new { x.JobId, x.NormalizedName });
                table.ForeignKey(
                    name: "FK_JobTargets_Jobs_JobId",
                    column: x => x.JobId,
                    principalSchema: "wsr",
                    principalTable: "Jobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ScriptParameterAllowedValues",
            schema: "wsr",
            columns: table => new
            {
                ScriptParameterDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NormalizedValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScriptParameterAllowedValues", x => new { x.ScriptParameterDefinitionId, x.NormalizedValue });
                table.ForeignKey(
                    name: "FK_ScriptParameterAllowedValues_ScriptParameterDefinitions_ScriptParameterDefinitionId",
                    column: x => x.ScriptParameterDefinitionId,
                    principalSchema: "wsr",
                    principalTable: "ScriptParameterDefinitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_Actor_OccurredUtc",
            schema: "wsr",
            table: "AuditEvents",
            columns: new[] { "Actor", "OccurredUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_Entity_OccurredUtc",
            schema: "wsr",
            table: "AuditEvents",
            columns: new[] { "EntityType", "EntityId", "OccurredUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_EventType_OccurredUtc",
            schema: "wsr",
            table: "AuditEvents",
            columns: new[] { "EventType", "OccurredUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_OccurredUtc",
            schema: "wsr",
            table: "AuditEvents",
            column: "OccurredUtc");

        migrationBuilder.CreateIndex(
            name: "UX_CredentialReferences_Provider_ExternalHash",
            schema: "wsr",
            table: "CredentialReferences",
            columns: new[] { "NormalizedProviderType", "ExternalIdentifierHash" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_JobApprovals_Job_DecisionUtc",
            schema: "wsr",
            table: "JobApprovals",
            columns: new[] { "JobId", "DecisionUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_JobExecutions_WorkerNodeId",
            schema: "wsr",
            table: "JobExecutions",
            column: "WorkerNodeId");

        migrationBuilder.CreateIndex(
            name: "UX_JobExecutions_Job_Attempt",
            schema: "wsr",
            table: "JobExecutions",
            columns: new[] { "JobId", "AttemptNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_JobExecutions_OneActivePerJob",
            schema: "wsr",
            table: "JobExecutions",
            column: "JobId",
            unique: true,
            filter: "[StartedUtc] IS NOT NULL AND [CompletedUtc] IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Jobs_CreatedUtc",
            schema: "wsr",
            table: "Jobs",
            column: "CreatedUtc");

        migrationBuilder.CreateIndex(
            name: "IX_Jobs_RequestedBy_CreatedUtc",
            schema: "wsr",
            table: "Jobs",
            columns: new[] { "RequestedBy", "CreatedUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Jobs_ScriptDefinitionId_ScriptVersionId",
            schema: "wsr",
            table: "Jobs",
            columns: new[] { "ScriptDefinitionId", "ScriptVersionId" });

        migrationBuilder.CreateIndex(
            name: "IX_Jobs_ScriptVersionId",
            schema: "wsr",
            table: "Jobs",
            column: "ScriptVersionId");

        migrationBuilder.CreateIndex(
            name: "IX_Jobs_Status_UpdatedUtc",
            schema: "wsr",
            table: "Jobs",
            columns: new[] { "Status", "UpdatedUtc" });

        migrationBuilder.CreateIndex(
            name: "UX_ScriptDefinitions_NormalizedName",
            schema: "wsr",
            table: "ScriptDefinitions",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_ScriptParameterDefinitions_Version_NormalizedName",
            schema: "wsr",
            table: "ScriptParameterDefinitions",
            columns: new[] { "ScriptVersionId", "NormalizedName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_ScriptVersions_Definition_SemanticVersion",
            schema: "wsr",
            table: "ScriptVersions",
            columns: new[] { "ScriptDefinitionId", "Major", "Minor", "Patch" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WorkerNodes_IsEnabled",
            schema: "wsr",
            table: "WorkerNodes",
            column: "IsEnabled");

        migrationBuilder.CreateIndex(
            name: "IX_WorkerNodes_LastHeartbeatUtc",
            schema: "wsr",
            table: "WorkerNodes",
            column: "LastHeartbeatUtc");

        migrationBuilder.CreateIndex(
            name: "UX_WorkerNodes_NormalizedName",
            schema: "wsr",
            table: "WorkerNodes",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.Sql(
            @"EXECUTE(N'
CREATE TRIGGER [wsr].[TR_ScriptVersions_RequireDryRunForPublishedExecute]
ON [wsr].[ScriptVersions]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        WHERE i.[IsPublished] = 1
          AND EXISTS
          (
              SELECT 1
              FROM [wsr].[ScriptVersionPhases] AS executePhase
              WHERE executePhase.[ScriptVersionId] = i.[Id]
                AND executePhase.[Phase] = N''Execute''
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM [wsr].[ScriptVersionPhases] AS dryRunPhase
              WHERE dryRunPhase.[ScriptVersionId] = i.[Id]
                AND dryRunPhase.[Phase] = N''DryRun''
          )
    )
    BEGIN
        THROW 51000, ''Published Execute-capable versions must also support DryRun.'', 1;
    END
END
')");

        migrationBuilder.Sql(
            @"EXECUTE(N'
CREATE TRIGGER [wsr].[TR_ScriptVersionPhases_RequireDryRunForPublishedExecute]
ON [wsr].[ScriptVersionPhases]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            SELECT [ScriptVersionId] FROM inserted
            UNION
            SELECT [ScriptVersionId] FROM deleted
        ) AS changed
        INNER JOIN [wsr].[ScriptVersions] AS version
            ON version.[Id] = changed.[ScriptVersionId]
        WHERE version.[IsPublished] = 1
          AND EXISTS
          (
              SELECT 1
              FROM [wsr].[ScriptVersionPhases] AS executePhase
              WHERE executePhase.[ScriptVersionId] = version.[Id]
                AND executePhase.[Phase] = N''Execute''
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM [wsr].[ScriptVersionPhases] AS dryRunPhase
              WHERE dryRunPhase.[ScriptVersionId] = version.[Id]
                AND dryRunPhase.[Phase] = N''DryRun''
          )
    )
    BEGIN
        THROW 51000, ''Published Execute-capable versions must also support DryRun.'', 1;
    END
END
')");

        migrationBuilder.Sql(
            @"EXECUTE(N'
CREATE TRIGGER [wsr].[TR_ScriptParameterAllowedValues_RequireEnum]
ON [wsr].[ScriptParameterAllowedValues]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN [wsr].[ScriptParameterDefinitions] AS definition
            ON definition.[Id] = i.[ScriptParameterDefinitionId]
        WHERE definition.[ParameterType] <> N''Enum''
    )
    BEGIN
        THROW 51001, ''Allowed values may only be stored for Enum parameters.'', 1;
    END
END
')");

        migrationBuilder.Sql(
            @"EXECUTE(N'
CREATE TRIGGER [wsr].[TR_ScriptParameterDefinitions_ProtectAllowedValues]
ON [wsr].[ScriptParameterDefinitions]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        WHERE i.[ParameterType] <> N''Enum''
          AND EXISTS
          (
              SELECT 1
              FROM [wsr].[ScriptParameterAllowedValues] AS allowedValue
              WHERE allowedValue.[ScriptParameterDefinitionId] = i.[Id]
          )
    )
    BEGIN
        THROW 51001, ''Allowed values may only be stored for Enum parameters.'', 1;
    END
END
')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AuditEventProperties",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "CredentialReferences",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "JobApprovals",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "JobExecutions",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "JobParameters",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "JobTargets",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "ScriptParameterAllowedValues",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "ScriptVersionPhases",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "ScriptVersionReportFormats",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "WorkerCapabilities",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "AuditEvents",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "Jobs",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "ScriptParameterDefinitions",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "WorkerNodes",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "ScriptVersions",
            schema: "wsr");

        migrationBuilder.DropTable(
            name: "ScriptDefinitions",
            schema: "wsr");
    }
}
