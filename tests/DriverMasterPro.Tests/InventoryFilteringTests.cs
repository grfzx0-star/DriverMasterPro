using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using DriverMasterPro;
using DriverMasterPro.Models;
using DriverMasterPro.Services.Abstractions;
using DriverMasterPro.Services.Localization;
using DriverMasterPro.ViewModels;
using Xunit;

namespace DriverMasterPro.Tests;

/// <summary>
/// Diagnostics stand-in with recognisable summary and evidence text, so the two derived
/// search fields can be exercised without depending on the rule table.
/// </summary>
internal sealed class FixedDiagnosticsService : IDiagnosticsService
{
    public DiagnosticReport Resolve(uint? problemCode, bool isArabic = false, string? deviceStatus = null) =>
        Build("device");

    public void Enrich(DeviceInfo device, bool isArabic = false) => device.Diagnostics = Build(device.Name);

    public void EnrichAll(IEnumerable<DeviceInfo> devices, bool isArabic = false)
    {
        foreach (var device in devices)
            Enrich(device);
    }

    private static DiagnosticReport Build(string name) => new(
        null,
        "",
        DiagnosticOutcome.Healthy,
        ProblemSeverity.Normal,
        "OK",
        "Normal",
        $"summary-{name}",
        $"description-{name}",
        $"evidence-{name}",
        "");
}

public sealed class InventoryViewModelFilteringTests
{
    private static InventoryViewModel CreateViewModel() => new(new FixedDiagnosticsService());

    private static List<DeviceInfo> CreateDevices() => new()
    {
        new DeviceInfo { Name = "Alpha", Manufacturer = "MakerA", DeviceId = "ID-111", Status = "OK" },
        new DeviceInfo { Name = "Beta", Manufacturer = "MakerB", DeviceId = "ID-222", Status = "Error" },
        new DeviceInfo { Name = "Gamma", Manufacturer = "MakerC", DeviceId = "ID-333", Status = "Warning" }
    };

    private static InventoryViewModel CreateLoadedViewModel()
    {
        var viewModel = CreateViewModel();
        viewModel.SetDevices(CreateDevices(), InventoryDisplayState.Fresh, "2024-05-01 10:00", false);
        return viewModel;
    }

    [Theory]
    [InlineData("Beta", "Beta")]          // Name
    [InlineData("makerc", "Gamma")]       // Manufacturer, case-insensitive
    [InlineData("ID-111", "Alpha")]       // DeviceId
    [InlineData("Warning", "Gamma")]      // Status
    [InlineData("summary-alpha", "Alpha")] // DiagnosticSummary
    [InlineData("evidence-beta", "Beta")]  // DiagnosticEvidence
    public void SearchText_FiltersAcrossAllSixFields(string query, string expectedName)
    {
        var viewModel = CreateLoadedViewModel();

        viewModel.SearchText = query;

        var match = Assert.Single(viewModel.FilteredDevices);
        Assert.Equal(expectedName, match.Name);
    }

    [Fact]
    public void SearchText_SetBeforeDevicesArrive_FiltersWhenDevicesArrive()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchText = "Beta";
        Assert.Empty(viewModel.FilteredDevices);

        viewModel.SetDevices(CreateDevices(), InventoryDisplayState.Fresh, null, false);

