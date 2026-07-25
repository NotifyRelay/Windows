using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotifyRelay.Worker.Bridge;
using NotifyRelay.Worker.Configuration;
using NotifyRelay.Worker.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<WorkerConfiguration>();
        services.AddSingleton<ServiceHost>();
        services.AddSingleton<PipeServer>();
        services.AddHostedService<WorkerService>();
    })
    .Build();

await host.RunAsync();
