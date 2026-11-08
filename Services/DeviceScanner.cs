using System.Diagnostics;
using System.Management;
using DriverMasterPro.Models;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro.Services;

/// <summary>
/// Read-only Windows Plug-and-Play inventory service.
/// Queries WMI Win32_PnPEntity. No driver changes, registry writes, or system modifications are made.
/// </summary>
public sealed class DeviceScanner : IDeviceScanner
{
    /// <summary>
    /// Timeout applied to individual WMI batch packet retrievals (IEnumWbemClassObject::Next).
    /// Note: This is NOT the total scan timeout. Total timeout is governed by the caller's CancellationToken.
    /// </summary>
    public static readonly TimeSpan WmiBatchPacketTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Executes a read-only hardware enumeration query against Win32_PnPEntity.
    ///
    /// CANCELLATION GUARANTEES &amp; LIMITATIONS:
    /// - Cooperative cancellation is evaluated before starting and between item iterations.
    /// - UNMANAGED COM LIMITATION: If an unmanaged COM RPC call blocks inside native code
    ///   (e.g., waiting for an unresponsive kernel driver or deadlocked WmiPrvSE provider),
    ///   managed CancellationToken cannot preempt the unmanaged thread. Cancellation takes
    ///   effect immediately once control returns from native code to managed code.
    /// </summary>
    public Task<List<DeviceInfo>> ScanAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var devices = new List<DeviceInfo>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, PNPDeviceID, Manufacturer, Status, ConfigManagerErrorCode FROM Win32_PnPEntity")
            {
                Options = new EnumerationOptions
                {
                    ReturnImmediately = true,
                    Timeout = WmiBatchPacketTimeout
                }
            };

            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                using (item) // Ensure unmanaged COM wrapper is disposed
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Prioritize PNPDeviceID; fallback to WMI Key property DeviceID if PNPDeviceID is absent
                    string pnpId = Convert.ToString(item["PNPDeviceID"])?.Trim() ?? "";
                    string devId = Convert.ToString(item["DeviceID"])?.Trim() ?? "";
                    string persistentId = !string.IsNullOrEmpty(pnpId) ? pnpId : devId;

                    devices.Add(new DeviceInfo
                    {
                        Name = Convert.ToString(item["Name"])?.Trim() ?? "Unknown device",
                        DeviceId = persistentId,
                        Manufacturer = Convert.ToString(item["Manufacturer"])?.Trim() ?? "",
                        Status = Convert.ToString(item["Status"])?.Trim() ?? "",
                        ProblemCode = item["ConfigManagerErrorCode"] is null
                            ? 0u : Convert.ToUInt32(item["ConfigManagerErrorCode"])
                    });
                }
            }

            return devices.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (OperationCanceledException)
        {
            Trace.TraceWarning("[DeviceScanner] ScanAsync operation was cancelled.");
            throw;
        }
        catch (ManagementException mex)
        {
            Trace.TraceError($"[DeviceScanner] WMI enumeration error ({mex.ErrorCode}): {mex.Message}");
            throw new InvalidOperationException($"WMI-Abfrage fehlgeschlagen: {mex.Message}", mex);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[DeviceScanner] ScanAsync unexpected error: {ex}");
            throw;
        }
    }, cancellationToken);
}
