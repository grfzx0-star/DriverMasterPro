using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DriverMasterPro.Models;

namespace DriverMasterPro.Services.Abstractions;

public interface IDatabaseService
{
    string DatabasePath { get; }
    Task InitializeAsync();
    Task SaveScanAsync(IReadOnlyCollection<DeviceInfo> devices, CancellationToken cancellationToken = default);
    Task<List<DeviceInfo>> GetCachedDevicesAsync();
    Task<List<DeviceInfo>> GetActiveDevicesAsync(string scanTimeUtc);
    Task<List<DeviceInfo>> GetHistoricalDevicesAsync(string currentScanTimeUtc);
}
