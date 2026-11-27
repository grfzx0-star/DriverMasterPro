namespace DriverMasterPro.Services.Localization;

/// <summary>
/// Localized UI strings for a single language. Property order matches the table order of the
/// golden fixture, so serialization stays deterministic. Flat tables hold one string for that
/// language; FilterOptions and ColumnHeaders hold the rows it displays.
/// </summary>
public sealed record LocalizationStrings(
    string PageTitles,
    string Subtitles,
    string ScanButtons,
    string DevicesLabels,
    string HealthyLabels,
    string ProblemLabels,
    string SearchLabels,
    string FilterLabels,
    string[] FilterOptions,
    string SearchTooltips,
    string[] ColumnHeaders,
    string EditionLabels,
    string DashboardLabels,
    string InventoryLabels,
    string LanguageLabels,
    string ReadyMessages,
    string HistoryLabels);
