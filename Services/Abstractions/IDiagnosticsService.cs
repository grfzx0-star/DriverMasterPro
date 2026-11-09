using System.Collections.Generic;
using DriverMasterPro.Models;

namespace DriverMasterPro.Services.Abstractions;

public interface IDiagnosticsService
{
    DiagnosticReport Resolve(uint? problemCode, bool isArabic = false, string? deviceStatus = null);
    void Enrich(DeviceInfo device, bool isArabic = false);
    void EnrichAll(IEnumerable<DeviceInfo> devices, bool isArabic = false);
}
