using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using DriverMasterPro.Models;
using DriverMasterPro.Services;
using Xunit;

namespace DriverMasterPro.IntegrationTests.Wmi;

/// <summary>
/// Live WMI integration tests. These exercise the real DefaultWmiQueryExecutor
/// against \\.\root\cimv2 on a Windows machine -- no mocks, no stubs.
///
/// Deliberate design rules:
///   * No assertion on device COUNTS: every machine and CI runner differs.
///   * No assertion on specific hardware: only structural invariants.
///   * Every test must prove the query really ran and rows were mapped.
/// </summary>
[Collection("WmiLive")]
public sealed class LiveWmiInventoryTests
{
    private const int MaxReasonableDurationSeconds = 60;

    private static List<DeviceInfo> ScanLive(CancellationToken cancellationToken = default)
        => new WmiDeviceInventoryProvider().GetDevices(cancellationToken);

    [Fact]
    public void LiveQuery_ReturnsInventory()
    {
        var devices = ScanLive();

        Assert.NotEmpty(devices);
    }

    [Fact]
    public void LiveQuery_YieldsAtLeastOneIdentifiableDevice()
    {
        var devices = ScanLive();

        Assert.Contains(devices, device => !string.IsNullOrWhiteSpace(device.DeviceId));
    }

    [Fact]
    public void LiveQuery_MapsEveryRowToNonNullValues()
    {
        var devices = ScanLive();

        Assert.All(devices, device =>
        {
            // Name is asserted for non-nullness only. Blank names are a KNOWN
            // DEFECT, characterised separately below -- not silently accepted.
            Assert.NotNull(device.Name);
            Assert.NotNull(device.DeviceId);
            Assert.NotNull(device.Manufacturer);
            Assert.NotNull(device.Status);
        });
    }

    /// <summary>
    /// Portable post-fix invariant: after the Name fallback was corrected to
    /// reject whitespace, EVERY device must carry a non-blank name on any machine.
    ///
    /// (Before the fix this failed on the entity HTREE\ROOT\0, whose WMI Name is
    /// an empty string. The deterministic stub-level regression tests for that
    /// defect live in DriverMasterPro.Tests/WmiDeviceInventoryProviderMappingTests.cs,
    /// so this live test no longer depends on such a device existing.)
    /// </summary>
    [Fact]
    public void LiveQuery_NeverProducesBlankDeviceNames()
    {
        var devices = ScanLive();

        Assert.All(devices, device =>
            Assert.False(
                string.IsNullOrWhiteSpace(device.Name),
                $"Blank name for DeviceId '{device.DeviceId}'."));
    }

    [Fact]
    public void LiveQuery_ProducesNoDuplicateDeviceIds()
    {
        var devices = ScanLive();

        var nonEmptyIds = new List<string>();
        foreach (var device in devices)
        {
            if (!string.IsNullOrWhiteSpace(device.DeviceId))
            {
                nonEmptyIds.Add(device.DeviceId);
            }
        }

        var distinct = new HashSet<string>(nonEmptyIds, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(nonEmptyIds.Count, distinct.Count);
    }

    [Fact]
    public void LiveQuery_CompletesWithinReasonableTime()
    {
        var stopwatch = Stopwatch.StartNew();

        var devices = ScanLive();

        stopwatch.Stop();

        Assert.NotEmpty(devices);
        Assert.True(
            stopwatch.Elapsed.TotalSeconds < MaxReasonableDurationSeconds,
            $"WMI inventory took {stopwatch.Elapsed.TotalSeconds:F1}s, expected under {MaxReasonableDurationSeconds}s.");
    }

    [Fact]
    public void AlreadyCanceledToken_IsHonoredBeforeQuerying()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => ScanLive(source.Token));
    }

    /// <summary>
    /// This proves BOUNDED COMPLETION, not instantaneous COM interruption.
    ///
    /// A CancellationToken cannot abort a blocked COM call inside
    /// ManagementObjectSearcher; it is honoured at the checkpoints the provider
    /// performs (before the query, per row, and after enumeration), and the
    /// enumeration itself is bounded by EnumerationOptions.Timeout (30s).
    /// So the contract asserted here is: the scan either returns or throws
    /// OperationCanceledException, and in both cases it finishes well inside the
    /// test budget rather than hanging.
    /// </summary>
    [Fact]
    public void CancellationDuringQuery_CompletesWithoutHanging()
    {
        using var source = new CancellationTokenSource();
        source.CancelAfter(TimeSpan.FromMilliseconds(50));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            ScanLive(source.Token);
        }
        catch (OperationCanceledException)
        {
            // Acceptable: cancellation observed mid-enumeration.
        }
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed.TotalSeconds < MaxReasonableDurationSeconds,
            $"Cancellation did not unblock the scan within {MaxReasonableDurationSeconds}s.");
    }
}