        var match = Assert.Single(viewModel.FilteredDevices);
        Assert.Equal("Beta", match.Name);
    }

    [Fact]
    public void SetDevices_UpdatesOriginStateAndTimestamp()
    {
        var viewModel = CreateViewModel();
        Assert.Equal(InventoryDisplayState.Empty, viewModel.DisplayState);
        Assert.Null(viewModel.ScanTimeUtc);

        viewModel.SetDevices(CreateDevices(), InventoryDisplayState.Cached, "2024-04-01 09:30", false);

        Assert.Equal(InventoryDisplayState.Cached, viewModel.DisplayState);
        Assert.Equal("2024-04-01 09:30", viewModel.ScanTimeUtc);
        Assert.Equal(3, viewModel.DeviceCount);
    }

    [Fact]
    public void SetDevices_WithEmptyInventory_ReportsEmptyState()
    {
        var viewModel = CreateLoadedViewModel();

        viewModel.SetDevices(Array.Empty<DeviceInfo>(), InventoryDisplayState.Empty, null, false);

        Assert.Equal(InventoryDisplayState.Empty, viewModel.DisplayState);
        Assert.Equal(0, viewModel.DeviceCount);
        Assert.Empty(viewModel.FilteredDevices);
    }

    [Fact]
    public void SetDevices_WithFreshInventory_ReportsFreshState()
    {
        var viewModel = CreateViewModel();

        viewModel.SetDevices(CreateDevices(), InventoryDisplayState.Fresh, "2024-05-01 10:00", false);

        Assert.Equal(InventoryDisplayState.Fresh, viewModel.DisplayState);
        Assert.Equal("2024-05-01 10:00", viewModel.ScanTimeUtc);
    }

    [Fact]
    public void FilterIndex_SelectsProblemsAndHealthy()
    {
        var viewModel = CreateLoadedViewModel();
        viewModel.Devices[0].Diagnostics = viewModel.Devices[0].Diagnostics! with
        {
            Outcome = DiagnosticOutcome.Critical
        };

        viewModel.FilterIndex = 1;
        var problem = Assert.Single(viewModel.FilteredDevices);
        Assert.Equal("Alpha", problem.Name);

        viewModel.FilterIndex = 2;
        Assert.Equal(2, viewModel.FilteredDevices.Count); // Beta and Gamma remain healthy
        Assert.DoesNotContain(viewModel.FilteredDevices, device => device.Name == "Alpha");

        viewModel.FilterIndex = 0;
        Assert.Equal(3, viewModel.FilteredDevices.Count);
    }

    [Fact]
    public void SetDevices_RaisesNoFilterChanged()
    {
        var viewModel = CreateViewModel();
        int raised = 0;
        viewModel.FilterChanged += (_, _) => raised++;

        viewModel.SetDevices(CreateDevices(), InventoryDisplayState.Fresh, null, false);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void SilentRefresh_RaisesNoFilterChanged()
    {
        var viewModel = CreateLoadedViewModel();
        int raised = 0;
        viewModel.FilterChanged += (_, _) => raised++;

        viewModel.RefreshFilteredDevices();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void UserChanges_RaiseFilterChangedOnceEach()
    {
        var viewModel = CreateLoadedViewModel();
        int raised = 0;
        viewModel.FilterChanged += (_, _) => raised++;

        viewModel.SearchText = "Alpha";
        viewModel.SearchText = "Alpha";   // equality guard: no second event
        viewModel.FilterIndex = 1;
        viewModel.FilterIndex = 1;

        Assert.Equal(2, raised);
    }
}

[Collection("WPF UI tests")]
public sealed class InventoryFilteringUiTests
{
    [Fact]
    public async Task FilteringBeforeShow_DoesNotTouchStatusText()
    {
        await StaRunner.RunAsync(() =>
        {
            var diagnostics = new FixedDiagnosticsService();
            var viewModel = new InventoryViewModel(diagnostics);
            var window = new MainWindow(
                new EmptyDeviceScanner(), new EmptyDatabaseService(), diagnostics,
                new InMemorySettingsService(), null, viewModel);

            var searchBox = Assert.IsType<TextBox>(window.FindName("SearchBox"));
            var statusText = Assert.IsType<TextBlock>(window.FindName("StatusText"));

            // No Show(): the guard in UpdateFilterOutput has to absorb the change.
            searchBox.Text = "Beta";
            string statusBeforeData = statusText.Text;

            viewModel.SetDevices(
                new List<DeviceInfo>
                {
                    new DeviceInfo { Name = "Alpha" },
                    new DeviceInfo { Name = "Beta" }
                },
                InventoryDisplayState.Fresh, null, false);

            Assert.Equal(statusBeforeData, statusText.Text);
            Assert.Single(viewModel.FilteredDevices);

            window.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LanguageSwitch_KeepsActiveSearch_WithoutOverwritingStatusText()
    {
        await StaRunner.RunAsync(async () =>
        {
            var diagnostics = new FixedDiagnosticsService();
            var viewModel = new InventoryViewModel(diagnostics);
            var window = new MainWindow(
                new EmptyDeviceScanner(), new EmptyDatabaseService(), diagnostics,
                new InMemorySettingsService(), null, viewModel);
            window.Show();
            await StaRunner.WaitForAsync(() => window.IsLoaded);

            var searchBox = Assert.IsType<TextBox>(window.FindName("SearchBox"));
            var languageBox = Assert.IsType<ComboBox>(window.FindName("LanguageBox"));
            var statusText = Assert.IsType<TextBlock>(window.FindName("StatusText"));
            var devicesGrid = Assert.IsType<DataGrid>(window.FindName("DevicesGrid"));

            viewModel.SetDevices(
                new List<DeviceInfo>
                {
                    new DeviceInfo { Name = "Alpha" },
                    new DeviceInfo { Name = "Beta" },
                    new DeviceInfo { Name = "Gamma" }
                },
                InventoryDisplayState.Fresh, "2024-05-01 10:00", false);

            searchBox.Text = "Beta";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            Assert.Single(viewModel.FilteredDevices);
            Assert.Single(devicesGrid.Items.Cast<object>());
            string statusAfterSearch = statusText.Text;

            languageBox.SelectedIndex = 3; // French: re-enriches and refreshes silently
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            Assert.Single(viewModel.FilteredDevices);
            Assert.Equal("Beta", viewModel.FilteredDevices[0].Name);

            // Scope of this assertion: the search survives the language switch. It does NOT
            // prove the silent-refresh rule, because ApplyLanguage writes the ready message
            // (line 635) after refreshing; that rule is covered by SetDevicesWhileShown below
            // and by the FilterChanged counting tests.
            Assert.Equal(LocalizationCatalog.Languages[3].ReadyMessages, statusText.Text);
            Assert.NotEqual(statusAfterSearch, statusText.Text);

            window.Close();
        });
    }

    [Fact]
    public async Task SetDevicesWhileShown_DoesNotOverwriteStatusText()
    {
        await StaRunner.RunAsync(async () =>
        {
            var diagnostics = new FixedDiagnosticsService();
            var viewModel = new InventoryViewModel(diagnostics);
            var window = new MainWindow(
                new EmptyDeviceScanner(), new EmptyDatabaseService(), diagnostics,
                new InMemorySettingsService(), null, viewModel);
            window.Show();
            await StaRunner.WaitForAsync(() => window.IsLoaded);

            var statusText = Assert.IsType<TextBlock>(window.FindName("StatusText"));
            string statusBeforeLoad = statusText.Text;

            viewModel.SetDevices(
                new List<DeviceInfo> { new DeviceInfo { Name = "Alpha" } },
                InventoryDisplayState.Fresh, "2024-05-01 10:00", false);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            // Loading data must not replace the inventory state message with a filter count.
            Assert.Equal(statusBeforeLoad, statusText.Text);
            Assert.Single(viewModel.FilteredDevices);

            window.Close();
        });
    }
}
