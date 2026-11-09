using System.Globalization;
using System.IO;
using System.Threading;
using DriverMasterPro;
using DriverMasterPro.Models;
using DriverMasterPro.Services;
using DriverMasterPro.Services.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DriverMasterPro.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task Services_AreRegisteredWithExpectedLifetimes()
    {
        using var host = App.CreateHostBuilder().Build();
        await host.StartAsync();

        var services = host.Services;
        var firstDatabase = services.GetRequiredService<IDatabaseService>();
        var secondDatabase = services.GetRequiredService<IDatabaseService>();
        var firstScanner = services.GetRequiredService<IDeviceScanner>();
        var secondScanner = services.GetRequiredService<IDeviceScanner>();

        Assert.Same(firstDatabase, secondDatabase);
        Assert.NotSame(firstScanner, secondScanner);

        await host.StopAsync();
    }

    [Fact]
    public async Task MainWindow_CanBeResolvedFromDependencyInjectionOnStaThread()
    {
        using var host = App.CreateHostBuilder().Build();
        await host.StartAsync();

        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.NotNull(host.Services.GetRequiredService<MainWindow>());
            }
            catch (Exception exception)
            {
                threadException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        await host.StopAsync();
        Assert.Null(threadException);
    }
}

public sealed class DatabaseServiceTests : IAsyncLifetime
{
    private string _databasePath = null!;
    private DatabaseService _database = null!;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dmp_test_{Guid.NewGuid():N}.db");
        _database = new DatabaseService(_databasePath);
        await _database.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task SaveScan_PreservesDevicesWithDuplicateNames()
    {
        var devices = new[]
        {
            CreateDevice("Generic USB Hub", @"USB\VID_045E&PID_0001\HUB_01", "Microsoft"),
            CreateDevice("Generic USB Hub", @"USB\VID_045E&PID_0001\HUB_02", "Microsoft"),
            CreateDevice("Generic USB Hub", @"USB\VID_045E&PID_0001\HUB_03", "Microsoft")
        };

        await _database.SaveScanAsync(devices);
        var cached = await _database.GetCachedDevicesAsync();

        Assert.Equal(3, cached.Count);
        Assert.Equal(3, cached.Select(device => device.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task SaveScan_PreservesDistinctInstancesOfTheSameHardwareModel()
    {
        var devices = new[]
        {
            CreateDevice("Ultra USB 3.0", @"USB\VID_0781&PID_5583\SERIAL_A1", "SanDisk"),
            CreateDevice("Ultra USB 3.0", @"USB\VID_0781&PID_5583\SERIAL_B2", "SanDisk")
        };

        await _database.SaveScanAsync(devices);
        var cachedIds = (await _database.GetCachedDevicesAsync()).Select(device => device.DeviceId).ToArray();

        Assert.Equal(2, cachedIds.Length);
        Assert.Contains(@"USB\VID_0781&PID_5583\SERIAL_A1", cachedIds);
        Assert.Contains(@"USB\VID_0781&PID_5583\SERIAL_B2", cachedIds);
    }

    [Fact]
    public async Task SaveScan_RepresentsUnidentifiedDevicesAsObservations()
    {
        await _database.SaveScanAsync(new[]
        {
            new DeviceInfo
            {
                Name = "Legacy Unknown Entity",
                DeviceId = "",
                Manufacturer = "Unknown",
                Status = "Degraded",
                ProblemCode = 28
            }
        });

        var snapshot = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());

        var device = Assert.Single(snapshot.Observations);
        Assert.Equal("", device.DeviceId);
    }

    [Fact]
    public async Task SaveScan_StoresUnidentifiedObservationsInSeparateScansWithoutCorrelatingThem()
    {
        var unidentifiedDevice = new DeviceInfo
        {
            Name = "Legacy Unknown Entity",
            DeviceId = "",
            Manufacturer = "Unknown",
            Status = "Degraded",
            ProblemCode = 28
        };

        await _database.SaveScanAsync(new[] { unidentifiedDevice });
        var firstScan = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());

        await _database.SaveScanAsync(new[] { unidentifiedDevice });
        var secondScan = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());
        var history = await _database.GetScanHistoryAsync();

        Assert.NotEqual(firstScan.ScanId, secondScan.ScanId);
        Assert.Equal(2, history.Count);
        Assert.Equal("", Assert.Single(firstScan.Observations).DeviceId);
        Assert.Equal("", Assert.Single(secondScan.Observations).DeviceId);
    }

