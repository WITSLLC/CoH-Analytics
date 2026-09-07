namespace CoHAnalytics.Services.Diagnostics;

/// <summary>
/// Non-blocking structured diagnostic event sink. Application behavior must never depend on
/// whether a diagnostic event is accepted or persisted.
/// </summary>
public interface IDiagnosticLog
{
    bool IsEnabled(DiagnosticChannel channel, DiagnosticCategory category);

    /// <summary>Queues one event without waiting for disk I/O and never throws.</summary>
    void Write(DiagnosticEvent diagnosticEvent);

    DiagnosticLogStatus GetStatus();
}
