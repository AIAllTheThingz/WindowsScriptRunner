using WindowsScriptRunner.Application;
using WindowsScriptRunner.Automation;
using WindowsScriptRunner.Infrastructure;
using WindowsScriptRunner.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "WindowsScriptRunner Worker";
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProductionAutomation(builder.Configuration);
builder.Services.AddWorkerServices(builder.Configuration);

var host = builder.Build();
host.Run();
