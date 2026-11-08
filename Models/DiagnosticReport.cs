namespace DriverMasterPro.Models;

public enum ProblemSeverity
{
    Normal,
    Notice,
    Warning,
    Critical
}

public sealed record DiagnosticReport(
    uint ProblemCode,
    string ErrorSymbol,
    string Summary,
    string Description,
    string SafeRecommendation,
    ProblemSeverity Severity
);
