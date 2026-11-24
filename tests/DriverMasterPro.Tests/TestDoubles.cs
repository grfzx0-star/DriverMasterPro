using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DriverMasterPro.Models;
using DriverMasterPro.Services;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro.Tests;

/// <summary>Scanner stub returning an empty inventory; no WMI access in tests.</summary>
internal sealed class EmptyDeviceScanner : IDeviceScanner
{
    public Task<List<DeviceInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<DeviceInfo>());
}

/// <summary>Database stub that stores nothing, so startup loads produce an empty inventory.</summary>
internal sealed class EmptyDatabaseService : IDatabaseService
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

/// <summary>
/// In-memory settings store. Tests must never use <see cref="JsonSettingsService"/>'s default
/// path: that resolves to the real %LOCALAPPDATA% file, so one test that switches language
/// leaks into every later test in the run (and into the developer's own settings).
/// </summary>
internal sealed class InMemorySettingsService : ISettingsService
{
    public AppSettings Current { get; private set; }

    public int SaveCount { get; private set; }

    public InMemorySettingsService(int languageIndex = 0)
    {
        Current = new AppSettings { LanguageIndex = languageIndex };
    }

    public AppSettings Load() => Current;

    public void Save(AppSettings settings)
    {
        Current = settings;
        SaveCount++;
    }
}

/// <summary>
/// Records calls so a test can prove the container-injected instance actually reached the
/// consumer, which singleton identity alone cannot show.
/// </summary>
internal sealed class SpySettingsService : ISettingsService
{
    public AppSettings Current { get; private set; } = new();

    public int LoadCallCount { get; private set; }

    public int SaveCallCount { get; private set; }

    public AppSettings Load()
    {
        LoadCallCount++;
        return Current;
    }

    public void Save(AppSettings settings)
    {
        Current = settings;
        SaveCallCount++;
    }
}

/// <summary>
/// Runs a test body on a dedicated STA thread with a live Dispatcher, which WPF windows
/// require. The xUnit package in use has no STA attribute, so this replaces one.
/// </summary>
internal static class StaRunner
{
    public static async Task RunAsync(Func<Task> test)
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

    public static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
