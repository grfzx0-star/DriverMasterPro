using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using DriverMasterPro.Models;
using DriverMasterPro.Services;
using DriverMasterPro.Services.Abstractions;
using Xunit;

namespace DriverMasterPro.Tests;

/// <summary>
/// Bug 4 guard: the version shown in the UI and reported by the assembly must come from
/// a single source (&lt;Version&gt; in DriverMasterPro.csproj), never from duplicated literals.
/// </summary>
public sealed class AppInfoTests
{
    [Fact]
    public void Version_MatchesAssemblyVersionAndIsNotTheSdkDefault()
    {
        var assemblyVersion = typeof(AppInfo).Assembly.GetName().Version;

        Assert.NotNull(assemblyVersion);
        Assert.Equal($"{assemblyVersion!.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}", AppInfo.Version);

        // The SDK default is 1.0.0.0; seeing it means <Version> was dropped from the csproj.
        // Update the expected floor when the project deliberately ships 1.0.0.
        Assert.NotEqual("1.0.0", AppInfo.Version);
        Assert.NotEqual("0.0.0", AppInfo.Version);
    }

    [Fact]
    public void Format_ReplacesEveryVersionPlaceholder()
    {
        var formatted = AppInfo.Format("Version {version} — Read-only MVP ({version})");

        Assert.Equal($"Version {AppInfo.Version} — Read-only MVP ({AppInfo.Version})", formatted);
        Assert.DoesNotContain("{version}", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_LeavesTemplateWithoutPlaceholderUnchanged()
    {
        Assert.Equal("Suche:", AppInfo.Format("Suche:"));
    }

    [Fact]
    public void Format_RejectsNullTemplate()
    {
        Assert.Throws<ArgumentNullException>(() => AppInfo.Format(null!));
    }
}

[Collection("WPF UI tests")]
public sealed class VersionLabelUiTests
{
    [Fact]
    public async Task Startup_ShowsVersionFromAssemblyInDefaultGermanLabel()
    {
        await RunOnStaThreadAsync(() =>
        {
            var window = new MainWindow(new EmptyDeviceScanner(), new EmptyDatabaseService(), new RuleBasedDiagnosticsService());
            var label = Assert.IsType<TextBlock>(window.FindName("VersionLabel"));

            Assert.Equal($"Version {AppInfo.Version} — Nur-Lese-MVP", label.Text);
            window.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SwitchingLanguage_RefreshesVersionLabelFromSameTemplates()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var window = new MainWindow(new EmptyDeviceScanner(), new EmptyDatabaseService(), new RuleBasedDiagnosticsService());
            window.Show();
            await WaitForAsync(() => window.IsLoaded);

            var languageBox = Assert.IsType<ComboBox>(window.FindName("LanguageBox"));
            var label = Assert.IsType<TextBlock>(window.FindName("VersionLabel"));

            languageBox.SelectedIndex = 2; // English
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal($"Version {AppInfo.Version} — Read-only MVP", label.Text);

            languageBox.SelectedIndex = 4; // Spanish, different prefix (Versión)
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal($"Versión {AppInfo.Version} — Solo lectura", label.Text);

            window.Close();
        });
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task RunOnStaThreadAsync(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await test();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            }));

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class EmptyDeviceScanner : IDeviceScanner
    {
        public Task<List<DeviceInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<DeviceInfo>());
    }

    private sealed class EmptyDatabaseService : IDatabaseService
    {
        public string DatabasePath => ":memory:";

        public Task InitializeAsync() => Task.CompletedTask;

        public Task SaveScanAsync(IReadOnlyCollection<DeviceInfo> devices, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ScanSnapshot?> GetLatestScanAsync() => Task.FromResult<ScanSnapshot?>(null);

        public Task<List<ScanSnapshot>> GetScanHistoryAsync() => Task.FromResult(new List<ScanSnapshot>());

        public Task<List<DeviceInfo>> GetCachedDevicesAsync() => Task.FromResult(new List<DeviceInfo>());

        public Task<List<DeviceInfo>> GetActiveDevicesAsync(string scanTimeUtc) => Task.FromResult(new List<DeviceInfo>());

        public Task<List<DeviceInfo>> GetHistoricalDevicesAsync(string currentScanTimeUtc) => Task.FromResult(new List<DeviceInfo>());
    }
}
