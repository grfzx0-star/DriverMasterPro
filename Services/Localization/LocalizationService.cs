namespace DriverMasterPro.Services.Localization;

internal sealed class LocalizationService : ILocalizationService
{
    public LocalizationStrings GetStrings(int languageIndex) =>
        LocalizationCatalog.Languages[languageIndex];
}
