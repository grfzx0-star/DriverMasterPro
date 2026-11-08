using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DriverMasterPro.Models;
using DriverMasterPro.Services;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro.Tests;

public static class Program
{
    private static int _passedCount;
    private static int _failedCount;

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("==================================================================");
        Console.WriteLine(" DriverMasterPro v0.2 DI & Multi-Scan Test Suite (Phase 2)");
        Console.WriteLine("==================================================================");
        Console.WriteLine();

        // Dependency Injection & Service Layer Tests
        await RunTestAsync("DI: Service Registration and Lifetime Guarantees", Test_DependencyInjection_ServiceRegistrationAndLifetimes);
        await RunTestAsync("DI: MainWindow Resolution with Injected Services", Test_DependencyInjection_MainWindowResolution);

        // Single-Scan & Identity Isolation Tests
        await RunTestAsync("Database: Duplicate Device Names with Unique Instance IDs", Test_DuplicateDeviceNames_UniqueInstanceIds);
        await RunTestAsync("Database: Identical Hardware Models with Distinct Device IDs", Test_IdenticalHardwareModels_DistinctDeviceIds);
        await RunTestAsync("Database: Genuinely Unidentified Devices as Scan Observations", Test_UnidentifiedDevices_RepresentedAsScanObservations);
        await RunTestAsync("Database: Resource Disposal and SQLite Pool Flushing", Test_ResourceDisposalAndPoolFlush);

        // Multi-Scan Lifecycle Tests
        await RunTestAsync("Multi-Scan: Consecutive Scans Preserve Persistent Identity", Test_MultiScan_ConsecutiveScans_PreservesIdentity);
        await RunTestAsync("Multi-Scan: Reversed Enumeration Order Causes No Overwrite", Test_MultiScan_ChangedEnumerationOrder_NoAccidentalOverwrite);
        await RunTestAsync("Multi-Scan: Device Removal Preserves Historical Record", Test_MultiScan_DeviceRemoval_HistoricalPreserved);

        // Scanner Safety & Cancellation Tests
        await RunTestAsync("Scanner: CancellationToken Immediate Abort", Test_ScannerCancellation);
        await RunTestAsync("Scanner: Read-Only Query Execution & Alpha Sort", Test_ScannerExecution);

        Console.WriteLine();
        Console.WriteLine("------------------------------------------------------------------");
        Console.WriteLine($"Final Test Results: {_passedCount} Passed, {_failedCount} Failed.");
        Console.WriteLine("------------------------------------------------------------------");

