namespace DriverMasterPro.Services;

/// <summary>
/// Persisted user preferences. <see cref="Schema"/> allows a later migration when
/// Phase 3 adds window size and position; readers must treat an unknown schema as default.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Current on-disk schema version.</summary>
    public int Schema { get; init; } = 1;

    /// <summary>
    /// Language index matching the <c>LanguageBox</c> item order:
    /// 0 Deutsch, 1 العربية, 2 English, 3 Français, 4 Español.
    /// </summary>
    public int LanguageIndex { get; init; }
}
