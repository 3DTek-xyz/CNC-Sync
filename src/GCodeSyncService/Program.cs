using GCodeSyncService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;

var logger = LogManager.GetCurrentClassLogger();

try
{
    logger.Info("Starting G-Code Sync Service application");

    var builder = Host.CreateDefaultBuilder(args)
        .UseWindowsService(options =>
        {
            options.ServiceName = "GCodeSyncService";
        })
        .ConfigureServices(services =>
        {
            services.AddHostedService<GCodeSyncWorkerService>();
        })
        .ConfigureLogging((context, logging) =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
            logging.AddNLog();
        });

    var host = builder.Build();
    
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.Error(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    LogManager.Shutdown();
}