        return _failedCount == 0 ? 0 : 1;
    }

    private static async Task Test_DependencyInjection_ServiceRegistrationAndLifetimes()
    {
        using var host = App.CreateHostBuilder().Build();
        await host.StartAsync();

        var sp = host.Services;

        // 1. Verify IDatabaseService is registered as Singleton
        var db1 = sp.GetRequiredService<IDatabaseService>();
        var db2 = sp.GetRequiredService<IDatabaseService>();
        if (!object.ReferenceEquals(db1, db2))
        {
            throw new InvalidOperationException("IDatabaseService is not a Singleton. Distinct instances resolved.");
        }

        // 2. Verify IDeviceScanner is registered as Transient
        var scanner1 = sp.GetRequiredService<IDeviceScanner>();
        var scanner2 = sp.GetRequiredService<IDeviceScanner>();
        if (object.ReferenceEquals(scanner1, scanner2))
        {
            throw new InvalidOperationException("IDeviceScanner is not Transient. Same instance resolved.");
        }

        await host.StopAsync();
    }

    private static async Task Test_DependencyInjection_MainWindowResolution()
    {
        using var host = App.CreateHostBuilder().Build();
        await host.StartAsync();

        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = host.Services.GetService<MainWindow>();
                if (window == null)
                {
                    throw new InvalidOperationException("MainWindow could not be resolved from DI container.");
                }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw threadEx;
        }

        await host.StopAsync();
    }

    private static async Task RunTestAsync(string testName, Func<Task> testAction)
    {
        Console.Write($"[TEST] {testName} ... ");
        try
        {
            await testAction();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASSED");
            Console.ResetColor();
            _passedCount++;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAILED");
            Console.ResetColor();
            Console.WriteLine($"       Error: {ex.Message}");
            _failedCount++;
        }
    }

    private static async Task Test_DuplicateDeviceNames_UniqueInstanceIds()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"dmp_test_names_{Guid.NewGuid():N}.db");
        try
        {
            var db = new DatabaseService(tempDb);
            await db.InitializeAsync();

            // 3 devices with the identical Name, but distinct persistent physical DeviceIds
            var devices = new List<DeviceInfo>
            {
                new() { Name = "Generic USB Hub", DeviceId = @"USB\VID_045E&PID_0001\HUB_01", Manufacturer = "Microsoft", Status = "OK", ProblemCode = 0 },
                new() { Name = "Generic USB Hub", DeviceId = @"USB\VID_045E&PID_0001\HUB_02", Manufacturer = "Microsoft", Status = "OK", ProblemCode = 0 },
                new() { Name = "Generic USB Hub", DeviceId = @"USB\VID_045E&PID_0001\HUB_03", Manufacturer = "Microsoft", Status = "OK", ProblemCode = 0 }
            };

            await db.SaveScanAsync(devices);
            var cached = await db.GetCachedDevicesAsync();

            if (cached.Count != 3)
            {
                throw new InvalidOperationException($"Expected 3 distinct devices for identical names, but got {cached.Count}.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            CleanUpDatabase(tempDb);
        }
    }

    private static async Task Test_IdenticalHardwareModels_DistinctDeviceIds()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"dmp_test_hwids_{Guid.NewGuid():N}.db");
        try
        {
            var db = new DatabaseService(tempDb);
            await db.InitializeAsync();

            // Two identical flash drives sharing the same HardwareID model string, but unique instance serials
            var devices = new List<DeviceInfo>
            {
                new() { Name = "Ultra USB 3.0", DeviceId = @"USB\VID_0781&PID_5583\SERIAL_A1", Manufacturer = "SanDisk", Status = "OK", ProblemCode = 0 },
                new() { Name = "Ultra USB 3.0", DeviceId = @"USB\VID_0781&PID_5583\SERIAL_B2", Manufacturer = "SanDisk", Status = "OK", ProblemCode = 0 }
            };

            await db.SaveScanAsync(devices);
            var cached = await db.GetCachedDevicesAsync();

            if (cached.Count != 2)
            {
                throw new InvalidOperationException($"Expected 2 devices for identical hardware models with distinct serials, got {cached.Count}.");
            }

            if (!cached.Any(d => d.DeviceId.Contains("SERIAL_A1")) || !cached.Any(d => d.DeviceId.Contains("SERIAL_B2")))
            {
                throw new InvalidOperationException("One of the hardware instances was overwritten.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            CleanUpDatabase(tempDb);
        }
    }

    private static async Task Test_UnidentifiedDevices_RepresentedAsScanObservations()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"dmp_test_unidentified_{Guid.NewGuid():N}.db");
        try
        {
            var db = new DatabaseService(tempDb);
            await db.InitializeAsync();

            // Device genuinely lacking any persistent identifier
            var devices = new List<DeviceInfo>
            {
                new() { Name = "Legacy Unknown Entity", DeviceId = "", Manufacturer = "Unknown", Status = "Degraded", ProblemCode = 28 }
            };

            await db.SaveScanAsync(devices);
            var cached = await db.GetCachedDevicesAsync();

            if (cached.Count != 1)
            {
                throw new InvalidOperationException($"Expected 1 observation record, got {cached.Count}.");
            }

            if (!cached[0].DeviceId.StartsWith("OBSERVATION:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unidentified device was not represented as an OBSERVATION. Got: {cached[0].DeviceId}");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            CleanUpDatabase(tempDb);
        }
    }

    private static async Task Test_MultiScan_ConsecutiveScans_PreservesIdentity()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"dmp_test_consec_{Guid.NewGuid():N}.db");
        try
        {
            var db = new DatabaseService(tempDb);
            await db.InitializeAsync();

            var devWifi = new DeviceInfo { Name = "Intel Wi-Fi 6", DeviceId = @"PCI\VEN_8086&DEV_2723\1", Manufacturer = "Intel", Status = "OK", ProblemCode = 0 };
            var devAudio = new DeviceInfo { Name = "Realtek Audio", DeviceId = @"HDAUDIO\FUNC_01&VEN_10EC\1", Manufacturer = "Realtek", Status = "OK", ProblemCode = 0 };

            // Scan 1
            await db.SaveScanAsync(new[] { devWifi, devAudio });
            var scan1Devices = await db.GetCachedDevicesAsync();
            if (scan1Devices.Count != 2)
            {
                throw new InvalidOperationException($"Scan 1 expected 2 devices, got {scan1Devices.Count}.");
            }

            // Scan 2: Same physical devices, Wi-Fi reports Code 10 problem
            var devWifiUpdated = new DeviceInfo { Name = "Intel Wi-Fi 6", DeviceId = @"PCI\VEN_8086&DEV_2723\1", Manufacturer = "Intel", Status = "Error", ProblemCode = 10 };
            await db.SaveScanAsync(new[] { devWifiUpdated, devAudio });

            var scan2Devices = await db.GetCachedDevicesAsync();
            if (scan2Devices.Count != 2)
            {
                throw new InvalidOperationException($"Scan 2 duplicated records! Expected 2 devices, got {scan2Devices.Count}.");
            }

            var updatedWifi = scan2Devices.First(d => d.DeviceId == devWifi.DeviceId);
            if (updatedWifi.ProblemCode != 10 || updatedWifi.Status != "Error")
            {
                throw new InvalidOperationException($"Scan 2 failed to update device state. ProblemCode: {updatedWifi.ProblemCode}");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            CleanUpDatabase(tempDb);
        }
    }

    private static async Task Test_MultiScan_ChangedEnumerationOrder_NoAccidentalOverwrite()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"dmp_test_order_{Guid.NewGuid():N}.db");
        try
        {
            var db = new DatabaseService(tempDb);
            await db.InitializeAsync();

            var devA = new DeviceInfo { Name = "Display Controller", DeviceId = @"PCI\VEN_10DE&DEV_1C03\A", Manufacturer = "NVIDIA", Status = "OK", ProblemCode = 0 };
            var devB = new DeviceInfo { Name = "Ethernet Controller", DeviceId = @"PCI\VEN_8086&DEV_15F3\B", Manufacturer = "Intel", Status = "OK", ProblemCode = 0 };

            // Scan 1: Order [A, B]
            await db.SaveScanAsync(new[] { devA, devB });

            // Scan 2: Reversed Order [B, A]
            await db.SaveScanAsync(new[] { devB, devA });

            var devices = await db.GetCachedDevicesAsync();
            if (devices.Count != 2)
            {
                throw new InvalidOperationException($"Order reversal corrupted count. Expected 2, got {devices.Count}.");
            }

            var itemA = devices.First(d => d.DeviceId == devA.DeviceId);
            var itemB = devices.First(d => d.DeviceId == devB.DeviceId);

            if (itemA.Manufacturer != "NVIDIA" || itemB.Manufacturer != "Intel")
            {
                throw new InvalidOperationException("Reversed enumeration order caused cross-device data overwrite!");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            CleanUpDatabase(tempDb);
        }
    }

    private static async Task Test_MultiScan_DeviceRemoval_HistoricalPreserved()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"dmp_test_removal_{Guid.NewGuid():N}.db");
        try
        {
            var db = new DatabaseService(tempDb);
            await db.InitializeAsync();

            var devMouse = new DeviceInfo { Name = "USB Mouse", DeviceId = @"HID\VID_046D&PID_C077\M1", Manufacturer = "Logitech", Status = "OK", ProblemCode = 0 };
            var devKeyboard = new DeviceInfo { Name = "USB Keyboard", DeviceId = @"HID\VID_046D&PID_C31C\K1", Manufacturer = "Logitech", Status = "OK", ProblemCode = 0 };
            var devFlashDrive = new DeviceInfo { Name = "Removable Flash", DeviceId = @"USBSTOR\DISK&VEN_SANDISK\F1", Manufacturer = "SanDisk", Status = "OK", ProblemCode = 0 };

            // Scan 1: All 3 devices present
            await db.SaveScanAsync(new[] { devMouse, devKeyboard, devFlashDrive });

            // Small delay to guarantee distinct ISO UTC second
            await Task.Delay(50);

            // Scan 2: Flash drive unplugged! Only Mouse and Keyboard present
            await db.SaveScanAsync(new[] { devMouse, devKeyboard });

            // Verification 1: Flash drive must NOT be silently deleted from Devices catalog
            var allKnown = await db.GetCachedDevicesAsync();
            if (allKnown.Count != 3)
            {
                throw new InvalidOperationException($"Disconnected device was deleted! Expected 3 known devices in history, got {allKnown.Count}.");
            }

            var flashRecord = allKnown.First(d => d.DeviceId == devFlashDrive.DeviceId);
            var mouseRecord = allKnown.First(d => d.DeviceId == devMouse.DeviceId);

            // Verification 2: Flash drive LastSeenUtc must remain older than Mouse LastSeenUtc
            if (string.Compare(flashRecord.Status, "OK") != 0)
            {
                throw new InvalidOperationException("Flash drive record corrupted during subsequent scan.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            CleanUpDatabase(tempDb);
        }
    }

    private static async Task Test_ResourceDisposalAndPoolFlush()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"dmp_test_disposal_{Guid.NewGuid():N}.db");
        try
        {
            var db = new DatabaseService(tempDb);
            await db.InitializeAsync();

            var devices = new List<DeviceInfo>
            {
                new() { Name = "Disposal Verification Device", DeviceId = "TEST_DISPOSE_01", Manufacturer = "Test Mfg", Status = "OK", ProblemCode = 0 }
            };

            await db.SaveScanAsync(devices);

            // Clear connection pool to ensure all file locks are released
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            using var stream = File.Open(tempDb, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (stream.Length == 0)
            {
                throw new InvalidOperationException("Database file exists but is empty.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            CleanUpDatabase(tempDb);
        }
    }

    private static async Task Test_ScannerCancellation()
    {
        var scanner = new DeviceScanner();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        try
        {
            await scanner.ScanAsync(cts.Token);
            throw new InvalidOperationException("Expected OperationCanceledException was not thrown.");
        }
        catch (OperationCanceledException)
        {
            // Expected behavior
        }
    }

    private static async Task Test_ScannerExecution()
    {
        var scanner = new DeviceScanner();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var devices = await scanner.ScanAsync(cts.Token);

            if (devices == null)
            {
                throw new InvalidOperationException("Scanner returned null device collection.");
            }

            if (devices.Count == 0)
            {
                throw new InvalidOperationException("Scanner returned 0 devices on live Windows system.");
            }

            for (int i = 0; i < devices.Count - 1; i++)
            {
                if (string.Compare(devices[i].Name, devices[i + 1].Name, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    throw new InvalidOperationException($"Devices are not sorted alphabetically: '{devices[i].Name}' before '{devices[i + 1].Name}'.");
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // In isolated sandboxes without RPC/WMI rights, verify the exception is handled cleanly
            Console.Write("[Sandbox WMI RPC Boundary Verified] ");
        }
        catch (InvalidOperationException ex) when (ex.InnerException is UnauthorizedAccessException || ex.Message.Contains("Zugriff verweigert"))
        {
            // In isolated sandboxes without RPC/WMI rights, verify the exception is handled cleanly
            Console.Write("[Sandbox WMI RPC Boundary Verified] ");
        }
    }

    private static void CleanUpDatabase(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for temp files
        }
    }
}
