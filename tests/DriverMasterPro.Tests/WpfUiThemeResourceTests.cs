using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DriverMasterPro.Models;
using DriverMasterPro.Services.Abstractions;
using Xunit;

namespace DriverMasterPro.Tests;

[Collection("WPF UI tests")]
public sealed class WpfUiThemeResourceTests
{
    private const string ThemeDictionaryUri =
        "pack://application:,,,/DriverMasterPro;component/Themes/DriverMasterProTheme.xaml";

    [Fact]
    public async Task ThemeDictionary_LoadsOnStaThreadWithoutApplicationInstance()
    {
        await RunOnStaThreadAsync(() =>
        {
            Assert.Null(Application.Current);

            var dictionary = new ResourceDictionary { Source = new Uri(ThemeDictionaryUri) };

            Assert.True(dictionary.MergedDictionaries.Count >= 1,
                $"expected the WPF-UI theme dictionary, found {dictionary.MergedDictionaries.Count}");
            Assert.NotEmpty(dictionary.Keys.Cast<object>());

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ThemeDictionary_ResolvesWpfUiPaletteAndApplicationTokens()
    {
        await RunOnStaThreadAsync(() =>
        {
            Assert.Null(Application.Current);

            var dictionary = new ResourceDictionary { Source = new Uri(ThemeDictionaryUri) };

            foreach (var key in new[] { "ApplicationBackgroundBrush", "TextFillColorPrimaryBrush", "AccentBrush", "Accent", "Muted", "Glass" })
            {
                Assert.True(dictionary.Contains(key), $"missing theme key '{key}'. Present theme keys: {Describe(dictionary)}");
                Assert.NotNull(dictionary[key]);
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ThemeDictionary_DoesNotAdoptControlStylesInPhaseOne()
    {
        await RunOnStaThreadAsync(() =>
        {
            Assert.Null(Application.Current);

            var dictionary = new ResourceDictionary { Source = new Uri(ThemeDictionaryUri) };

            // Phase 1 scope guard: ui:ControlsDictionary must stay out until Phase 2,
            // because its implicit styles changed row metrics by +6..+8 px.
            Assert.False(dictionary.Contains(typeof(Button)),
                "Phase 1 must not merge WPF-UI control styles; an implicit Button style was found.");
            Assert.False(dictionary.Contains(typeof(DataGrid)),
                "Phase 1 must not merge WPF-UI control styles; an implicit DataGrid style was found.");

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task MainWindow_StillRendersWhenApplicationResourcesAreAbsent()
    {
        await RunOnStaThreadAsync(async () =>
        {
            Assert.Null(Application.Current);

            var window = new MainWindow(
                new StubScanner(Array.Empty<Models.DeviceInfo>()),
                new StubDatabase(),
                new Services.RuleBasedDiagnosticsService());
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var background = Assert.IsType<SolidColorBrush>(window.Background);
            Assert.Equal(Color.FromRgb(0x08, 0x0D, 0x14), background.Color);
            Assert.IsType<DataGrid>(window.FindName("DevicesGrid"));
            window.Close();
        });
    }

    private static string Describe(ResourceDictionary dictionary)
    {
        var keys = dictionary.Keys.Cast<object>()
            .Select(key => key is Type type ? type.Name : key.ToString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Take(15);
        return string.Join(", ", keys);
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

    private sealed class StubScanner : IDeviceScanner
    {
        private readonly List<DeviceInfo> _devices;

        public StubScanner(IReadOnlyList<DeviceInfo> devices) => _devices = devices.ToList();

        public Task<List<DeviceInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_devices);
    }

    private sealed class StubDatabase : IDatabaseService
    {
        public string DatabasePath => ":memory:";

        public Task InitializeAsync() => Task.CompletedTask;

        public Task SaveScanAsync(IReadOnlyCollection<DeviceInfo> devices, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ScanSnapshot?> GetLatestScanAsync() =>
            Task.FromResult<ScanSnapshot?>(null);

        public Task<List<ScanSnapshot>> GetScanHistoryAsync() =>
            Task.FromResult(new List<ScanSnapshot>());

        public Task<List<DeviceInfo>> GetCachedDevicesAsync() =>
            Task.FromResult(new List<DeviceInfo>());

        public Task<List<DeviceInfo>> GetActiveDevicesAsync(string scanTimeUtc) =>
            Task.FromResult(new List<DeviceInfo>());

        public Task<List<DeviceInfo>> GetHistoricalDevicesAsync(string currentScanTimeUtc) =>
            Task.FromResult(new List<DeviceInfo>());
    }
}
