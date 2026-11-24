using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using DriverMasterPro.Services;
using Xunit;

namespace DriverMasterPro.Tests;

/// <summary>
/// Covers language persistence and startup flow direction. Bug 6: the language pass used to
/// run only on a Loaded user change, so a saved Arabic preference never reached the first frame.
/// </summary>
[Collection("WPF UI tests")]
public sealed class LanguagePersistenceTests
{
    // LanguageBox item order: 0 Deutsch, 1 العربية, 2 English, 3 Français, 4 Español.
    private const int ArabicIndex = 1;
    private const int SpanishIndex = 4;
    private const int GermanIndex = 0;

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        string directory = NewTempDirectory();
        try
        {
            var service = new JsonSettingsService(Path.Combine(directory, "settings.json"));
            service.Save(new AppSettings { LanguageIndex = SpanishIndex });

            Assert.Equal(SpanishIndex, service.Load().LanguageIndex);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Save_WritesSchemaVersion()
    {
        string directory = NewTempDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            new JsonSettingsService(path).Save(new AppSettings { LanguageIndex = ArabicIndex });

            Assert.Contains("\"Schema\": 1", File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsGermanDefault()
    {
        string directory = NewTempDirectory();
        try
        {
            var service = new JsonSettingsService(Path.Combine(directory, "settings.json"));

            Assert.Equal(GermanIndex, service.Load().LanguageIndex);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Load_CorruptFile_ReturnsGermanDefault()
    {
        string directory = NewTempDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{ this is not json");

            Assert.Equal(GermanIndex, new JsonSettingsService(path).Load().LanguageIndex);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ArabicStartup_AppliesRightToLeftOnFirstFrame()
    {
        string directory = NewTempDirectory();
        try
        {
            var service = new JsonSettingsService(Path.Combine(directory, "settings.json"));
            service.Save(new AppSettings { LanguageIndex = ArabicIndex });

            await StaRunner.RunAsync(async () =>
            {
                var window = CreateWindow(service);
                try
                {
                    // Flow direction must be correct before Show() completes the first frame.
                    Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);

                    window.Show();
                    await StaRunner.WaitForAsync(() => window.IsLoaded);
                    Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);

                    var languageBox = Assert.IsType<ComboBox>(window.FindName("LanguageBox"));
                    Assert.Equal(ArabicIndex, languageBox.SelectedIndex);
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task OutOfRangePersistedIndex_IsClampedToGerman()
    {
        string directory = NewTempDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{\"Schema\":1,\"LanguageIndex\":99}");

            await StaRunner.RunAsync(() =>
            {
                var window = CreateWindow(new JsonSettingsService(path));
                try
                {
                    var languageBox = Assert.IsType<ComboBox>(window.FindName("LanguageBox"));
                    Assert.InRange(languageBox.SelectedIndex, GermanIndex, SpanishIndex);
                    Assert.Equal(GermanIndex, languageBox.SelectedIndex);
                    Assert.Equal(FlowDirection.LeftToRight, window.FlowDirection);
                }
                finally
                {
                    window.Close();
                }

                return Task.CompletedTask;
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task SwitchingLanguage_PersistsSelection()
    {
        string directory = NewTempDirectory();
        try
        {
            var service = new JsonSettingsService(Path.Combine(directory, "settings.json"));

            await StaRunner.RunAsync(async () =>
            {
                var window = CreateWindow(service);
                try
                {
                    window.Show();
                    await StaRunner.WaitForAsync(() => window.IsLoaded);

                    var languageBox = Assert.IsType<ComboBox>(window.FindName("LanguageBox"));
                    languageBox.SelectedIndex = ArabicIndex;
                    await StaRunner.WaitForAsync(() => service.Load().LanguageIndex == ArabicIndex);

                    Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static MainWindow CreateWindow(ISettingsService settings) =>
        new(new EmptyDeviceScanner(), new EmptyDatabaseService(), new RuleBasedDiagnosticsService(), settings);

    private static string NewTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dmp-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
