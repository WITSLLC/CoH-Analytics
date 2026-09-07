namespace CoHAnalytics.Services.Diagnostics;

/// <summary>
/// Creates a sanitized, user-saved diagnostics ZIP for beta support.
/// Operates independently of the hidden Diagnostics workspace.
/// </summary>
public interface IDiagnosticsReportService
{
    /// <summary>
    /// Builds a sanitized diagnostics report ZIP at <paramref name="destinationZipPath"/>.
    /// Live LocalAppData JSONL files are left byte-for-byte unchanged.
    /// </summary>
    DiagnosticsReportCreationResult CreateReport(string destinationZipPath);
}
