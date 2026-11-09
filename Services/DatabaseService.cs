using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using DriverMasterPro.Models;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro.Services;

public sealed class DatabaseService : IDatabaseService
{
    private readonly string _dbPath;
    public string DatabasePath => _dbPath;

    public DatabaseService(string? customDbPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customDbPath))
        {
            var customFolder = Path.GetDirectoryName(customDbPath);
            if (!string.IsNullOrEmpty(customFolder))
            {
                Directory.CreateDirectory(customFolder);
            }
            _dbPath = customDbPath;
        }
        else
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DriverMasterPro");
            Directory.CreateDirectory(folder);
            _dbPath = Path.Combine(folder, "driver-master.db");
        }
    }

    private SqliteConnection OpenConnection() => new($"Data Source={_dbPath}");

    public async Task InitializeAsync()
    {
        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
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
                CREATE INDEX IF NOT EXISTS IX_Devices_LastSeenUtc ON Devices(LastSeenUtc);
                """;
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[DatabaseService] InitializeAsync failed: {ex}");
            throw;
        }
    }

    public async Task SaveScanAsync(IReadOnlyCollection<DeviceInfo> devices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO Devices(DeviceId, Name, Manufacturer, Status, ProblemCode, LastSeenUtc)
                VALUES($id, $name, $manufacturer, $status, $problem, $seen)
                ON CONFLICT(DeviceId) DO UPDATE SET
                    Name=excluded.Name, Manufacturer=excluded.Manufacturer,
                    Status=excluded.Status, ProblemCode=excluded.ProblemCode,
                    LastSeenUtc=excluded.LastSeenUtc;
                """;

            var pId = command.Parameters.Add("$id", SqliteType.Text);
            var pName = command.Parameters.Add("$name", SqliteType.Text);
            var pManufacturer = command.Parameters.Add("$manufacturer", SqliteType.Text);
            var pStatus = command.Parameters.Add("$status", SqliteType.Text);
            var pProblem = command.Parameters.Add("$problem", SqliteType.Integer);
            var pSeen = command.Parameters.Add("$seen", SqliteType.Text);

            var nowIso = DateTime.UtcNow.ToString("O");
            var scanObservationId = Guid.NewGuid().ToString("N");
            var seenPersistentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int observationCounter = 0;

            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string rawId = device.DeviceId?.Trim() ?? "";
                string resolvedId;

                if (string.IsNullOrEmpty(rawId))
                {
                    // Device genuinely lacks a verified persistent hardware identifier.
                    // Represent as a scan-specific observation anchored to this scan session,
                    // rather than fabricating a false persistent hardware identity.
                    resolvedId = $"OBSERVATION:{scanObservationId}:{device.Name}#{observationCounter++}";
                }
                else
                {
                    resolvedId = rawId;

                    // If identical physical DeviceId appears more than once in the same scan batch,
                    // keep the first as the primary hardware anchor and record subsequent instances
                    // as scan-specific duplicates to preserve database integrity.
                    if (!seenPersistentIds.Add(resolvedId))
                    {
                        resolvedId = $"{resolvedId}#DUP-OBSERVATION:{scanObservationId}:{observationCounter++}";
                    }
                }

                pId.Value = resolvedId;
                pName.Value = string.IsNullOrWhiteSpace(device.Name) ? "Unknown device" : device.Name;
                pManufacturer.Value = (object?)device.Manufacturer ?? DBNull.Value;
                pStatus.Value = (object?)device.Status ?? DBNull.Value;
                pProblem.Value = device.ProblemCode;
                pSeen.Value = nowIso;

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var history = connection.CreateCommand();
            history.Transaction = (SqliteTransaction)transaction;
            history.CommandText = "INSERT INTO ScanHistory(ScanTimeUtc, DeviceCount) VALUES($time, $count)";
            history.Parameters.AddWithValue("$time", nowIso);
            history.Parameters.AddWithValue("$count", devices.Count);
            await history.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Trace.TraceWarning("[DatabaseService] SaveScanAsync cancelled. Transaction rolled back.");
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[DatabaseService] SaveScanAsync failed: {ex}");
            throw;
        }
    }

    public async Task<List<DeviceInfo>> GetCachedDevicesAsync()
    {
        var devices = new List<DeviceInfo>();

        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name, DeviceId, Manufacturer, Status, ProblemCode FROM Devices ORDER BY Name";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                devices.Add(new DeviceInfo
                {
                    Name = reader.IsDBNull(0) ? "Unknown device" : reader.GetString(0),
                    DeviceId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Manufacturer = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Status = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ProblemCode = reader.IsDBNull(4) ? 0u : Convert.ToUInt32(reader.GetInt64(4))
                });
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[DatabaseService] GetCachedDevicesAsync failed: {ex}");
            throw;
        }

        return devices;
    }

    public async Task<List<DeviceInfo>> GetActiveDevicesAsync(string scanTimeUtc)
    {
        var devices = new List<DeviceInfo>();

        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name, DeviceId, Manufacturer, Status, ProblemCode FROM Devices WHERE LastSeenUtc = $seen ORDER BY Name";
            command.Parameters.AddWithValue("$seen", scanTimeUtc);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                devices.Add(new DeviceInfo
                {
                    Name = reader.IsDBNull(0) ? "Unknown device" : reader.GetString(0),
                    DeviceId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Manufacturer = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Status = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ProblemCode = reader.IsDBNull(4) ? 0u : Convert.ToUInt32(reader.GetInt64(4))
                });
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[DatabaseService] GetActiveDevicesAsync failed: {ex}");
            throw;
        }

        return devices;
    }

    public async Task<List<DeviceInfo>> GetHistoricalDevicesAsync(string currentScanTimeUtc)
    {
        var devices = new List<DeviceInfo>();

        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name, DeviceId, Manufacturer, Status, ProblemCode FROM Devices WHERE LastSeenUtc < $seen ORDER BY Name";
            command.Parameters.AddWithValue("$seen", currentScanTimeUtc);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                devices.Add(new DeviceInfo
                {
                    Name = reader.IsDBNull(0) ? "Unknown device" : reader.GetString(0),
                    DeviceId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Manufacturer = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Status = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ProblemCode = reader.IsDBNull(4) ? 0u : Convert.ToUInt32(reader.GetInt64(4))
                });
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[DatabaseService] GetHistoricalDevicesAsync failed: {ex}");
            throw;
        }

        return devices;
    }
}