    [Fact]
    public async Task SaveScan_PreservesRepeatedPersistentIdAsSeparateObservation()
    {
        var duplicateDevices = new[]
        {
            CreateDevice("Repeated Device", "TEST_DUPLICATE_ID", "Test Manufacturer"),
            CreateDevice("Repeated Device", "TEST_DUPLICATE_ID", "Test Manufacturer")
        };

        await _database.SaveScanAsync(duplicateDevices);
        var snapshot = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());
        var cached = snapshot.Observations;

        Assert.Equal(2, cached.Count);
        Assert.All(cached, device => Assert.Equal("TEST_DUPLICATE_ID", device.DeviceId));
        Assert.Equal(2, snapshot.DeviceCount);
    }

    [Fact]
    public async Task SaveScan_PreservesMissingProblemCodeAsUnknownEvidence()
    {
        await _database.SaveScanAsync(new[]
        {
            new DeviceInfo
            {
                Name = "Incomplete WMI Evidence",
                DeviceId = "TEST_MISSING_PROBLEM_CODE",
                Manufacturer = "Test",
                Status = "OK",
                ProblemCode = null
            }
        });

        var device = Assert.Single(await _database.GetCachedDevicesAsync());

        Assert.Null(device.ProblemCode);
        Assert.Equal(
            DiagnosticOutcome.Unknown,
            new RuleBasedDiagnosticsService().Resolve(device.ProblemCode, deviceStatus: device.Status).Outcome);
    }

    [Fact]
    public async Task SaveScan_RoundTripsObservedZeroWithEvidenceAvailable()
    {
        await _database.SaveScanAsync(new[]
        {
            CreateDevice("Observed zero", "TEST_ZERO_EVIDENCE", "Test", problemCode: 0)
        });

        var snapshot = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());
        var device = Assert.Single(snapshot.Observations);

        Assert.Equal(0u, device.ProblemCode);
        Assert.Equal(DiagnosticOutcome.Healthy,
            new RuleBasedDiagnosticsService().Resolve(device.ProblemCode, deviceStatus: device.Status).Outcome);

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ProblemCode, ProblemCodeEvidenceAvailable
            FROM DeviceObservations
            WHERE ScanId = $scanId AND ObservationOrdinal = 0;
            """;
        command.Parameters.AddWithValue("$scanId", snapshot.ScanId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task SaveScan_PreservesRepeatedDeviceIdsAcrossScansAsIndependentSnapshots()
    {
        var firstDevices = new[]
        {
            CreateDevice("Repeated ID A", "REPEATED_ID", "Maker A"),
            CreateDevice("Repeated ID B", "REPEATED_ID", "Maker B")
        };
        await _database.SaveScanAsync(firstDevices);
        var firstScan = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());

        var secondDevices = new[]
        {
            CreateDevice("New repeated ID A", "REPEATED_ID", "Maker A", problemCode: 10),
            CreateDevice("New repeated ID B", "REPEATED_ID", "Maker B", problemCode: 10)
        };
        await _database.SaveScanAsync(secondDevices);

        var secondScan = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());
        var history = await _database.GetScanHistoryAsync();

        Assert.NotEqual(firstScan.ScanId, secondScan.ScanId);
        Assert.Equal(2, history.Count);
        Assert.Equal(2, firstScan.Observations.Count);
        Assert.All(firstScan.Observations, device => Assert.Equal("REPEATED_ID", device.DeviceId));
        Assert.Equal(new[] { "Repeated ID A", "Repeated ID B" },
            firstScan.Observations.Select(device => device.Name));
        Assert.Equal(2, secondScan.Observations.Count);
        Assert.All(secondScan.Observations, device => Assert.Equal("REPEATED_ID", device.DeviceId));
        Assert.Equal(new[] { "New repeated ID A", "New repeated ID B" },
            secondScan.Observations.Select(device => device.Name));
    }

    [Fact]
    public async Task Initialize_IsIdempotentAndPreservesLegacyDevicesAndScanHistory()
    {
        var legacyDatabasePath = Path.Combine(Path.GetTempPath(), $"dmp_legacy_test_{Guid.NewGuid():N}.db");
        var legacyScanTime = "2024-01-02T03:04:05.0000000Z";
        var laterScanTime = "2024-02-03T04:05:06.0000000Z";
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={legacyDatabasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE Devices (
                        DeviceId TEXT PRIMARY KEY,
                        Name TEXT NOT NULL,
                        Manufacturer TEXT,
                        Status TEXT,
                        ProblemCode INTEGER,
                        LastSeenUtc TEXT NOT NULL
                    );
                    CREATE TABLE ScanHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ScanTimeUtc TEXT NOT NULL,
                        DeviceCount INTEGER NOT NULL
                    );
                    CREATE INDEX IX_Devices_LastSeenUtc ON Devices(LastSeenUtc);
                    INSERT INTO Devices(DeviceId, Name, Manufacturer, Status, ProblemCode, LastSeenUtc)
                    VALUES('LEGACY_ZERO', 'Legacy healthy candidate', 'Legacy Maker Zero', 'OK', 0, $legacyScanTime);
                    INSERT INTO Devices(DeviceId, Name, Manufacturer, Status, ProblemCode, LastSeenUtc)
                    VALUES('LEGACY_ERROR', 'Legacy error', 'Legacy Maker Error', 'Error', 10, $laterScanTime);
                    INSERT INTO Devices(DeviceId, Name, Manufacturer, Status, ProblemCode, LastSeenUtc)
                    VALUES('LEGACY_NULL', 'Legacy missing code', 'Legacy Maker Null', 'Unknown', NULL, $laterScanTime);
                    INSERT INTO ScanHistory(Id, ScanTimeUtc, DeviceCount)
                    VALUES(41, $legacyScanTime, 3);
                    INSERT INTO ScanHistory(Id, ScanTimeUtc, DeviceCount)
                    VALUES(42, $laterScanTime, 7);
                    """;
                command.Parameters.AddWithValue("$legacyScanTime", legacyScanTime);
                command.Parameters.AddWithValue("$laterScanTime", laterScanTime);
                await command.ExecuteNonQueryAsync();
            }

            var legacyDatabase = new DatabaseService(legacyDatabasePath);
            await legacyDatabase.InitializeAsync();
            await legacyDatabase.InitializeAsync();

            var cached = await legacyDatabase.GetCachedDevicesAsync();

            Assert.Equal(3, cached.Count);
            var zeroCodeDevice = Assert.Single(cached, device => device.DeviceId == "LEGACY_ZERO");
            Assert.Equal("Legacy healthy candidate", zeroCodeDevice.Name);
            Assert.Equal("Legacy Maker Zero", zeroCodeDevice.Manufacturer);
            Assert.Equal("OK", zeroCodeDevice.Status);
            Assert.Null(zeroCodeDevice.ProblemCode);
            Assert.Equal(
                DiagnosticOutcome.Unknown,
                new RuleBasedDiagnosticsService().Resolve(zeroCodeDevice.ProblemCode, deviceStatus: zeroCodeDevice.Status).Outcome);

            var nonzeroCodeDevice = Assert.Single(cached, device => device.DeviceId == "LEGACY_ERROR");
            Assert.Equal("Legacy error", nonzeroCodeDevice.Name);
            Assert.Equal("Legacy Maker Error", nonzeroCodeDevice.Manufacturer);
            Assert.Equal("Error", nonzeroCodeDevice.Status);
            Assert.Equal<uint?>(10u, nonzeroCodeDevice.ProblemCode);

            var nullCodeDevice = Assert.Single(cached, device => device.DeviceId == "LEGACY_NULL");
            Assert.Equal("Legacy missing code", nullCodeDevice.Name);
            Assert.Equal("Legacy Maker Null", nullCodeDevice.Manufacturer);
            Assert.Equal("Unknown", nullCodeDevice.Status);
            Assert.Null(nullCodeDevice.ProblemCode);

            await using var verifyConnection = new SqliteConnection($"Data Source={legacyDatabasePath}");
            await verifyConnection.OpenAsync();
            await using (var verifyCommand = verifyConnection.CreateCommand())
            {
                verifyCommand.CommandText = """
                    SELECT DeviceId, Name, Manufacturer, Status, ProblemCode,
                           ProblemCodeEvidenceAvailable, LastSeenUtc
                    FROM Devices ORDER BY DeviceId
                    """;
                await using var reader = await verifyCommand.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("LEGACY_ERROR", reader.GetString(0));
                Assert.Equal("Legacy error", reader.GetString(1));
                Assert.Equal("Legacy Maker Error", reader.GetString(2));
                Assert.Equal("Error", reader.GetString(3));
                Assert.Equal(10L, reader.GetInt64(4));
                Assert.Equal(1L, reader.GetInt64(5));
                Assert.Equal(laterScanTime, reader.GetString(6));

                Assert.True(await reader.ReadAsync());
                Assert.Equal("LEGACY_NULL", reader.GetString(0));
                Assert.Equal("Legacy missing code", reader.GetString(1));
                Assert.Equal("Legacy Maker Null", reader.GetString(2));
                Assert.Equal("Unknown", reader.GetString(3));
                Assert.True(reader.IsDBNull(4));
                Assert.Equal(1L, reader.GetInt64(5));
                Assert.Equal(laterScanTime, reader.GetString(6));

                Assert.True(await reader.ReadAsync());
                Assert.Equal("LEGACY_ZERO", reader.GetString(0));
                Assert.Equal("Legacy healthy candidate", reader.GetString(1));
                Assert.Equal("Legacy Maker Zero", reader.GetString(2));
                Assert.Equal("OK", reader.GetString(3));
                Assert.Equal(0L, reader.GetInt64(4));
                Assert.Equal(0L, reader.GetInt64(5));
                Assert.Equal(legacyScanTime, reader.GetString(6));
                Assert.False(await reader.ReadAsync());
            }

            await using (var historyCommand = verifyConnection.CreateCommand())
            {
                historyCommand.CommandText = "SELECT Id, ScanTimeUtc, DeviceCount FROM ScanHistory ORDER BY Id";
                await using var reader = await historyCommand.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(41L, reader.GetInt64(0));
                Assert.Equal(legacyScanTime, reader.GetString(1));
                Assert.Equal(3L, reader.GetInt64(2));
                Assert.True(await reader.ReadAsync());
                Assert.Equal(42L, reader.GetInt64(0));
                Assert.Equal(laterScanTime, reader.GetString(1));
                Assert.Equal(7L, reader.GetInt64(2));
                Assert.False(await reader.ReadAsync());
            }

            await using (var schemaCommand = verifyConnection.CreateCommand())
            {
                schemaCommand.CommandText = """
                    SELECT name FROM pragma_table_info('Devices') ORDER BY cid;
                    """;
                var columnNames = new List<string>();
                await using (var reader = await schemaCommand.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        columnNames.Add(reader.GetString(0));
                    }
                }

                Assert.Equal(
                    new[]
                    {
                        "DeviceId", "Name", "Manufacturer", "Status", "ProblemCode",
                        "LastSeenUtc", "ProblemCodeEvidenceAvailable"
                    },
                    columnNames);
                Assert.Equal(1, columnNames.Count(name => name == "ProblemCodeEvidenceAvailable"));

                schemaCommand.CommandText = """
                    SELECT type || ':' || name
                    FROM sqlite_master
                    WHERE name NOT LIKE 'sqlite_%'
                    ORDER BY type, name;
                    """;
                var schemaObjects = new List<string>();
                await using (var reader = await schemaCommand.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        schemaObjects.Add(reader.GetString(0));
                    }
                }

                Assert.Equal(
                    new[]
                    {
                        "index:IX_DeviceObservations_DeviceId",
                        "index:IX_Devices_LastSeenUtc",
                        "index:IX_ScanSnapshots_OneLatest",
                        "table:DeviceObservations",
                        "table:Devices",
                        "table:ScanHistory",
                        "table:ScanSnapshots"
                    },
                    schemaObjects);
            }

            await using (var versionCommand = verifyConnection.CreateCommand())
            {
                versionCommand.CommandText = "PRAGMA user_version;";
                Assert.Equal(1L, await versionCommand.ExecuteScalarAsync());
            }

            Assert.Null(await legacyDatabase.GetLatestScanAsync());
            Assert.Empty(await legacyDatabase.GetScanHistoryAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(legacyDatabasePath))
            {
                File.Delete(legacyDatabasePath);
            }
        }
    }

    [Fact]
    public async Task Initialize_PreservesPreviouslyAvailableZeroEvidenceWhenVersioningExistingSchema()
    {
        var existingDatabasePath = Path.Combine(Path.GetTempPath(), $"dmp_existing_schema_{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={existingDatabasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE Devices (
                        DeviceId TEXT PRIMARY KEY,
                        Name TEXT NOT NULL,
                        Manufacturer TEXT,
                        Status TEXT,
                        ProblemCode INTEGER,
                        ProblemCodeEvidenceAvailable INTEGER NOT NULL DEFAULT 1,
                        LastSeenUtc TEXT NOT NULL
                    );
                    CREATE TABLE ScanHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ScanTimeUtc TEXT NOT NULL,
                        DeviceCount INTEGER NOT NULL
                    );
                    CREATE INDEX IX_Devices_LastSeenUtc ON Devices(LastSeenUtc);
                    INSERT INTO Devices(
                        DeviceId, Name, Manufacturer, Status, ProblemCode,
                        ProblemCodeEvidenceAvailable, LastSeenUtc)
                    VALUES('OBSERVED_ZERO', 'Observed zero', 'Test', 'OK', 0, 1, '2026-01-01T00:00:00.0000000Z');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var existingDatabase = new DatabaseService(existingDatabasePath);
            await existingDatabase.InitializeAsync();
            await existingDatabase.InitializeAsync();

            var device = Assert.Single(await existingDatabase.GetCachedDevicesAsync());
            Assert.Equal("OBSERVED_ZERO", device.DeviceId);
            Assert.Equal(0u, device.ProblemCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(existingDatabasePath))
            {
                File.Delete(existingDatabasePath);
            }
        }
    }

    [Fact]
    public async Task SaveScan_PreservesSameNameUnidentifiedDevicesAsSeparateObservations()
    {
        await _database.SaveScanAsync(new[]
        {
            CreateDevice("Generic device", "", "Unknown"),
            CreateDevice("Generic device", "", "Unknown")
        });

        var snapshot = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());

        Assert.Equal(2, snapshot.Observations.Count);
        Assert.All(snapshot.Observations, device => Assert.Equal("", device.DeviceId));
        Assert.Equal(2, snapshot.DeviceCount);
    }

    [Fact]
    public async Task SaveScan_StoresUpdatedDeviceInLatestSnapshot()
    {
        var wifi = CreateDevice("Intel Wi-Fi 6", @"PCI\VEN_8086&DEV_2723\1", "Intel");
        var audio = CreateDevice("Realtek Audio", @"HDAUDIO\FUNC_01&VEN_10EC\1", "Realtek");
        await _database.SaveScanAsync(new[] { wifi, audio });

        var updatedWifi = CreateDevice(wifi.Name, wifi.DeviceId, "Intel", "Error", 10);
        await _database.SaveScanAsync(new[] { updatedWifi, audio });
        var cached = await _database.GetCachedDevicesAsync();

        Assert.Equal(2, cached.Count);
        var persistedWifi = Assert.Single(cached, device => device.DeviceId == wifi.DeviceId);
        Assert.Equal("Error", persistedWifi.Status);
        Assert.Equal<uint?>(10u, persistedWifi.ProblemCode);
    }

    [Fact]
    public async Task SaveScan_ReversedEnumerationOrderDoesNotOverwriteOtherDevices()
    {
        var display = CreateDevice("Display Controller", @"PCI\VEN_10DE&DEV_1C03\A", "NVIDIA");
        var ethernet = CreateDevice("Ethernet Controller", @"PCI\VEN_8086&DEV_15F3\B", "Intel");

        await _database.SaveScanAsync(new[] { display, ethernet });
        await _database.SaveScanAsync(new[] { ethernet, display });
        var cached = await _database.GetCachedDevicesAsync();

        Assert.Equal(2, cached.Count);
        Assert.Equal("NVIDIA", Assert.Single(cached, device => device.DeviceId == display.DeviceId).Manufacturer);
        Assert.Equal("Intel", Assert.Single(cached, device => device.DeviceId == ethernet.DeviceId).Manufacturer);
    }

    [Fact]
    public async Task SaveScan_NewScanLeavesHistoricalSnapshotUnchanged()
    {
        var mouse = CreateDevice("USB Mouse", @"HID\VID_046D&PID_C077\M1", "Logitech");
        var keyboard = CreateDevice("USB Keyboard", @"HID\VID_046D&PID_C31C\K1", "Logitech");
        var flashDrive = CreateDevice("Removable Flash", @"USBSTOR\DISK&VEN_SANDISK\F1", "SanDisk");

        await _database.SaveScanAsync(new[] { mouse, keyboard, flashDrive });
        var firstScan = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());

        await _database.SaveScanAsync(new[] { mouse, keyboard });

        var secondScan = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());
        var history = await _database.GetScanHistoryAsync();
        var cached = await _database.GetCachedDevicesAsync();

        Assert.NotEqual(firstScan.ScanId, secondScan.ScanId);
        Assert.Equal(2, secondScan.DeviceCount);
        Assert.Equal(2, cached.Count);
        Assert.DoesNotContain(cached, device => device.DeviceId == flashDrive.DeviceId);
        Assert.Equal(3, firstScan.DeviceCount);
        Assert.Contains(firstScan.Observations, device => device.DeviceId == flashDrive.DeviceId);
        Assert.Equal(new[] { "Removable Flash", "USB Keyboard", "USB Mouse" },
            firstScan.Observations.Select(device => device.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(2, history.Count);
        Assert.Equal(firstScan.Observations.Select(device => (device.DeviceId, device.Name, device.ProblemCode)),
            history.Single(scan => scan.ScanId == firstScan.ScanId).Observations
                .Select(device => (device.DeviceId, device.Name, device.ProblemCode)));
        Assert.Contains(flashDrive.DeviceId,
            (await _database.GetHistoricalDevicesAsync(secondScan.ScanTimeUtc)).Select(device => device.DeviceId));
    }

    [Fact]
    public async Task SaveScan_FailureRollsBackSnapshotAndAllowsFollowingScan()
    {
        var original = CreateDevice("Original", "ORIGINAL_ID", "Original Maker");
        await _database.SaveScanAsync(new[] { original });
        var originalLatest = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());

        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER FailSecondObservation
                BEFORE INSERT ON DeviceObservations
                WHEN NEW.ObservationOrdinal = 1
                BEGIN
                    SELECT RAISE(ABORT, 'injected observation failure');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var failedDevices = new[]
        {
            CreateDevice("Will Roll Back A", "FAIL_A", "Test"),
            CreateDevice("Will Roll Back B", "FAIL_B", "Test")
        };
        await Assert.ThrowsAsync<SqliteException>(() => _database.SaveScanAsync(failedDevices));

        var afterFailureHistory = await _database.GetScanHistoryAsync();
        var afterFailureLatest = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());
        Assert.Single(afterFailureHistory);
        Assert.Equal(originalLatest.ScanId, afterFailureLatest.ScanId);
        Assert.Equal(new[] { "ORIGINAL_ID" },
            afterFailureLatest.Observations.Select(device => device.DeviceId));

        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TRIGGER FailSecondObservation;";
            await command.ExecuteNonQueryAsync();
        }

        var recoveredDevice = CreateDevice("Recovered", "RECOVERED_ID", "Test");
        await _database.SaveScanAsync(new[] { recoveredDevice });

        var recoveredHistory = await _database.GetScanHistoryAsync();
        var recoveredLatest = Assert.IsType<ScanSnapshot>(await _database.GetLatestScanAsync());
        Assert.Equal(2, recoveredHistory.Count);
        Assert.Equal("Recovered", Assert.Single(recoveredLatest.Observations).Name);
        Assert.Equal(recoveredHistory[0].ScanId, recoveredLatest.ScanId);
    }

    [Fact]
    public async Task DatabaseCanBeOpenedExclusivelyAfterScanResourcesAreReleased()
    {
        await _database.SaveScanAsync(new[]
        {
            CreateDevice("Disposal Verification Device", "TEST_DISPOSE_01", "Test Mfg")
        });
        SqliteConnection.ClearAllPools();

        using var stream = File.Open(_databasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.True(stream.Length > 0);
    }

    private static DeviceInfo CreateDevice(
        string name,
        string deviceId,
        string manufacturer,
        string status = "OK",
        uint? problemCode = 0) =>
        new()
        {
            Name = name,
            DeviceId = deviceId,
            Manufacturer = manufacturer,
            Status = status,
            ProblemCode = problemCode
        };
}

