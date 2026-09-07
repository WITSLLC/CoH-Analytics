using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Extracts gameplay XP and Influence telemetry from structurally classified parser events.
/// </summary>
public interface IGameplayTelemetryParser
{
    bool TryParse(ParserEvent parserEvent, out GameplayTelemetryObservation observation);
}
