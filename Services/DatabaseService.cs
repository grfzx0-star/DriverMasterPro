using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using DriverMasterPro.Models;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro.Services;

public sealed class DatabaseService : IDatabaseService
{
    private const int CurrentSchemaVersion = 1;
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

    private SqliteConnection OpenConnection() =>
        new(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ToString());

    public async Task InitializeAsync()
    {
        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            var schemaCommand = connection.CreateCommand();
            schemaCommand.Transaction = (SqliteTransaction)transaction;
            schemaCommand.CommandText = """
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
            await using (schemaCommand)
            {
                await schemaCommand.ExecuteNonQueryAsync();
            }

            var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = (SqliteTransaction)transaction;
            versionCommand.CommandText = "PRAGMA user_version;";
            int schemaVersion;
            await using (versionCommand)
            {
                schemaVersion = Convert.ToInt32(await versionCommand.ExecuteScalarAsync());
            }

            if (schemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Database schema version {schemaVersion} is newer than supported version {CurrentSchemaVersion}.");
            }

            if (schemaVersion < 1)
            {
                var hasProblemCodeEvidenceColumn = await HasColumnAsync(
                    connection, (SqliteTransaction)transaction, "Devices", "ProblemCodeEvidenceAvailable");

                if (!hasProblemCodeEvidenceColumn)
                {
                    var evidenceMigration = connection.CreateCommand();
                    evidenceMigration.Transaction = (SqliteTransaction)transaction;
                    evidenceMigration.CommandText = """
                        ALTER TABLE Devices
                        ADD COLUMN ProblemCodeEvidenceAvailable INTEGER NOT NULL DEFAULT 1;
                        UPDATE Devices
                        SET ProblemCodeEvidenceAvailable = 0
                        WHERE ProblemCode = 0;
                        """;
                    await using (evidenceMigration)
                    {
                        await evidenceMigration.ExecuteNonQueryAsync();
                    }
                }

                var snapshotSchema = connection.CreateCommand();
                snapshotSchema.Transaction = (SqliteTransaction)transaction;
                snapshotSchema.CommandText = """
                    CREATE TABLE ScanSnapshots (
                        ScanId INTEGER PRIMARY KEY AUTOINCREMENT,
                        ScanTimeUtc TEXT NOT NULL,
                        DeviceCount INTEGER NOT NULL CHECK(DeviceCount >= 0),
                        IsLatest INTEGER NOT NULL CHECK(IsLatest IN (0, 1))
                    );
                    CREATE UNIQUE INDEX IX_ScanSnapshots_OneLatest
                        ON ScanSnapshots(IsLatest) WHERE IsLatest = 1;
                    CREATE TABLE DeviceObservations (
                        ScanId INTEGER NOT NULL REFERENCES ScanSnapshots(ScanId) ON DELETE CASCADE,
                        ObservationOrdinal INTEGER NOT NULL CHECK(ObservationOrdinal >= 0),
                        DeviceId TEXT,
                        Name TEXT NOT NULL,
                        Manufacturer TEXT,
                        Status TEXT,
                        ProblemCode INTEGER,
                        ProblemCodeEvidenceAvailable INTEGER NOT NULL CHECK(ProblemCodeEvidenceAvailable IN (0, 1)),
                        PRIMARY KEY(ScanId, ObservationOrdinal)
                    );
                    CREATE INDEX IX_DeviceObservations_DeviceId
                        ON DeviceObservations(DeviceId);
                    PRAGMA user_version = 1;
                    """;
                await using (snapshotSchema)
                {
                    await snapshotSchema.ExecuteNonQueryAsync();
                }
            }

            await transaction.CommitAsync();
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

            var latestCommand = connection.CreateCommand();
            latestCommand.Transaction = (SqliteTransaction)transaction;
            latestCommand.CommandText = "UPDATE ScanSnapshots SET IsLatest = 0 WHERE IsLatest = 1;";
            await using (latestCommand)
            {
                await latestCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var scanTimeUtc = DateTime.UtcNow.ToString("O");
            var scanCommand = connection.CreateCommand();
            scanCommand.Transaction = (SqliteTransaction)transaction;
            scanCommand.CommandText = """
                INSERT INTO ScanSnapshots(ScanTimeUtc, DeviceCount, IsLatest)
                VALUES($scanTime, $deviceCount, 1);
                SELECT last_insert_rowid();
                """;
            scanCommand.Parameters.AddWithValue("$scanTime", scanTimeUtc);
            scanCommand.Parameters.AddWithValue("$deviceCount", devices.Count);
            long scanId;
            await using (scanCommand)
            {
                scanId = Convert.ToInt64(await scanCommand.ExecuteScalarAsync(cancellationToken));
            }

            var observationCommand = connection.CreateCommand();
            observationCommand.Transaction = (SqliteTransaction)transaction;
            observationCommand.CommandText = """
                INSERT INTO DeviceObservations(
                    ScanId, ObservationOrdinal, DeviceId, Name, Manufacturer, Status,
                    ProblemCode, ProblemCodeEvidenceAvailable)
                VALUES(
                    $scanId, $ordinal, $deviceId, $name, $manufacturer, $status,
                    $problemCode, $problemCodeEvidenceAvailable);
                """;

            var scanIdParameter = observationCommand.Parameters.Add("$scanId", SqliteType.Integer);
            var ordinalParameter = observationCommand.Parameters.Add("$ordinal", SqliteType.Integer);
            var deviceIdParameter = observationCommand.Parameters.Add("$deviceId", SqliteType.Text);
            var nameParameter = observationCommand.Parameters.Add("$name", SqliteType.Text);
            var manufacturerParameter = observationCommand.Parameters.Add("$manufacturer", SqliteType.Text);
            var statusParameter = observationCommand.Parameters.Add("$status", SqliteType.Text);
            var problemCodeParameter = observationCommand.Parameters.Add("$problemCode", SqliteType.Integer);
            var evidenceParameter = observationCommand.Parameters.Add("$problemCodeEvidenceAvailable", SqliteType.Integer);
            scanIdParameter.Value = scanId;

            await using (observationCommand)
            {
                int ordinal = 0;
                foreach (var device in devices)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ordinalParameter.Value = ordinal++;
                    deviceIdParameter.Value = string.IsNullOrWhiteSpace(device.DeviceId)
                        ? DBNull.Value
                        : device.DeviceId;
                    nameParameter.Value = string.IsNullOrWhiteSpace(device.Name)
                        ? "Unknown device"
                        : device.Name;
                    manufacturerParameter.Value = (object?)device.Manufacturer ?? DBNull.Value;
                    statusParameter.Value = (object?)device.Status ?? DBNull.Value;
                    problemCodeParameter.Value = (object?)device.ProblemCode ?? DBNull.Value;
                    evidenceParameter.Value = device.ProblemCode.HasValue ? 1 : 0;

                    await observationCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            }

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

    public async Task<ScanSnapshot?> GetLatestScanAsync()
    {
        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ScanId, ScanTimeUtc, DeviceCount
                FROM ScanSnapshots
                WHERE IsLatest = 1;
                """;
            long scanId;
            string scanTimeUtc;
            int deviceCount;
            await using (command)
            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                {
                    return null;
                }

                scanId = reader.GetInt64(0);
                scanTimeUtc = reader.GetString(1);
                deviceCount = reader.GetInt32(2);
            }

