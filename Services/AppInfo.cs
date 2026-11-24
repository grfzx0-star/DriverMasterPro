using System;

namespace DriverMasterPro.Services;

/// <summary>
/// Single source of truth for the application version.
/// The number comes from the assembly, which is stamped by &lt;Version&gt; in
/// DriverMasterPro.csproj. Callers supply only the localized wording around it,
/// using the "{version}" placeholder, so no version digit is duplicated in code or XAML.
/// </summary>
public static class AppInfo
{
    /// <summary>Assembly version formatted as "major.minor.build" (for example "0.2.0").</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// Replaces the "{version}" placeholder in a localized label template.
    /// Templates without the placeholder are returned unchanged.
    /// </summary>
    public static string Format(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template.Replace("{version}", Version, StringComparison.Ordinal);
    }

    private static string ReadVersion()
    {
        System.Version? version = typeof(AppInfo).Assembly.GetName().Version;
        if (version is null)
        {
            return "0.0.0";
        }

        // Build is -1 when the assembly carries a two-part version such as 0.2.
        return version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";
    }
}
