using System;
using System.IO;
using System.Text.Json;

namespace DriverMasterPro.Services;

/// <summary>
/// Stores preferences as JSON in <c>%LOCALAPPDATA%\DriverMasterPro\settings.json</c>.
/// Writes go to a temporary file and are then moved into place, so an interrupted
/// write cannot leave a half-written file that silently resets the user's language.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;

    public JsonSettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DriverMasterPro",
            "settings.json");
    }

    /// <summary>Full path of the settings file. Useful for diagnostics and tests.</summary>
    public string SettingsPath => _path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(_path);
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return settings ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Hand-edited or truncated file: start from defaults instead of failing to launch.
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (IOException)
        {
            // Non-fatal: the next start falls back to defaults.
        }
        catch (UnauthorizedAccessException)
        {
            // Non-fatal: the next start falls back to defaults.
        }
    }
}
