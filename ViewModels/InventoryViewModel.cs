using System.Collections.Generic;
using System.Collections.ObjectModel;
using DriverMasterPro.Models;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro.ViewModels;

/// <summary>
/// Inventory shown by the main window: the devices of the displayed snapshot and the counters
/// derived from them. Search and filter logic still lives in MainWindow and is expected to
/// move here in step 3a-ii; until then the window hands over its results explicitly.
/// </summary>
public sealed class InventoryViewModel : ViewModelBase
{
    private readonly IDiagnosticsService _diagnostics;
    private readonly List<DeviceInfo> _allDevices = new();

    public InventoryViewModel(IDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>Devices of the displayed snapshot, unfiltered.</summary>
    public IReadOnlyList<DeviceInfo> Devices => _allDevices;

    /// <summary>What the grid shows. Replacing the contents keeps the existing binding valid.</summary>
    public ObservableCollection<DeviceInfo> FilteredDevices { get; } = new();

    public int DeviceCount => _allDevices.Count;

    public int HealthyCount =>
        _allDevices.Count(device => device.Diagnostics?.Outcome == DiagnosticOutcome.Healthy);

    public int ProblemCount =>
        _allDevices.Count(device => device.Diagnostics?.Outcome != DiagnosticOutcome.Healthy);

    /// <summary>
    /// Replaces the displayed devices and enriches them for the current language. Raising the
    /// counter notifications here is what keeps the bound labels in step with the data.
    /// </summary>
    public void SetDevices(IEnumerable<DeviceInfo> devices, bool isArabic)
    {
        _allDevices.Clear();
        _allDevices.AddRange(devices);
        _diagnostics.EnrichAll(_allDevices, isArabic);

        OnPropertyChanged(nameof(DeviceCount));
        OnPropertyChanged(nameof(HealthyCount));
        OnPropertyChanged(nameof(ProblemCount));

        ReplaceFilteredDevices(_allDevices);
    }

    /// <summary>
    /// Seam for step 3a-ii: once the search and filter logic moves here, this becomes the only
    /// place that decides what FilteredDevices contains. Today the window passes its results in.
    /// </summary>
    public void ReplaceFilteredDevices(IEnumerable<DeviceInfo> devices)
    {
        FilteredDevices.Clear();
        foreach (var device in devices)
        {
            FilteredDevices.Add(device);
        }
    }
}
