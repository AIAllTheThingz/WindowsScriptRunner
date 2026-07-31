using WindowsScriptRunner.Contracts.Reports;

namespace WindowsScriptRunner.Web.Pages.Reports.LocalHostInventory;

public sealed record LocalHostInventoryReportView(
    Guid ReportId,
    Guid JobId,
    string PackageId,
    string PackageVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset CollectedUtc,
    string ComputerName,
    string OsDescription,
    string OsVersion,
    string OsArchitecture,
    string PowerShellVersion)
{
    public static LocalHostInventoryReportView FromResponse(LocalHostInventoryReportResponse response) =>
        new(
            response.ReportId,
            response.JobId,
            response.PackageId,
            response.PackageVersion,
            response.CreatedUtc,
            response.CollectedUtc,
            response.ComputerName,
            response.OsDescription,
            response.OsVersion,
            response.OsArchitecture,
            response.PowerShellVersion);
}
