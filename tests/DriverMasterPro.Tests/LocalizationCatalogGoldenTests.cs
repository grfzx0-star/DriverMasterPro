using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using DriverMasterPro.Services.Localization;
using Xunit;

namespace DriverMasterPro.Tests;

/// <summary>
/// Compares <see cref="LocalizationCatalog"/> against an independent golden fixture. The
/// fixture is language-major (one object per language, 17 named tables), so it survives the
/// planned record reshape: after that reshape only the two accessors marked below change,
/// while the fixture file itself must stay byte-identical.
/// </summary>
/// <remarks>
/// The fixture is copied to the output directory with PreserveNewest, so running
/// dotnet test --no-build after editing the fixture can test against a stale copy. Update the
/// fixture and rebuild together, and verify the intent with
/// git diff --exit-code -- tests/DriverMasterPro.Tests/Fixtures/localization-catalog.golden.json
/// rather than with a file hash, so line-ending normalisation does not produce false alarms.
/// </remarks>
public sealed class LocalizationCatalogGoldenTests
{
    /// <summary>
    /// Must match the options used to generate the fixture, or a regeneration would produce a
    /// spurious diff. UnicodeRanges.All keeps Arabic readable instead of \uXXXX escapes.
    /// </summary>
    internal static readonly JsonSerializerOptions FixtureOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private static readonly string[] FlatTables =
    {
        "PageTitles", "Subtitles", "ScanButtons", "DevicesLabels", "HealthyLabels",
        "ProblemLabels", "SearchLabels", "FilterLabels", "SearchTooltips", "EditionLabels",
        "DashboardLabels", "InventoryLabels", "LanguageLabels", "ReadyMessages", "HistoryLabels"
    };

    private static readonly string[] JaggedTables = { "FilterOptions", "ColumnHeaders" };

    private static readonly JsonElement Golden = LoadGolden();

    [Fact]
    public void EveryLanguageMatchesTheGoldenFixture()
    {
        var languages = Golden.EnumerateArray().ToArray();
        Assert.Equal(5, languages.Length);

        var problems = new List<string>();

        for (var languageIndex = 0; languageIndex < languages.Length; languageIndex++)
        {
            foreach (var table in FlatTables)
            {
                CompareFlat(table, languageIndex, languages[languageIndex], problems);
            }

            foreach (var table in JaggedTables)
            {
                CompareJagged(table, languageIndex, languages[languageIndex], problems);
            }
        }

        Assert.True(
            problems.Count == 0,
            "Catalog differs from the golden fixture:"
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    // 1b-ii seam: these are the only members that know how the catalog is stored. They
    // project the five per-language records back into the table-major shape the fixture
    // comparison loop below still uses, so the loop itself never changes.
    private static string[] FlatValues(string table) =>
        Languages().Select(language => (string)Property(table).GetValue(language)!).ToArray();

    private static string[][] JaggedValues(string table) =>
        Languages().Select(language => (string[])Property(table).GetValue(language)!).ToArray();

    private static LocalizationStrings[] Languages() =>
        (LocalizationStrings[])Field("Languages").GetValue(null)!;

    private static PropertyInfo Property(string table) =>
        typeof(LocalizationStrings).GetProperty(table, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"LocalizationStrings has no public property {table}.");

    private static FieldInfo Field(string table) =>
        typeof(LocalizationCatalog).GetField(table, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException($"LocalizationCatalog has no public field {table}.");

    private static void CompareFlat(
        string table,
        int languageIndex,
        JsonElement language,
        List<string> problems)
    {
        var expected = language.GetProperty(table).GetString();
        var actual = FlatValues(table)[languageIndex];

        if (expected != actual)
        {
            problems.Add(
                $"language {languageIndex}, {table}: expected \"{expected}\", actual \"{actual}\"");
        }
    }

    private static void CompareJagged(
        string table,
        int languageIndex,
        JsonElement language,
        List<string> problems)
    {
        var expected = language.GetProperty(table)
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        var actual = JaggedValues(table)[languageIndex];

        if (!expected.SequenceEqual(actual))
        {
            problems.Add(
                $"language {languageIndex}, {table}: expected [{string.Join(", ", expected)}],"
                + $" actual [{string.Join(", ", actual)}]");
        }
    }

    private static JsonElement LoadGolden()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "localization-catalog.golden.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Golden fixture not found at {path}.", path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
