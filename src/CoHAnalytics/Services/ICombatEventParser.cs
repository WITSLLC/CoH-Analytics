using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Extracts normalized combat events from structurally classified parser events.
/// </summary>
public interface ICombatEventParser
{
    bool TryParse(ParserEvent parserEvent, out CombatEvent combatEvent);

    bool TryParseCanonical(ParserEvent parserEvent, out CanonicalCombatEvent canonicalEvent);

    bool TryAdaptToLegacy(CanonicalCombatEvent canonicalEvent, out CombatEvent combatEvent);
}
