using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using DriverMasterPro.Models;
using DriverMasterPro.Services;
using DriverMasterPro.Services.Abstractions;
using Xunit;

namespace DriverMasterPro.Tests;

[CollectionDefinition("WPF UI tests", DisableParallelization = true)]
public sealed class WpfUiTestCollection
{
}

[Collection("WPF UI tests")]
public sealed class CacheHistoryUiTests
{
    [Fact]
    public async Task Startup_LoadsLatestSnapshotAsStaleCachedInventoryWithoutMergingObservations()
    {
        var older = CreateSnapshot(10, "2026-11-08T10:00:00.0000000Z",
            CreateDevice("Older device", "DUPLICATE_ID"));
        var latest = CreateSnapshot(11, "2026-11-09T10:00:00.0000000Z",
            CreateDevice("First duplicate", "DUPLICATE_ID"),
            CreateDevice("Second duplicate", "DUPLICATE_ID"),
            CreateDevice("Unidentified", ""));
        var database = new StubDatabaseService(new[] { latest, older });

        await RunOnStaThreadAsync(async () =>
        {
            var window = CreateWindow(database, Array.Empty<DeviceInfo>());
            window.Show();
            await WaitForAsync(() => database.GetLatestCallCount == 1 && database.GetHistoryCallCount == 1);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var state = Assert.IsType<TextBlock>(window.FindName("InventoryStateText"));
            var history = Assert.IsType<ComboBox>(window.FindName("SnapshotHistoryBox"));
            var grid = Assert.IsType<DataGrid>(window.FindName("DevicesGrid"));

            Assert.Contains("Zwischengespeicherter Bestand", state.Text);
            Assert.Contains("veraltet", state.Text);
            Assert.Equal(latest.ScanId, Assert.IsType<long>(history.SelectedValue));
            Assert.Equal(2, history.Items.Count);
            Assert.Equal(3, grid.Items.Count);
            Assert.Equal(
                new[] { "DUPLICATE_ID", "DUPLICATE_ID", "" },
                grid.Items.Cast<DeviceInfo>().Select(device => device.DeviceId));
            Assert.Equal(0, database.GetCachedCallCount);
            window.Close();
        });
    }

    [Fact]
    public async Task Startup_UsesLegacyCacheWithoutInventingSnapshotHistory()
    {
        var legacyDevice = CreateDevice("Legacy cached device", "LEGACY_ID");
        var database = new StubDatabaseService(Array.Empty<ScanSnapshot>(), new[] { legacyDevice });

        await RunOnStaThreadAsync(async () =>
        {
            var window = CreateWindow(database, Array.Empty<DeviceInfo>());
            window.Show();
            await WaitForAsync(() => database.GetCachedCallCount == 1);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var state = Assert.IsType<TextBlock>(window.FindName("InventoryStateText"));
            var history = Assert.IsType<ComboBox>(window.FindName("SnapshotHistoryBox"));
            var grid = Assert.IsType<DataGrid>(window.FindName("DevicesGrid"));

            Assert.Contains("Älterer zwischengespeicherter Bestand", state.Text);
            Assert.Contains("Zeitstempel nicht verfügbar", state.Text);
            Assert.Empty(history.Items);
            Assert.False(history.IsEnabled);
            Assert.Single(grid.Items);
            Assert.Equal(1, database.GetHistoryCallCount);
            window.Close();
        });
    }