public sealed class DeviceScannerTests
{
    [Fact]
    public async Task ScanAsync_ImmediatelyCancelsWhenTokenIsAlreadyCancelled()
    {
        var provider = new FakeDeviceInventoryProvider(new[]
        {
            new DeviceInfo { Name = "Should not run" }
        });
        var scanner = new DeviceScanner(provider);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(cancellation.Token));
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task ScanAsync_SortsProviderResultsAlphabeticallyWithoutHardware()
    {
        var expected = new[]
        {
            new DeviceInfo { Name = "zeta device", DeviceId = "Z" },
            new DeviceInfo { Name = "Alpha device", DeviceId = "A" },
            new DeviceInfo { Name = "Beta device", DeviceId = "B" }
        };
        var scanner = new DeviceScanner(new FakeDeviceInventoryProvider(expected));

        var devices = await scanner.ScanAsync();

        Assert.Equal(new[] { "Alpha device", "Beta device", "zeta device" }, devices.Select(device => device.Name));
    }

    [Fact]
    public async Task ScanAsync_AcceptsSuccessfullyQueriedEmptyInventory()
    {
        var scanner = new DeviceScanner(new FakeDeviceInventoryProvider(Array.Empty<DeviceInfo>()));

        var devices = await scanner.ScanAsync();

        Assert.Empty(devices);
    }

