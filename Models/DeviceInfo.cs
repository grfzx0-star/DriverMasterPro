namespace DriverMasterPro.Models;

public sealed class DeviceInfo
{
    public string Name { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Status { get; set; } = "";
    public uint? ProblemCode { get; set; }
    public string Health => ProblemCode == 0 ? "OK" : $"Problem {ProblemCode}";

    public DiagnosticReport? Diagnostics { get; set; }

    public string DiagnosticSummary => Diagnostics?.Summary ?? Health;
    public string SafeRecommendation => Diagnostics?.SafeRecommendation ?? "";
    public ProblemSeverity Severity => Diagnostics?.Severity ?? (ProblemCode == 0 ? ProblemSeverity.Normal : ProblemSeverity.Warning);
}
