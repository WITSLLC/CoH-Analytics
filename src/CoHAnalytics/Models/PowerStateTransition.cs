namespace CoHAnalytics.Models;

/// <summary>
/// Explicit activation or recharge-candidate wording. Recharge values do not confirm a power,
/// duration, or availability; independent same-session player activation is required later.
/// </summary>
public enum PowerStateTransition
{
    /// <summary>Explicit activation; actor scope determines whether it is player evidence.</summary>
    Activated = 1,

    /// <summary>Text says a name is recharged; the name has not been confirmed as a power.</summary>
    RechargeCompletedObserved = 2,

    /// <summary>
    /// Text says a name is still recharging; the name has not been confirmed as a power.
    /// Repository fixtures do not prove this is
    /// always a click-while-unavailable, so it is retained as an observed still-recharging
    /// line rather than inferred blocked-use.
    /// </summary>
    StillRechargingObserved = 3
}
