using System.Collections.Generic;
using System.Threading;
using DriverMasterPro.Models;

namespace DriverMasterPro.Services.Abstractions;

public interface IDeviceInventoryProvider
{
    List<DeviceInfo> GetDevices(CancellationToken cancellationToken);
}
