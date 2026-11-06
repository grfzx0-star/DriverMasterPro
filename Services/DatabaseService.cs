using System.IO;
using Microsoft.Data.Sqlite;
using DriverMasterPro.Models;

namespace DriverMasterPro.Services;

public sealed class DatabaseService
{
    private readonly string _dbPath;
    public string DatabasePath => _dbPath;

    public DatabaseService()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DriverMasterPro");
        Directory.CreateDirectory(folder);
        _dbPath = Path.Combine(folder, "driver-master.db");
    }

    private SqliteConnection OpenConnection() => new($"Data Source={_dbPath}");

    public async Task InitializeAsync()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Devices (
                DeviceId TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Manufacturer TEXT,
                Status TEXT,
                ProblemCode INTEGER,
                LastSeenUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ScanHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ScanTimeUtc TEXT NOT NULL,
                DeviceCount INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveScanAsync(IReadOnlyCollection<DeviceInfo> devices)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        foreach (var device in devices)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Devices(DeviceId, Name, Manufacturer, Status, ProblemCode, LastSeenUtc)
                VALUES($id, $name, $manufacturer, $status, $problem, $seen)
                ON CONFLICT(DeviceId) DO UPDATE SET
                    Name=excluded.Name, Manufacturer=excluded.Manufacturer,
                    Status=excluded.Status, ProblemCode=excluded.ProblemCode,
                    LastSeenUtc=excluded.LastSeenUtc;
                """;
            command.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(device.DeviceId) ? $"NO-ID:{device.Name}" : device.DeviceId);
            command.Parameters.AddWithValue("$name", device.Name);
            command.Parameters.AddWithValue("$manufacturer", device.Manufacturer);
            command.Parameters.AddWithValue("$status", device.Status);
            command.Parameters.AddWithValue("$problem", device.ProblemCode);
            command.Parameters.AddWithValue("$seen", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
        var history = connection.CreateCommand();
        history.Transaction = transaction;
        history.CommandText = "INSERT INTO ScanHistory(ScanTimeUtc, DeviceCount) VALUES($time, $count)";
        history.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O"));
        history.Parameters.AddWithValue("$count", devices.Count);
        await history.ExecuteNonQueryAsync();
        transaction.Commit();
    }
}

