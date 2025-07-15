using OBridge.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
	options.SingleLine = true;
	options.TimestampFormat = "[HH:mm:ss] ";
	options.IncludeScopes = false;
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
