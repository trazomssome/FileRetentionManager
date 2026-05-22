using System.IO;
using System.Windows;
using FileRetentionManager.App.Services;
using FileRetentionManager.App.ViewModels;
using FileRetentionManager.Domain.Rules;
using FileRetentionManager.Domain.Services;
using FileRetentionManager.Infrastructure.WPF.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FileRetentionManager.App;

public partial class App : Application
{
    private IHost? host;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog((context, services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .Enrich.FromLogContext()
                    .WriteTo.Console()
                    .WriteTo.File(
                        Path.Combine(AppContext.BaseDirectory, "logs", "file-retention-.log"),
                        rollingInterval: RollingInterval.Day);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<IFileSystemService, PhysicalFileSystemService>();
                services.AddSingleton<IUserDecisionService, WpfUserDecisionService>();
                services.AddSingleton<ITargetPathPickerService, WpfTargetPathPickerService>();
                services.AddSingleton<IRetentionRule>(CompositeRetentionRule.Default);
                services.AddSingleton<IReportGenerator>(provider =>
                    new MarkdownReportGenerator(
                        provider.GetRequiredService<IFileSystemService>(),
                        Path.Combine(AppContext.BaseDirectory, "reports"),
                        provider.GetRequiredService<ILogger<MarkdownReportGenerator>>()));
                services.AddSingleton<IRetentionSequenceService, RetentionSequenceService>();
                services.AddSingleton<IRetentionScheduleService, RetentionScheduleService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await host.StartAsync();

        var mainWindow = host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        if (host is not null)
        {
            await host.StopAsync(TimeSpan.FromSeconds(5));

            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }

        Log.CloseAndFlush();
    }
}
