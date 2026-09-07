using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Orchestration.Contributors;

internal static class ContributorContributionFactory
{
    public static ApplicationContribution Create(
        ApplicationContributorDescriptor descriptor,
        ContributorHealth health,
        ContributorActivity activity,
        IReadOnlyList<ApplicationFact> facts,
        IReadOnlyList<ApplicationIssue> issues,
        TimeProvider timeProvider,
        string? componentVersion = null,
        string? sourceDescription = null)
    {
        var now = timeProvider.GetUtcNow();
        return new ApplicationContribution(
            descriptor.ProviderId,
            health,
            activity,
            facts,
            issues,
            Array.Empty<ApplicationAction>(),
            now)
        {
            ComponentVersion = componentVersion,
            SourceDescription = sourceDescription
        }.WithImmutableCollections();
    }

    public static ApplicationFact BooleanFact(
        string key,
        bool value,
        ApplicationFactScope scope,
        DateTimeOffset observedAt,
        ApplicationFactConfidence confidence = ApplicationFactConfidence.Observed,
        ApplicationFactDisplay? display = null,
        string? relatedEntityId = null) =>
        new(key, new ApplicationFactValue.Boolean(value), scope, observedAt)
        {
            Confidence = confidence,
            Display = display,
            RelatedEntityId = relatedEntityId
        };

    public static ApplicationFact IntegerFact(
        string key,
        long value,
        ApplicationFactScope scope,
        DateTimeOffset observedAt,
        ApplicationFactDisplay? display = null) =>
        new(key, new ApplicationFactValue.Integer(value), scope, observedAt)
        {
            Confidence = ApplicationFactConfidence.Observed,
            Display = display
        };

    public static ApplicationFact TextFact(
        string key,
        string value,
        ApplicationFactScope scope,
        DateTimeOffset observedAt,
        ApplicationFactConfidence confidence = ApplicationFactConfidence.Observed,
        ApplicationFactDisplay? display = null) =>
        new(key, new ApplicationFactValue.Text(value), scope, observedAt)
        {
            Confidence = confidence,
            Display = display
        };

    public static ApplicationFact TimestampFact(
        string key,
        DateTimeOffset value,
        ApplicationFactScope scope,
        DateTimeOffset observedAt,
        ApplicationFactDisplay? display = null) =>
        new(key, new ApplicationFactValue.Timestamp(value), scope, observedAt)
        {
            Confidence = ApplicationFactConfidence.Observed,
            Display = display
        };

    public static ApplicationIssue Issue(
        string code,
        ApplicationIssueSeverity severity,
        string summary,
        string providerId,
        DateTimeOffset observedAt,
        bool requiresUserAction = false,
        string? detail = null,
        string? relatedEntityId = null) =>
        new(code, severity, summary, providerId, observedAt)
        {
            UpdatedAt = observedAt,
            RequiresUserAction = requiresUserAction,
            Detail = detail,
            RelatedEntityId = relatedEntityId
        };
}
