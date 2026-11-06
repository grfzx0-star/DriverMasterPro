using System.Management;
using DriverMasterPro.Models;

namespace DriverMasterPro.Services;

/// <summary>Read-only Windows Plug-and-Play inventory. No driver changes are made.</summary>
public sealed class DeviceScanner
{
    public Task<List<DeviceInfo>> ScanAsync() => Task.Run(() =>
    {
        var devices = new List<DeviceInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID, Manufacturer, Status, ConfigManagerErrorCode FROM Win32_PnPEntity");
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            devices.Add(new DeviceInfo
            {
                Name = Convert.ToString(item["Name"]) ?? "Unknown device",
                DeviceId = Convert.ToString(item["PNPDeviceID"]) ?? "",
                Manufacturer = Convert.ToString(item["Manufacturer"]) ?? "",
                Status = Convert.ToString(item["Status"]) ?? "",
                ProblemCode = item["ConfigManagerErrorCode"] is null
                    ? 0u : Convert.ToUInt32(item["ConfigManagerErrorCode"])
            });
        }
        return devices.OrderBy(d => d.Name).ToList();
    });
}
