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
                "SELECT Name, PNPDeviceID, Manufacturer, Status, ConfigManagerErrorCode FROM Win32_PnPEntity")
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
                    object? rawProblemCode = item["ConfigManagerErrorCode"];

                    devices.Add(new DeviceInfo
                    {
                        Name = Convert.ToString(item["Name"])?.Trim() ?? "Unknown device",
                        DeviceId = pnpId,
                        Manufacturer = Convert.ToString(item["Manufacturer"])?.Trim() ?? "",
                        Status = Convert.ToString(item["Status"])?.Trim() ?? "",
                        ProblemCode = rawProblemCode is null or DBNull
                            ? null
                            : Convert.ToUInt32(rawProblemCode)
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
