namespace DriverMasterPro.Services.Localization;

/// <summary>
/// Resolves a language index to its localized strings. Data only: the service holds no state
/// and never touches a control, so the caller keeps ownership of which language is shown.
/// </summary>
public interface ILocalizationService
{
    /// <param name="languageIndex">
    /// 0-4 (LanguageBox order). The caller must clamp: an out-of-range index throws instead of
    /// silently falling back, so a wrong index surfaces immediately rather than as German text.
    /// </param>
    LocalizationStrings GetStrings(int languageIndex);
}
