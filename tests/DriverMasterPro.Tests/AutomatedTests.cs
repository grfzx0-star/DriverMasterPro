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

        var cached = await _database.GetCachedDevicesAsync();

        var device = Assert.Single(cached);
        Assert.StartsWith("OBSERVATION:", device.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveScan_DoesNotTreatUnidentifiedObservationAsPersistentDeviceIdentity()
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
        var firstObservationId = Assert.Single(await _database.GetCachedDevicesAsync()).DeviceId;

        await _database.SaveScanAsync(new[] { unidentifiedDevice });
        var observations = await _database.GetCachedDevicesAsync();

        Assert.Equal(2, observations.Count);
        Assert.All(observations, device =>
            Assert.StartsWith("OBSERVATION:", device.DeviceId, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(firstObservationId, observations.Select(device => device.DeviceId));
        Assert.Equal(2, observations.Select(device => device.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
        var cached = await _database.GetCachedDevicesAsync();

        Assert.Equal(2, cached.Count);
        Assert.Equal(2, cached.Select(device => device.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(cached, device => device.DeviceId == "TEST_DUPLICATE_ID");
        Assert.Contains(cached, device =>
            device.DeviceId.StartsWith("TEST_DUPLICATE_ID#DUP-OBSERVATION:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveScan_UpdatesExistingDeviceWithoutDuplicatingIdentity()
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
    public async Task SaveScan_RemovalPreservesHistoricalTimestampAndRecord()
    {
        var mouse = CreateDevice("USB Mouse", @"HID\VID_046D&PID_C077\M1", "Logitech");
        var keyboard = CreateDevice("USB Keyboard", @"HID\VID_046D&PID_C31C\K1", "Logitech");
        var flashDrive = CreateDevice("Removable Flash", @"USBSTOR\DISK&VEN_SANDISK\F1", "SanDisk");

        await _database.SaveScanAsync(new[] { mouse, keyboard, flashDrive });
        var flashSeenOnFirstScan = await GetLastSeenUtcAsync(flashDrive.DeviceId);
        var mouseSeenOnFirstScan = await GetLastSeenUtcAsync(mouse.DeviceId);
        await Task.Delay(50);

        await _database.SaveScanAsync(new[] { mouse, keyboard });

        var cached = await _database.GetCachedDevicesAsync();
        Assert.Equal(3, cached.Count);
        var flashRecord = Assert.Single(cached, device => device.DeviceId == flashDrive.DeviceId);
        Assert.Equal("OK", flashRecord.Status);

        var flashSeenAfterRemoval = await GetLastSeenUtcAsync(flashDrive.DeviceId);
        var mouseSeenAfterSecondScan = await GetLastSeenUtcAsync(mouse.DeviceId);
        Assert.Equal(flashSeenOnFirstScan, flashSeenAfterRemoval);
        Assert.True(
            DateTimeOffset.Parse(flashSeenAfterRemoval, CultureInfo.InvariantCulture) <
            DateTimeOffset.Parse(mouseSeenAfterSecondScan, CultureInfo.InvariantCulture));
        Assert.True(
            DateTimeOffset.Parse(mouseSeenAfterSecondScan, CultureInfo.InvariantCulture) >
            DateTimeOffset.Parse(mouseSeenOnFirstScan, CultureInfo.InvariantCulture));

        var historicalDevices = await _database.GetHistoricalDevicesAsync(mouseSeenAfterSecondScan);
        Assert.Contains(flashDrive.DeviceId, historicalDevices.Select(device => device.DeviceId));
        Assert.DoesNotContain(mouse.DeviceId, historicalDevices.Select(device => device.DeviceId));
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

    private async Task<string> GetLastSeenUtcAsync(string deviceId)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT LastSeenUtc FROM Devices WHERE DeviceId = $id";
        command.Parameters.AddWithValue("$id", deviceId);

        return (string?)await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException($"No LastSeenUtc value was found for device '{deviceId}'.");
    }

    private static DeviceInfo CreateDevice(
        string name,
        string deviceId,
        string manufacturer,
        string status = "OK",
        uint problemCode = 0) =>
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