    [Fact]
    public async Task Startup_DisablesScanningUntilCacheLoadingCompletes()
    {
        var initialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var database = new StubDatabaseService(
            Array.Empty<ScanSnapshot>(),
            initializationTask: initialization.Task);
        var scanner = new StubDeviceScanner(Array.Empty<DeviceInfo>());

        await RunOnStaThreadAsync(async () =>
        {
            var window = new MainWindow(scanner, database, new RuleBasedDiagnosticsService(), new InMemorySettingsService());
            window.Show();
            await WaitForAsync(() => database.InitializeCallCount == 1);

            var scanButton = Assert.IsType<Button>(window.FindName("ScanButton"));
            Assert.False(scanButton.IsEnabled);
            scanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, scanButton));
            Assert.Equal(0, scanner.ScanCallCount);
            Assert.Equal(0, database.SaveCallCount);

            initialization.SetResult();
            await WaitForAsync(() => database.GetCachedCallCount == 1 && scanButton.IsEnabled);
            Assert.Equal(0, scanner.ScanCallCount);
            window.Close();
        });
    }

    [Fact]
    public async Task StartupFailure_RestoresScanningAndReportsDatabaseError()
    {
        var database = new StubDatabaseService(
            Array.Empty<ScanSnapshot>(),
            initializationTask: Task.FromException(new InvalidOperationException("startup failure")));

        await RunOnStaThreadAsync(async () =>
        {
            var window = CreateWindow(database, Array.Empty<DeviceInfo>());
            window.Show();
            var scanButton = Assert.IsType<Button>(window.FindName("ScanButton"));
            await WaitForAsync(() => database.InitializeCallCount == 1 && scanButton.IsEnabled);

            var status = Assert.IsType<TextBlock>(window.FindName("StatusText"));
            Assert.Contains("startup failure", status.Text, StringComparison.Ordinal);
            Assert.True(scanButton.IsEnabled);
            window.Close();
        });
    }

    [Fact]
    public async Task SelectingOlderSnapshotDisplaysOnlyItsOrderedObservations()
    {
        var older = CreateSnapshot(20, "2026-11-07T10:00:00.0000000Z",
            CreateDevice("Historical duplicate A", "REUSED_ID"),
            CreateDevice("Historical duplicate B", "REUSED_ID"),
            CreateDevice("Historical unidentified", ""));
        var latest = CreateSnapshot(21, "2026-11-09T10:00:00.0000000Z",
            CreateDevice("Current device", "CURRENT_ID"));
        var database = new StubDatabaseService(new[] { latest, older });

        await RunOnStaThreadAsync(async () =>
        {
            var window = CreateWindow(database, Array.Empty<DeviceInfo>());
            window.Show();
            await WaitForAsync(() => database.GetLatestCallCount == 1 && database.GetHistoryCallCount == 1);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var history = Assert.IsType<ComboBox>(window.FindName("SnapshotHistoryBox"));
            history.SelectedValue = older.ScanId;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var state = Assert.IsType<TextBlock>(window.FindName("InventoryStateText"));
            var grid = Assert.IsType<DataGrid>(window.FindName("DevicesGrid"));
            Assert.Equal(older.ScanId, Assert.IsType<long>(history.SelectedValue));
            Assert.Contains("Historischer Snapshot", state.Text);
            Assert.Equal(
                new[] { "Historical duplicate A", "Historical duplicate B", "Historical unidentified" },
                grid.Items.Cast<DeviceInfo>().Select(device => device.Name));
            Assert.Equal(
                new[] { "REUSED_ID", "REUSED_ID", "" },
                grid.Items.Cast<DeviceInfo>().Select(device => device.DeviceId));
            Assert.Equal(1, database.GetLatestCallCount);
            Assert.Equal(1, database.GetHistoryCallCount);
            window.Close();
        });
    }

    [Fact]
    public async Task SuccessfulScanRefreshesHistoryAndMarksPersistedSnapshotFresh()
    {
        var prior = CreateSnapshot(30, "2026-11-08T10:00:00.0000000Z",
            CreateDevice("Prior device", "PRIOR_ID"));
        var scanDevices = new[]
        {
            CreateDevice("Repeated observation A", "SAME_ID"),
            CreateDevice("Repeated observation B", "SAME_ID"),
            CreateDevice("Missing identifier", "")
        };
        var database = new StubDatabaseService(new[] { prior });

        await RunOnStaThreadAsync(async () =>
        {
            var window = CreateWindow(database, scanDevices);
            window.Show();
            await WaitForAsync(() => database.GetLatestCallCount == 1 && database.GetHistoryCallCount == 1);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var scanButton = Assert.IsType<Button>(window.FindName("ScanButton"));
            scanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, scanButton));
            await WaitForAsync(() => database.SaveCallCount == 1 && database.GetHistoryCallCount == 2);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var state = Assert.IsType<TextBlock>(window.FindName("InventoryStateText"));
            var history = Assert.IsType<ComboBox>(window.FindName("SnapshotHistoryBox"));
            var grid = Assert.IsType<DataGrid>(window.FindName("DevicesGrid"));
            var saved = Assert.IsType<ScanSnapshot>(await database.GetLatestScanAsync());

            Assert.Contains("Frischer Scan", state.Text);
            Assert.Equal(saved.ScanId, Assert.IsType<long>(history.SelectedValue));
            Assert.Equal(2, history.Items.Count);
            Assert.Equal(3, grid.Items.Count);
            Assert.Equal(
                new[] { "SAME_ID", "SAME_ID", "" },
                grid.Items.Cast<DeviceInfo>().Select(device => device.DeviceId));
            Assert.Equal(scanDevices.Length, saved.DeviceCount);
            window.Close();
        });
    }

    [Fact]
    public async Task LanguageSelection_UpdatesNewHistoryAndStateLabelsInAllFiveLanguages()
    {
        var latest = CreateSnapshot(40, "2026-11-09T10:00:00.0000000Z",
            CreateDevice("Cached device", "CACHED_ID"));
        var database = new StubDatabaseService(new[] { latest });
        var expectedHistoryLabels = new[]
        {
            "Scan-Verlauf:",
            "سجل الفحوصات:",
            "Scan history:",
            "Historique des analyses :",
            "Historial de análisis:"
        };
        var expectedStateStarts = new[]
        {
            "Zwischengespeicherter Bestand",
            "بيانات مخزنة مؤقتاً",
            "Cached inventory",
            "Inventaire en cache",
            "Inventario en caché"
        };

        await RunOnStaThreadAsync(async () =>
        {
            var window = CreateWindow(database, Array.Empty<DeviceInfo>());
            window.Show();
            await WaitForAsync(() => database.GetLatestCallCount == 1 && database.GetHistoryCallCount == 1);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var language = Assert.IsType<ComboBox>(window.FindName("LanguageBox"));
            var historyLabel = Assert.IsType<TextBlock>(window.FindName("HistoryLabel"));
            var state = Assert.IsType<TextBlock>(window.FindName("InventoryStateText"));
            var history = Assert.IsType<ComboBox>(window.FindName("SnapshotHistoryBox"));
            Assert.Same(historyLabel, AutomationProperties.GetLabeledBy(history));
            language.SelectedIndex = 2;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            for (int index = 0; index < expectedHistoryLabels.Length; index++)
            {
                language.SelectedIndex = index;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(expectedHistoryLabels[index], historyLabel.Text);
                Assert.StartsWith(expectedStateStarts[index], state.Text, StringComparison.Ordinal);
                Assert.Equal(
                    index == 1 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    window.FlowDirection);
            }

            window.Close();
        });
    }

    private static MainWindow CreateWindow(StubDatabaseService database, IEnumerable<DeviceInfo> scanDevices) =>
        new(new StubDeviceScanner(scanDevices), database, new RuleBasedDiagnosticsService(), new InMemorySettingsService());

    private static ScanSnapshot CreateSnapshot(long scanId, string scanTimeUtc, params DeviceInfo[] devices) =>
        new(scanId, scanTimeUtc, devices.Length, devices);

    private static DeviceInfo CreateDevice(string name, string deviceId) =>
        new()
        {
            Name = name,
            DeviceId = deviceId,
            Manufacturer = "Test",
            Status = "OK",
            ProblemCode = 0
        };

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
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StubDeviceScanner(IEnumerable<DeviceInfo> devices) : IDeviceScanner
    {
        public int ScanCallCount { get; private set; }

        public Task<List<DeviceInfo>> ScanAsync(CancellationToken cancellationToken = default)
        {
            ScanCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(devices.ToList());
        }
    }

    private sealed class StubDatabaseService : IDatabaseService
    {
        private readonly List<ScanSnapshot> _snapshots;
        private readonly List<DeviceInfo> _legacyDevices;
        private readonly Task _initializationTask;
        private long _nextScanId;

        public int InitializeCallCount { get; private set; }
        public int GetLatestCallCount { get; private set; }
        public int GetHistoryCallCount { get; private set; }
        public int GetCachedCallCount { get; private set; }
        public int SaveCallCount { get; private set; }
        public string DatabasePath => "test database";

        public StubDatabaseService(
            IEnumerable<ScanSnapshot> snapshots,
            IEnumerable<DeviceInfo>? legacyDevices = null,
            Task? initializationTask = null)
        {
            _snapshots = snapshots.OrderByDescending(snapshot => snapshot.ScanId).ToList();
            _legacyDevices = legacyDevices?.ToList() ?? new List<DeviceInfo>();
            _initializationTask = initializationTask ?? Task.CompletedTask;
            _nextScanId = _snapshots.Count == 0 ? 1 : _snapshots.Max(snapshot => snapshot.ScanId) + 1;
        }

        public Task InitializeAsync()
        {
            InitializeCallCount++;
            return _initializationTask;
        }

        public Task SaveScanAsync(IReadOnlyCollection<DeviceInfo> devices, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            var snapshot = new ScanSnapshot(
                _nextScanId++,
                $"2026-11-09T12:00:{SaveCallCount:00}.0000000Z",
                devices.Count,
                devices.ToList());
            _snapshots.Insert(0, snapshot);
            return Task.CompletedTask;
        }

        public Task<ScanSnapshot?> GetLatestScanAsync()
        {
            GetLatestCallCount++;
            return Task.FromResult(_snapshots.FirstOrDefault());
        }

        public Task<List<ScanSnapshot>> GetScanHistoryAsync()
        {
            GetHistoryCallCount++;
            return Task.FromResult(_snapshots.ToList());
        }

        public Task<List<DeviceInfo>> GetCachedDevicesAsync()
        {
            GetCachedCallCount++;
            return Task.FromResult(_legacyDevices.ToList());
        }

        public Task<List<DeviceInfo>> GetActiveDevicesAsync(string scanTimeUtc) =>
            Task.FromResult(new List<DeviceInfo>());

        public Task<List<DeviceInfo>> GetHistoricalDevicesAsync(string currentScanTimeUtc) =>
            Task.FromResult(new List<DeviceInfo>());
    }
}
