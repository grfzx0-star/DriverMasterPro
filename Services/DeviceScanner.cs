using System.Diagnostics;
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
    public static readonly TimeSpan WmiBatchPacketTimeout = WmiDeviceInventoryProvider.BatchPacketTimeout;

    private readonly IDeviceInventoryProvider _inventoryProvider;

    public DeviceScanner(IDeviceInventoryProvider inventoryProvider)
    {
        _inventoryProvider = inventoryProvider ?? throw new ArgumentNullException(nameof(inventoryProvider));
    }

    public DeviceScanner() : this(new WmiDeviceInventoryProvider())
    {
    }

    /// <summary>
    /// Executes a read-only inventory scan and sorts the results by device name.
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
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var devices = _inventoryProvider.GetDevices(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return devices.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (OperationCanceledException)
        {
            Trace.TraceWarning("[DeviceScanner] ScanAsync operation was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[DeviceScanner] ScanAsync unexpected error: {ex}");
            throw;
        }
    }, cancellationToken);
}
