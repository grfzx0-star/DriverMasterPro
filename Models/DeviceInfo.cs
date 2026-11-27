namespace DriverMasterPro.Models;

public sealed class DeviceInfo
{
    public string Name { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Status { get; set; } = "";
    public uint? ProblemCode { get; set; }
    public string Health => Diagnostics?.OutcomeLabel ?? "Unknown";

    public DiagnosticReport? Diagnostics { get; set; }

    public string DiagnosticSummary => Diagnostics?.Summary ?? "No diagnostic result available.";
    public string DiagnosticSeverity => Diagnostics?.SeverityLabel ?? "Unknown";
    public string DiagnosticEvidence => Diagnostics?.Evidence ?? "No diagnostic evidence is available.";
    public string DiagnosticDescription => Diagnostics?.Description ?? "";
    public string SafeRecommendation => Diagnostics?.SafeRecommendation ?? "";
    public ProblemSeverity Severity => Diagnostics?.Severity ?? ProblemSeverity.Unknown;
}
