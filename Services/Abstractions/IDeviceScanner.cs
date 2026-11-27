using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DriverMasterPro.Models;

namespace DriverMasterPro.Services.Abstractions;

public interface IDeviceScanner
{
    Task<List<DeviceInfo>> ScanAsync(CancellationToken cancellationToken = default);
}
