# PROJECT STRUCTURE
**DriverMasterPro v0.2 Foundation**  
*Senior Reverse Engineering & Architectural Audit*

---

## 1. Directory Tree & Solution Layout

The repository is organized as a single Windows desktop application project targeting .NET 10. The tree structure below captures all tracked and distribution files:

```
D:\Office\Programs\DriverMasterPro_v0.1_Source\DriverMasterPro_v0.1\
│
├── .gitignore                                      # Standard .NET Git ignore patterns
├── README.md                                       # High-level product overview and build guide
├── DriverMasterPro.csproj                          # .NET 10 project definition (WPF, C# 13/14)
│
├── App.xaml                                        # WPF Application entry markup & accent resource
├── App.xaml.cs                                     # Application startup code-behind
│
├── MainWindow.xaml                                 # Primary desktop window (Dark UI, 2-column layout)
├── MainWindow.xaml.cs                              # Window code-behind, event handling & UI state
│
├── Models\
│   └── DeviceInfo.cs                               # Hardware device entity DTO
│
├── Services\
│   ├── DeviceScanner.cs                            # WMI Plug-and-Play query engine
│   └── DatabaseService.cs                          # SQLite persistence and history service
│
├── docs\                                           # Architecture & technical documentation
│   ├── PROJECT_STRUCTURE.md                        # Project layout and file breakdown (this document)
│   ├── ARCHITECTURE_ANALYSIS.md                    # WPF architecture and subsystem audit
│   ├── FEATURE_MAP.md                              # Implementation status and missing features
│   ├── DATABASE_ANALYSIS.md                        # SQLite schema, transactions and query audit
│   └── BUGS_AND_IMPROVEMENTS.md                    # Discovered bugs, security review, and roadmap
│
└── artifacts\                                      # Distribution packages
    ├── DriverMasterPro-v0.1.0-win-x64.zip         # 60,123,728 bytes ZIP release archive
    └── DriverMasterPro-v0.1.0-win-x64\
        ├── DriverMasterPro.exe                     # 142,398,652 bytes self-contained single-file binary
        └── README.txt                              # End-user execution instructions
```

---

## 2. Project File & Toolchain Analysis

### 2.1 Project Configuration (`DriverMasterPro.csproj`)

Exact contents of [DriverMasterPro.csproj](file:///D:/Office/Programs/DriverMasterPro_v0.1_Source/DriverMasterPro_v0.1/DriverMasterPro.csproj#L1-L16):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>DriverMasterPro</AssemblyName>
    <RootNamespace>DriverMasterPro</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.14" />
    <PackageReference Include="System.Management" Version="10.0.0" />
  </ItemGroup>
</Project>
```

### 2.2 Toolchain & Compiler Settings (Verified Facts)
- **SDK**: `Microsoft.NET.Sdk`
- **Target Framework**: `net10.0-windows` (targets .NET 10.0 on Windows platform).
- **Subsystem**: `WinExe` (creates a standard Windows graphical application without a console window).
- **Presentation Engine**: WPF (`<UseWPF>true</UseWPF>`).
- **Null Safety**: `<Nullable>enable</Nullable>` is enforced across all source files.
- **Implicit Namespaces**: `<ImplicitUsings>enable</ImplicitUsings>` auto-imports root namespaces (`System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Threading.Tasks`, etc.).
- **Nuget Dependencies**:
  1. `Microsoft.Data.Sqlite` (10.0.0): Official lightweight ADO.NET SQLite driver.
  2. `SQLitePCLRaw.bundle_e_sqlite3` (2.1.14): Embedded native C SQLite3 runtime engine.
  3. `System.Management` (10.0.0): Windows Management Instrumentation (WMI) managed wrapper.

---

## 3. Source File Inventory & Metrics

| File Path | Type | SLOC | Primary Namespace | Core Purpose & Responsibilities |
| :--- | :--- | :---: | :--- | :--- |
| [App.xaml.cs](../App.xaml.cs) | C# | — | `DriverMasterPro` | Starts the host, registers services, resolves the main window, and stops/disposes the host on exit. |
| [MainWindow.xaml](../MainWindow.xaml) | XAML | — | `DriverMasterPro` | WPF dashboard with inventory grid and selected-device diagnostics details. |
| [MainWindow.xaml.cs](../MainWindow.xaml.cs) | C# | — | `DriverMasterPro` | Orchestrates injected scanning, diagnostics, database writes, filtering, and UI localization. |
| [Models/DeviceInfo.cs](../Models/DeviceInfo.cs) | C# | — | `DriverMasterPro.Models` | Device data including nullable WMI problem code and enriched diagnostic report. |
| [Models/DiagnosticReport.cs](../Models/DiagnosticReport.cs) | C# | — | `DriverMasterPro.Models` | Diagnostic outcome/severity enums and evidence/recommendation report. |
| [Services/DeviceScanner.cs](../Services/DeviceScanner.cs) | C# | — | `DriverMasterPro.Services` | Sorts inventory results from the injected inventory provider. |
| [Services/WmiDeviceInventoryProvider.cs](../Services/WmiDeviceInventoryProvider.cs) | C# | — | `DriverMasterPro.Services` | Read-only WMI provider; uses `PNPDeviceID` only when available as device identity. |
| [Services/RuleBasedDiagnosticsService.cs](../Services/RuleBasedDiagnosticsService.cs) | C# | — | `DriverMasterPro.Services` | Maps known Configuration Manager codes; preserves missing, unmapped, and conflicting evidence as Unknown. |
| [Services/DatabaseService.cs](../Services/DatabaseService.cs) | C# | — | `DriverMasterPro.Services` | Persists scan observations and history transactionally and exposes device query methods. |
| [AutomatedTests.cs](../tests/DriverMasterPro.Tests/AutomatedTests.cs) | C# | — | `DriverMasterPro.Tests` | xUnit tests for scanner, diagnostics, persistence, dependency injection, and WPF diagnostics integration. |

---

## 4. Release Artifacts Analysis

The `artifacts/` folder contains pre-packaged release distributions:

1. **`artifacts/DriverMasterPro-v0.1.0-win-x64.zip`** (60,123,728 bytes):
   - Compressed archive containing the release payload and `README.txt`.
2. **`artifacts/DriverMasterPro-v0.1.0-win-x64/DriverMasterPro.exe`** (142,398,652 bytes):
   - **Publish Mode**: Self-Contained Single-File Executable (`PublishSingleFile=true`, `SelfContained=true`, `RuntimeIdentifier=win-x64`).
   - Bundles the .NET 10 CLR runtime, native WPF rendering DLLs (`wpfgfx_cor3.dll`, `PresentationNative_cor3.dll`), native SQLite engine (`e_sqlite3.dll`), and managed assemblies into a standalone executable requiring no pre-installed .NET runtime on host machines.
3. **`artifacts/DriverMasterPro-v0.1.0-win-x64/README.txt`**:
   - Outlines operating requirements (Windows 10/11 64-bit) and emphasizes the read-only safety boundary.

---

## 5. Git Repository & Version History

- **Branch**: `main`
- **Initial Commit Hash**: `288e300`
- **Commit Subject**: `Initial commit: Driver Master Pro read-only MVP`
- **Repository Health**: Clean working tree. No branches, tags, or intermediate revisions exist.
