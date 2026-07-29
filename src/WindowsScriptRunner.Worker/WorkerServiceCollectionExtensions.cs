using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.Worker;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services
            .AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();
        services.AddSingleton<WorkerIdentity>();
        services.AddSingleton<WorkerRuntimeState>();
        services.AddSingleton<WorkerMetrics>();
        services.AddSingleton<IWorkerDelay, SystemWorkerDelay>();
        services.AddSingleton<IWorkerRandom, SystemWorkerRandom>();
        services.AddSingleton<JobWorkHandlerRegistry>();
        services.AddHostedService<WorkerRegistrationHostedService>();
        services.AddHostedService<WorkerHeartbeatService>();
        services.AddHostedService<JobQueueWorker>();
        services.AddHostedService<ExpiredLeaseRecoveryService>();
        return services;
    }
}