            return await ReadSnapshotAsync(connection, scanId, scanTimeUtc, deviceCount);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[DatabaseService] GetLatestScanAsync failed: {ex}");
            throw;
        }
    }

    public async Task<List<ScanSnapshot>> GetScanHistoryAsync()
    {
        var scans = new List<(long Id, string Time, int Count)>();
        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ScanId, ScanTimeUtc, DeviceCount
                FROM ScanSnapshots
                ORDER BY ScanId DESC;
                """;
            await using (command)
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    scans.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2)));
                }
            }

            var snapshots = new List<ScanSnapshot>(scans.Count);
            foreach (var scan in scans)
            {
                snapshots.Add(await ReadSnapshotAsync(connection, scan.Id, scan.Time, scan.Count));
            }

            return snapshots;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[DatabaseService] GetScanHistoryAsync failed: {ex}");
            throw;
        }
    }

    public async Task<List<DeviceInfo>> GetCachedDevicesAsync()
    {
        var latestScan = await GetLatestScanAsync();
        if (latestScan is not null)
        {
            return latestScan.Observations.ToList();
        }

        return await ReadLegacyDevicesAsync(
            "SELECT Name, DeviceId, Manufacturer, Status, ProblemCode, ProblemCodeEvidenceAvailable FROM Devices ORDER BY Name");
    }

    public async Task<List<DeviceInfo>> GetActiveDevicesAsync(string scanTimeUtc)
    {
        var scan = await GetScanByTimeAsync(scanTimeUtc);
        if (scan is not null)
        {
            return scan.Observations.ToList();
        }

        return await ReadLegacyDevicesAsync(
            "SELECT Name, DeviceId, Manufacturer, Status, ProblemCode, ProblemCodeEvidenceAvailable FROM Devices WHERE LastSeenUtc = $seen ORDER BY Name",
            ("$seen", scanTimeUtc));
    }

    public async Task<List<DeviceInfo>> GetHistoricalDevicesAsync(string currentScanTimeUtc)
    {
        var history = await GetScanHistoryAsync();
        if (history.Count > 0)
        {
            return history
                .Where(scan => string.CompareOrdinal(scan.ScanTimeUtc, currentScanTimeUtc) < 0)
                .SelectMany(scan => scan.Observations)
                .ToList();
        }

        return await ReadLegacyDevicesAsync(
            "SELECT Name, DeviceId, Manufacturer, Status, ProblemCode, ProblemCodeEvidenceAvailable FROM Devices WHERE LastSeenUtc < $seen ORDER BY Name",
            ("$seen", currentScanTimeUtc));
    }

    private async Task<ScanSnapshot?> GetScanByTimeAsync(string scanTimeUtc)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ScanId, ScanTimeUtc, DeviceCount
            FROM ScanSnapshots
            WHERE ScanTimeUtc = $scanTime
            ORDER BY ScanId DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$scanTime", scanTimeUtc);
        long scanId;
        string matchedScanTimeUtc;
        int deviceCount;
        await using (command)
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                return null;
            }

            scanId = reader.GetInt64(0);
            matchedScanTimeUtc = reader.GetString(1);
            deviceCount = reader.GetInt32(2);
        }

        return await ReadSnapshotAsync(connection, scanId, matchedScanTimeUtc, deviceCount);
    }

    private async Task<List<DeviceInfo>> ReadLegacyDevicesAsync(
        string query,
        (string Name, object Value)? parameter = null)
    {
        var devices = new List<DeviceInfo>();
        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = query;
            if (parameter is { } value)
            {
                command.Parameters.AddWithValue(value.Name, value.Value);
            }

            await using (command)
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    devices.Add(new DeviceInfo
                    {
                        Name = reader.IsDBNull(0) ? "Unknown device" : reader.GetString(0),
                        DeviceId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Manufacturer = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        Status = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        ProblemCode = ReadProblemCode(reader, 4, 5)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[DatabaseService] Legacy device read failed: {ex}");
            throw;
        }

        return devices;
    }

    private async Task<ScanSnapshot> ReadSnapshotAsync(
        SqliteConnection connection,
        long scanId,
        string scanTimeUtc,
        int deviceCount)
    {
        var observations = new List<DeviceInfo>(deviceCount);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DeviceId, Name, Manufacturer, Status, ProblemCode, ProblemCodeEvidenceAvailable
            FROM DeviceObservations
            WHERE ScanId = $scanId
            ORDER BY ObservationOrdinal;
            """;
        command.Parameters.AddWithValue("$scanId", scanId);
        await using (command)
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                observations.Add(new DeviceInfo
                {
                    DeviceId = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Name = reader.IsDBNull(1) ? "Unknown device" : reader.GetString(1),
                    Manufacturer = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Status = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ProblemCode = ReadProblemCode(reader, 4, 5)
                });
            }
        }

        if (observations.Count != deviceCount)
        {
            throw new InvalidDataException(
                $"Scan {scanId} declares {deviceCount} observations but contains {observations.Count}.");
        }

        return new ScanSnapshot(scanId, scanTimeUtc, deviceCount, observations);
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using (command)
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static uint? ReadProblemCode(SqliteDataReader reader, int problemCodeIndex, int evidenceIndex) =>
        reader.IsDBNull(problemCodeIndex) || reader.GetInt64(evidenceIndex) == 0
            ? null
            : Convert.ToUInt32(reader.GetInt64(problemCodeIndex));
}
