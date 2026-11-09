namespace DriverMasterPro.Models;

public enum ProblemSeverity
{
    Normal,
    Notice,
    Warning,
    Critical,
    Unknown
}

public enum DiagnosticOutcome
{
    Healthy,
    Warning,
    Critical,
    Unknown
}

public sealed record DiagnosticReport(
    uint? ProblemCode,
    string ErrorSymbol,
    DiagnosticOutcome Outcome,
    ProblemSeverity Severity,
    string OutcomeLabel,
    string SeverityLabel,
    string Summary,
    string Description,
    string Evidence,
    string SafeRecommendation);
