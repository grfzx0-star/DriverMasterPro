namespace DriverMasterPro.Models;

public sealed record ScanSnapshot(
    long ScanId,
    string ScanTimeUtc,
    int DeviceCount,
    IReadOnlyList<DeviceInfo> Observations);
