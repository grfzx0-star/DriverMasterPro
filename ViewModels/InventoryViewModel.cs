using System.Collections.Generic;
using System.Collections.ObjectModel;
using DriverMasterPro.Models;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro.ViewModels;

/// <summary>
/// Inventory shown by the main window: the devices of the displayed snapshot, the counters
/// derived from them, the search and filter state, and the origin of what is displayed.
/// Localized rendering stays in the window until the inline Translate literals move into
/// the catalog; this view model holds state and data only.
/// </summary>
public sealed class InventoryViewModel : ViewModelBase
{
    private readonly IDiagnosticsService _diagnostics;
    private readonly List<DeviceInfo> _allDevices = new();
    private string _searchText = "";
    private int _filterIndex;

    public InventoryViewModel(IDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>Raised after a user-driven filter change, never after loading data.</summary>
    public event EventHandler? FilterChanged;

    /// <summary>Devices of the displayed snapshot, unfiltered.</summary>
    public IReadOnlyList<DeviceInfo> Devices => _allDevices;

    /// <summary>What the grid shows. Replacing the contents keeps the existing binding valid.</summary>
    public ObservableCollection<DeviceInfo> FilteredDevices { get; } = new();

    /// <summary>Where the displayed devices came from: fresh scan, cache, history or nothing.</summary>
    public InventoryDisplayState DisplayState { get; private set; } = InventoryDisplayState.Empty;

    /// <summary>Timestamp of the displayed snapshot, already formatted for display.</summary>
    public string? ScanTimeUtc { get; private set; }

    public int DeviceCount => _allDevices.Count;

    public int HealthyCount =>
        _allDevices.Count(device => device.Diagnostics?.Outcome == DiagnosticOutcome.Healthy);

    public int ProblemCount =>
        _allDevices.Count(device => device.Diagnostics?.Outcome != DiagnosticOutcome.Healthy);

    /// <summary>
    /// Raw text as typed. Trimming happens while filtering, not here: a setter that trimmed
    /// would rewrite the bound text box on every keystroke and move the caret.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
                return;

            _searchText = value;
            OnPropertyChanged();
            RefreshWithEvent();
        }
    }

    /// <summary>Selected filter: 1 shows problems only, 2 shows healthy only, anything else all.</summary>
    public int FilterIndex
    {
        get => _filterIndex;
        set
        {
            if (_filterIndex == value)
                return;

            _filterIndex = value;
            OnPropertyChanged();
            RefreshWithEvent();
        }
    }

    /// <summary>
    /// Replaces the displayed devices and enriches them for the current language. Deliberately
    /// silent: loading data or switching language must not overwrite the status text, which is
    /// why the caller still owns its own status and diagnostic updates.
    /// </summary>
    public void SetDevices(
        IEnumerable<DeviceInfo> devices,
        InventoryDisplayState state,
        string? scanTimeUtc,
        bool isArabic)
    {
        _allDevices.Clear();
        _allDevices.AddRange(devices);
        _diagnostics.EnrichAll(_allDevices, isArabic);

        DisplayState = state;
        ScanTimeUtc = scanTimeUtc;

        OnPropertyChanged(nameof(DisplayState));
        OnPropertyChanged(nameof(ScanTimeUtc));
        OnPropertyChanged(nameof(DeviceCount));
        OnPropertyChanged(nameof(HealthyCount));
        OnPropertyChanged(nameof(ProblemCount));

        RefreshCore();
    }

    /// <summary>
    /// Silent refresh for callers that re-render on their own, such as the language switch
    /// after re-enriching the devices.
    /// </summary>
    public void RefreshFilteredDevices() => RefreshCore();

    private void RefreshCore()
    {
        IEnumerable<DeviceInfo> filtered = _allDevices;

        if (_filterIndex == 1)
            filtered = filtered.Where(d => d.Diagnostics?.Outcome != DiagnosticOutcome.Healthy);
        else if (_filterIndex == 2)
            filtered = filtered.Where(d => d.Diagnostics?.Outcome == DiagnosticOutcome.Healthy);

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            string query = _searchText.Trim();
            filtered = filtered.Where(d =>
                (d.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.Manufacturer ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.DeviceId ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.Status ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                d.DiagnosticSummary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                d.DiagnosticEvidence.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceFilteredDevices(filtered.ToList());
    }

    private void RefreshWithEvent()
    {
        RefreshCore();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the visible devices without touching the filter inputs.</summary>
    private void ReplaceFilteredDevices(IEnumerable<DeviceInfo> devices)
    {
        FilteredDevices.Clear();
        foreach (var device in devices)
        {
            FilteredDevices.Add(device);
        }
    }
}
