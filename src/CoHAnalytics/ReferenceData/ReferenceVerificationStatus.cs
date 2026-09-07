namespace CoHAnalytics.ReferenceData;

/// <summary>Verification confidence for a reference catalog record.</summary>
public enum ReferenceVerificationStatus
{
    VerifiedDirect,
    VerifiedMultiSource,
    SecondaryOnly,
    Conflicting,
    NeedsDirectVerification
}
