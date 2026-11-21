using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using DriverMasterPro.Models;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro.Services;

public sealed class WmiDeviceInventoryProvider : IDeviceInventoryProvider
{
    private const string WmiScopePath = @"\\.\root\cimv2";
    private const string PnpEntityQuery =
        "SELECT Name, PNPDeviceID, Manufacturer, Status, ConfigManagerErrorCode FROM Win32_PnPEntity";

    public static readonly TimeSpan BatchPacketTimeout = TimeSpan.FromSeconds(30);

    private readonly IWmiQueryExecutor _queryExecutor;

    public WmiDeviceInventoryProvider() : this(new DefaultWmiQueryExecutor())
    {
    }

    internal WmiDeviceInventoryProvider(IWmiQueryExecutor queryExecutor)
    {
        _queryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));
    }

    public List<DeviceInfo> GetDevices(CancellationToken cancellationToken)
    {
        var devices = new List<DeviceInfo>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var row in _queryExecutor.ExecuteQuery(
                PnpEntityQuery,
                BatchPacketTimeout,
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                row.TryGetValue("PNPDeviceID", out object? rawPnpId);
                row.TryGetValue("Name", out object? rawName);
                row.TryGetValue("Manufacturer", out object? rawManufacturer);
                row.TryGetValue("Status", out object? rawStatus);
                row.TryGetValue("ConfigManagerErrorCode", out object? rawProblemCode);

                string pnpId = Convert.ToString(rawPnpId)?.Trim() ?? "";

                devices.Add(new DeviceInfo
                {
                    Name = Convert.ToString(rawName)?.Trim() ?? "Unknown device",
                    DeviceId = pnpId,
                    Manufacturer = Convert.ToString(rawManufacturer)?.Trim() ?? "",
                    Status = Convert.ToString(rawStatus)?.Trim() ?? "",
                    ProblemCode = rawProblemCode is null or DBNull
                        ? null
                        : Convert.ToUInt32(rawProblemCode)
                });
            }

            cancellationToken.ThrowIfCancellationRequested();
            return devices;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceError(
                $"[WmiDeviceInventoryProvider] WMI access denied (HResult={exception.HResult}): {exception.Message}");
            throw;
        }
        catch (COMException exception)
        {
            Trace.TraceError(
                $"[WmiDeviceInventoryProvider] WMI COM error (ErrorCode={exception.ErrorCode}, HResult={exception.HResult}): {exception.Message}");
            throw new InvalidOperationException(
                $"WMI-Abfrage fehlgeschlagen: {exception.Message}", exception);
        }
        catch (ManagementException exception)
        {
            Trace.TraceError(
                $"[WmiDeviceInventoryProvider] WMI enumeration error ({exception.ErrorCode}): {exception.Message}");
            throw new InvalidOperationException(
                $"WMI-Abfrage fehlgeschlagen: {exception.Message}", exception);
        }
    }

    private sealed class DefaultWmiQueryExecutor : IWmiQueryExecutor
    {
        public IEnumerable<IReadOnlyDictionary<string, object?>> ExecuteQuery(
            string query,
            TimeSpan batchPacketTimeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            var scope = new ManagementScope(WmiScopePath);

            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery(query))
            {
                Options = new EnumerationOptions
                {
                    ReturnImmediately = true,
                    Timeout = batchPacketTimeout
                }
            };

            using var results = searcher.Get();

            cancellationToken.ThrowIfCancellationRequested();

            foreach (ManagementBaseObject item in results)
            {
                IReadOnlyDictionary<string, object?> row;

                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? name = Convert.ToString(item["Name"]);
                    string? pnpDeviceId = Convert.ToString(item["PNPDeviceID"]);
                    string? manufacturer = Convert.ToString(item["Manufacturer"]);
                    string? status = Convert.ToString(item["Status"]);

                    object? rawCode = item["ConfigManagerErrorCode"];
                    uint? problemCode = rawCode is null or DBNull
                        ? null
                        : Convert.ToUInt32(rawCode);

                    row = new Dictionary<string, object?>(
                        5,
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["Name"] = name,
                        ["PNPDeviceID"] = pnpDeviceId,
                        ["Manufacturer"] = manufacturer,
                        ["Status"] = status,
                        ["ConfigManagerErrorCode"] = problemCode
                    };
                }

                rows.Add(row);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return rows;
        }
    }
}