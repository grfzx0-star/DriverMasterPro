namespace DriverMasterPro.Models;

/// <summary>
/// What the inventory list currently shows: nothing, a persisted snapshot of a given
/// freshness, or the result of the scan that just finished. Public because the view
/// model takes it as a parameter once the display state moves there (task 3a-ii).
/// </summary>
public enum InventoryDisplayState
{
    Empty,
    LegacyCached,
    Cached,
    Historical,
    Fresh
}