    [Fact]
    public async Task ScanAsync_PropagatesAccessDeniedInsteadOfReturningSuccessfulScan()
    {
        var accessDenied = new UnauthorizedAccessException("WMI access denied.");
        var scanner = new DeviceScanner(new FakeDeviceInventoryProvider(accessDenied));

        var thrown = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scanner.ScanAsync());

        Assert.Same(accessDenied, thrown);
    }

    [Fact]
    public async Task ScanAsync_PropagatesWmiFailureThatWrapsAccessDenied()
    {
        var accessDenied = new UnauthorizedAccessException("WMI access denied.");
        var wmiFailure = new InvalidOperationException("WMI query failed.", accessDenied);
        var scanner = new DeviceScanner(new FakeDeviceInventoryProvider(wmiFailure));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync());

        Assert.Same(wmiFailure, thrown);
        Assert.Same(accessDenied, thrown.InnerException);
    }

    [Fact]
    [Trait("Category", "WmiIntegration")]
    [Trait("Requires", "Windows WMI service and permission to query Win32_PnPEntity; hardware inventory may be empty.")]
    public async Task ScanAsync_QueriesLiveWmiInventory()
    {
        var scanner = new DeviceScanner();

        var devices = await scanner.ScanAsync();

        Assert.NotNull(devices);
        Assert.Equal(
            devices.Select(device => device.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            devices.Select(device => device.Name));
    }

    private sealed class FakeDeviceInventoryProvider : IDeviceInventoryProvider
    {
        private readonly List<DeviceInfo>? _devices;
        private readonly Exception? _exception;

        public int CallCount { get; private set; }

        public FakeDeviceInventoryProvider(IEnumerable<DeviceInfo> devices)
        {
            _devices = devices.ToList();
        }

        public FakeDeviceInventoryProvider(Exception exception)
        {
            _exception = exception;
        }

        public List<DeviceInfo> GetDevices(CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            if (_exception is not null)
            {
                throw _exception;
            }

            return _devices ?? throw new InvalidOperationException("No fake inventory was configured.");
        }
    }
}
