using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Application.Reports;
using WindowsScriptRunner.Application.Time;
using WindowsScriptRunner.Application.Workers;

namespace WindowsScriptRunner.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IClock, SystemClock>();
        services.AddTransient<GetJobHandler>();
        services.AddTransient<ListJobAuthorizationResourcesHandler>();
        services.AddTransient<ListAwaitingApprovalJobsHandler>();
        services.AddTransient<RegisterWorkerHandler>();
        services.AddTransient<RecordWorkerHeartbeatHandler>();
        services.AddTransient<AcquireJobLeaseHandler>();
        services.AddTransient<RenewJobLeaseHandler>();
        services.AddTransient<ReleaseUnstartedJobLeaseHandler>();
        services.AddTransient<RecoverExpiredJobLeaseHandler>();
        services.AddTransient<InspectJobLeaseHandler>();
        services.AddTransient<StartLeasedDryRunHandler>();
        services.AddTransient<CompleteLeasedDryRunHandler>();
        services.AddTransient<CompleteLeasedReadOnlyDryRunHandler>();
        services.AddTransient<TerminateLeasedDryRunHandler>();
        services.AddTransient<StartLeasedExecutionHandler>();
        services.AddTransient<BeginLeasedPostValidationHandler>();
        services.AddTransient<RecordLeasedExecutionOutcomeHandler>();
        services.AddTransient<CompleteLocalHostInventoryDryRunHandler>();
        services.AddTransient<GetLocalHostInventoryReportHandler>();
        services.AddTransient<ListLocalHostInventoryReportsHandler>();
        return services;
    }

    public static IServiceCollection AddWebPortalApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IJobFingerprintService, ApprovalFingerprintService>();
        services.AddTransient<ApproveJobHandler>();
        services.AddTransient<RejectJobHandler>();
        services.AddTransient<GetApprovalReviewHandler>();
        return services;
    }
}
