using SelfGuardService;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "SelfGuardBlockerService");
builder.Logging.AddEventLog(s => { s.SourceName = "SelfGuard"; s.LogName = "Application"; });
builder.Services.AddHostedService<BlockerWorker>();
builder.Build().Run();
