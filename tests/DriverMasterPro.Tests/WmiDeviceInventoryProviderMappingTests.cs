using System;
using System.Collections.Generic;
using System.Threading;
using DriverMasterPro.Models;
using DriverMasterPro.Services;
using Xunit;

namespace DriverMasterPro.Tests;

/// <summary>
/// Deterministic regression tests for the WMI row -> DeviceInfo mapping.
///
/// These replace any reliance on finding a real nameless device on the machine:
/// a fake IWmiQueryExecutor feeds exact rows, so the outcome is identical on
/// every machine and on CI.
///
/// Regression covered: the Name fallback used to be
///     Convert.ToString(rawName)?.Trim() ?? "Unknown device"
/// which only caught null. A whitespace or empty WMI Name stayed blank.
/// </summary>
public sealed class WmiDeviceInventoryProviderMappingTests
{
    [Fact]
    public void NullName_FallsBackToUnknownDevice()
    {
        var devices = MapRow("Name", null);

        Assert.Equal("Unknown device", Assert.Single(devices).Name);
    }

    [Fact]
    public void EmptyName_FallsBackToUnknownDevice()
    {
        var devices = MapRow("Name", "");

        Assert.Equal("Unknown device", Assert.Single(devices).Name);
    }

    [Fact]
    public void WhitespaceName_FallsBackToUnknownDevice()
    {
        var devices = MapRow("Name", "   \t  ");

        Assert.Equal("Unknown device", Assert.Single(devices).Name);
    }

    [Fact]
    public void RealName_IsTrimmedAndKept()
    {
        var devices = MapRow("Name", "  Intel(R) Xeon(R)  ");

        Assert.Equal("Intel(R) Xeon(R)", Assert.Single(devices).Name);
    }

    [Fact]
    public void MissingManufacturerAndStatus_BecomeEmptyStrings()
    {
        var devices = MapRow("Name", "Real Device");

        var device = Assert.Single(devices);
        Assert.Equal("", device.Manufacturer);
        Assert.Equal("", device.Status);
    }

    [Fact]
    public void ProblemCode_MapsFromConfigManagerErrorCode()
    {
        var devices = MapRow("Name", "Real Device", "ConfigManagerErrorCode", 28u);

        Assert.Equal(28u, Assert.Single(devices).ProblemCode);
    }

    [Fact]
    public void MissingProblemCode_MapsToNull()
    {
        var devices = MapRow("Name", "Real Device");

        Assert.Null(Assert.Single(devices).ProblemCode);
    }

    [Fact]
    public void AlreadyCanceledToken_ThrowsBeforeMapping()
    {
        var provider = new WmiDeviceInventoryProvider(new SingleRowExecutor("Real Device"));

        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => provider.GetDevices(source.Token));
    }

    private static List<DeviceInfo> MapRow(
        string key,
        object? value,
        string? extraKey = null,
        object? extraValue = null)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [key] = value
        };

        if (extraKey is not null)
        {
            row[extraKey] = extraValue;
        }

        var provider = new WmiDeviceInventoryProvider(new SingleRowExecutor(row));

        return provider.GetDevices(CancellationToken.None);
    }

    private sealed class SingleRowExecutor : IWmiQueryExecutor
    {
        private readonly IReadOnlyDictionary<string, object?> _row;

        public SingleRowExecutor(string deviceName)
            : this(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = deviceName
            })
        {
        }

        public SingleRowExecutor(IReadOnlyDictionary<string, object?> row) => _row = row;

        public IEnumerable<IReadOnlyDictionary<string, object?>> ExecuteQuery(
            string query,
            TimeSpan batchPacketTimeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new[] { _row };
        }
    }
}
