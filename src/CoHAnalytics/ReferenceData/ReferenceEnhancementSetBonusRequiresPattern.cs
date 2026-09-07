namespace CoHAnalytics.ReferenceData;

/// <summary>
/// Bounded semantic classification of Homecoming set-bonus Requires expressions.
/// The full ordered token sequence remains canonical on the bonus tier record.
/// </summary>
public enum ReferenceEnhancementSetBonusRequiresPattern
{
    None = 0,
    PieceGate = 1,
    PvPMap = 2,
    Other = 3
}
