using Microsoft.Extensions.Options;
using WindowsScriptRunner.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();
builder.Services.AddHostedService<HeartbeatWorker>();

var host = builder.Build();
host.Run();
