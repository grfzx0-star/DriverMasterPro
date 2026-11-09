using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DriverMasterPro.Services;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro;

public partial class App : Application
{
    private static IHost? _host;
    public static IHost AppHost => _host ?? throw new InvalidOperationException("App host is not initialized.");

    public static IHostBuilder CreateHostBuilder(string[]? args = null) =>
        Host.CreateDefaultBuilder(args ?? Array.Empty<string>())
            .ConfigureServices((_, services) =>
            {
                // Register Core Services with appropriate lifetimes
                services.AddSingleton<IDatabaseService, DatabaseService>();
                services.AddTransient<IDeviceInventoryProvider, WmiDeviceInventoryProvider>();
                services.AddTransient<IDeviceScanner, DeviceScanner>();

                // Register Views
                services.AddSingleton<MainWindow>();
            });

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Trace.TraceError($"[App] Unhandled UI Exception: {args.Exception}");
        };

        try
        {
            _host = CreateHostBuilder(e.Args).Build();
            await _host.StartAsync();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[App] Startup failure: {ex}");
            MessageBox.Show($"Kritischer Anwendungsfehler beim Start:\n{ex.Message}", "Driver Master Pro", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[App] Error stopping host: {ex.Message}");
            }
            finally
            {
                _host.Dispose();
                _host = null;
            }
        }

        base.OnExit(e);
    }
}
