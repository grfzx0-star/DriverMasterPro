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
                    ProblemCodeEvidenceAvailable INTEGER NOT NULL DEFAULT 1,
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

            var hasProblemCodeEvidenceColumn = false;
            await using (var schemaCommand = connection.CreateCommand())
            {
                schemaCommand.CommandText = "PRAGMA table_info(Devices);";
                await using var reader = await schemaCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (reader.GetString(1).Equals("ProblemCodeEvidenceAvailable", StringComparison.OrdinalIgnoreCase))
                    {
                        hasProblemCodeEvidenceColumn = true;
                        break;
                    }
                }
            }

            if (!hasProblemCodeEvidenceColumn)
            {
                await using var migrationTransaction = await connection.BeginTransactionAsync();
                await using var migrationCommand = connection.CreateCommand();
                migrationCommand.Transaction = (SqliteTransaction)migrationTransaction;
                migrationCommand.CommandText = """
                    ALTER TABLE Devices
                    ADD COLUMN ProblemCodeEvidenceAvailable INTEGER NOT NULL DEFAULT 1;
                    UPDATE Devices
                    SET ProblemCodeEvidenceAvailable = 0
                    WHERE ProblemCode = 0;
                    """;
                await migrationCommand.ExecuteNonQueryAsync();
                await migrationTransaction.CommitAsync();
            }
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
                INSERT INTO Devices(
                    DeviceId, Name, Manufacturer, Status, ProblemCode,
                    ProblemCodeEvidenceAvailable, LastSeenUtc)
                VALUES($id, $name, $manufacturer, $status, $problem, $problemEvidence, $seen)
                ON CONFLICT(DeviceId) DO UPDATE SET
                    Name=excluded.Name, Manufacturer=excluded.Manufacturer,
                    Status=excluded.Status, ProblemCode=excluded.ProblemCode,
                    ProblemCodeEvidenceAvailable=excluded.ProblemCodeEvidenceAvailable,
                    LastSeenUtc=excluded.LastSeenUtc;
                """;

            var pId = command.Parameters.Add("$id", SqliteType.Text);
            var pName = command.Parameters.Add("$name", SqliteType.Text);
            var pManufacturer = command.Parameters.Add("$manufacturer", SqliteType.Text);
            var pStatus = command.Parameters.Add("$status", SqliteType.Text);
            var pProblem = command.Parameters.Add("$problem", SqliteType.Integer);
            var pProblemEvidence = command.Parameters.Add("$problemEvidence", SqliteType.Integer);
            var pSeen = command.Parameters.Add("$seen", SqliteType.Text);

            var nowIso = DateTime.UtcNow.ToString("O");
            var scanObservationId = Guid.NewGuid().ToString("N");
            var seenDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int observationCounter = 0;

            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string rawId = device.DeviceId?.Trim() ?? "";
                string resolvedId;

                if (string.IsNullOrEmpty(rawId))
                {
                    // Keep devices without a supplied identifier as separate scan observations.
                    resolvedId = $"OBSERVATION:{scanObservationId}:{device.Name}#{observationCounter++}";
                }
                else
                {
                    resolvedId = rawId;

                    // Preserve repeated IDs as separate observations without inferring physical identity.
                    if (!seenDeviceIds.Add(resolvedId))
                    {
                        resolvedId = $"{resolvedId}#DUP-OBSERVATION:{scanObservationId}:{observationCounter++}";
                    }
                }

                pId.Value = resolvedId;
                pName.Value = string.IsNullOrWhiteSpace(device.Name) ? "Unknown device" : device.Name;
                pManufacturer.Value = (object?)device.Manufacturer ?? DBNull.Value;
                pStatus.Value = (object?)device.Status ?? DBNull.Value;
                pProblem.Value = (object?)device.ProblemCode ?? DBNull.Value;
                pProblemEvidence.Value = device.ProblemCode is null ? 0 : 1;
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
            command.CommandText = """
                SELECT Name, DeviceId, Manufacturer, Status, ProblemCode, ProblemCodeEvidenceAvailable
                FROM Devices ORDER BY Name
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                devices.Add(new DeviceInfo
                {
                    Name = reader.IsDBNull(0) ? "Unknown device" : reader.GetString(0),
                    DeviceId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Manufacturer = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Status = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ProblemCode = ReadProblemCode(reader)
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
            command.CommandText = """
                SELECT Name, DeviceId, Manufacturer, Status, ProblemCode, ProblemCodeEvidenceAvailable
                FROM Devices WHERE LastSeenUtc = $seen ORDER BY Name
                """;
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
                    ProblemCode = ReadProblemCode(reader)
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
            command.CommandText = """
                SELECT Name, DeviceId, Manufacturer, Status, ProblemCode, ProblemCodeEvidenceAvailable
                FROM Devices WHERE LastSeenUtc < $seen ORDER BY Name
                """;
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
                    ProblemCode = ReadProblemCode(reader)
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

    private static uint? ReadProblemCode(SqliteDataReader reader) =>
        reader.IsDBNull(4) || reader.GetInt64(5) == 0
            ? null
            : Convert.ToUInt32(reader.GetInt64(4));
}
