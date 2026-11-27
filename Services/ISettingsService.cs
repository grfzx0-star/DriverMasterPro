namespace DriverMasterPro.Services;

/// <summary>
/// Reads and writes user preferences. Implementations must never throw on a missing,
/// corrupt or unreadable store: they fall back to defaults instead, because a settings
/// failure must not prevent the application from starting.
/// </summary>
public interface ISettingsService
{
    AppSettings Load();

    void Save(AppSettings settings);
}
