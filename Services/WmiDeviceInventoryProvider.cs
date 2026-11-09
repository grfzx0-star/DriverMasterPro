using System.Diagnostics;
using System.Management;
using DriverMasterPro.Models;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro.Services;

public sealed class WmiDeviceInventoryProvider : IDeviceInventoryProvider
{
    public static readonly TimeSpan BatchPacketTimeout = TimeSpan.FromSeconds(30);

    public List<DeviceInfo> GetDevices(CancellationToken cancellationToken)
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
                    Timeout = BatchPacketTimeout
                }
            };

            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();

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

            return devices;
        }
        catch (ManagementException exception)
        {
            Trace.TraceError($"[DeviceScanner] WMI enumeration error ({exception.ErrorCode}): {exception.Message}");
            throw new InvalidOperationException($"WMI-Abfrage fehlgeschlagen: {exception.Message}", exception);
        }
    }
}
