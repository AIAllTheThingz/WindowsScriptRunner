using WindowsScriptRunner.Application;
using WindowsScriptRunner.Infrastructure;
using WindowsScriptRunner.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWorkerServices(builder.Configuration);

var host = builder.Build();
host.Run();
