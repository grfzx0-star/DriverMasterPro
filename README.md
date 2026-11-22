# DRIVER MASTER PRO — v0.2 Foundation

Independent Windows desktop starter project.

## Current features
- Windows 10/11 x64
- WPF / C# / .NET 10
- German default UI, Arabic/RTL, English, French, and Spanish interface labels
- Read-only Plug-and-Play inventory through Windows Management Instrumentation (WMI)
- Local SQLite device inventory and scan history
- Scan works offline; no cloud account is required

## Safety boundary
The application only reads device information and writes its local scan database. It does **not** install, update, disable, or remove drivers.

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

Scanner unit tests use a fake inventory provider and do not require hardware. Access-denied and
other query failures fail tests, and a successful empty inventory is a valid result at the unit
level.

Live-WMI coverage lives in a separate project, `tests/DriverMasterPro.IntegrationTests`, which is
not part of the solution and is not built or run by CI. It is not selected by a test trait; it is
excluded because it is a different project. These tests require the Windows WMI service and expect
a non-empty inventory, so they must be run manually on a machine with real hardware:

```
dotnet test .\tests\DriverMasterPro.IntegrationTests\DriverMasterPro.IntegrationTests.csproj -c Release
```

Note the deliberate difference in expectation: the unit tests accept an empty inventory, while the
integration tests assert a non-empty one.

## Device diagnostics

After a scan, the application maps the Windows `ConfigManagerErrorCode` to a local, read-only diagnostic. Select a device to see its WMI status, assessment, severity, reported issue, supporting code evidence, and suggested next step. The catalog does not identify a root cause or perform repairs.

- Interface labels are available in German, Arabic, English, French, and Spanish; diagnostic catalog text is currently German or Arabic.
- A reported code of `0` is shown as healthy only when the accompanying WMI status is absent or `OK`. This means Windows Configuration Manager reported no problem; it is not a comprehensive hardware or driver health assessment.
- Missing problem codes, unmapped codes, and conflicting status evidence are shown as **Unknown**, not healthy.
- Warning and Critical are application triage labels, not severity values supplied by Windows.

Local database location:

`%LOCALAPPDATA%\DriverMasterPro\driver-master.db`

Only a non-empty `PNPDeviceID` is used as the supplied inventory key, and the application does not independently verify that it proves the same physical device across scans. If it is unavailable, the database stores a scan-specific observation; names, manufacturers, and the WMI `DeviceID` field are not substituted as persistent physical identity. Such observations cannot be correlated as the same physical device across scans, and historical observations remain in the database.

## Notes
This is a source-code foundation, not a signed installer. WMI status and ConfigManagerErrorCode are limited inventory signals, not complete driver-health diagnoses. Build and test on a non-critical machine first.
