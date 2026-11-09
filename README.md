# DRIVER MASTER PRO — v0.1

Independent Windows desktop starter project.

## Current features
- Windows 10/11 x64
- WPF / C# / .NET 10
- German default UI and Arabic selectable UI with RTL flow direction
- Read-only Plug-and-Play inventory through Windows Management Instrumentation (WMI)
- Local SQLite device inventory and scan history
- Scan works offline; no cloud account is required

## Safety boundary
This first version only reads device information. It does **not** install, update, disable, or remove drivers. It does not reuse Driver Booster files, proprietary databases, endpoints, or code.

## Requirements
- Windows 10/11 x64
- .NET 10 SDK

## Build on Windows
Open PowerShell in the project folder and run:

```powershell
dotnet --info
dotnet restore
dotnet build -c Release
dotnet run
```

## Automated tests

Restore, build, and run the test project on Windows:

```powershell
dotnet restore .\tests\DriverMasterPro.Tests\DriverMasterPro.Tests.csproj
dotnet build .\DriverMasterPro.csproj -c Release
dotnet build .\tests\DriverMasterPro.Tests\DriverMasterPro.Tests.csproj -c Release
dotnet test .\tests\DriverMasterPro.Tests\DriverMasterPro.Tests.csproj -c Release
```

Scanner unit tests use a fake inventory provider and do not require hardware. The test tagged `Category=WmiIntegration` queries live WMI and requires Windows WMI service access; CI excludes only that integration test. Access-denied and other query failures fail tests. A successful empty inventory is valid.

Local database location:

`%LOCALAPPDATA%\DriverMasterPro\driver-master.db`

Devices without a persistent Plug-and-Play identifier are stored as unique scan observations. The application cannot identify the same physical device across scans without a stable hardware identifier.

## Notes
This is a source-code starter, not a signed installer. A WMI status / ConfigManagerErrorCode is an inventory signal, not a complete driver-health diagnosis. Build and test on a non-critical machine first.
