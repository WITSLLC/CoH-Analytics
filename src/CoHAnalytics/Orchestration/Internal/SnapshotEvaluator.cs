using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Orchestration.Internal;

internal static class SnapshotEvaluator
{
    public sealed record EvaluationResult(
        OverallApplicationState State,
        string? StateSummary,
        string OverallStateRule,
        ApplicationIssue? PrimaryIssue,
        string? PrimaryIssueRule,
        IReadOnlyList<ApplicationIssue> OrderedIssues,
        IReadOnlyList<ApplicationAction> OrderedActions,
        IReadOnlyList<ProviderSummary> OrderedProviders,
        IReadOnlyList<ApplicationFact> Facts);

    public sealed record ProviderEvaluationInput(
        ApplicationContributorDescriptor Descriptor,
        ApplicationContributorLifecycleState LifecycleState,
        ApplicationContribution Contribution,
        bool IsStale,
        IReadOnlyList<string> UnmetRequiredCapabilities,
        ContributorHealth EffectiveHealth);

    public static EvaluationResult Evaluate(
        IReadOnlyList<ProviderEvaluationInput> providers,
        DateTimeOffset now)
    {
        var activeIssues = new List<(ApplicationIssue Issue, ApplicationContributorImportance Importance, int Priority)>();
        var actions = new List<(ApplicationAction Action, int ContributorPriority)>();
        var facts = new List<ApplicationFact>();
        var summaries = new List<ProviderSummary>();

        foreach (var provider in providers)
        {
            var contribution = provider.Contribution;
            var providerIssues = NormalizeIssues(contribution.Issues, contribution.ProviderId, now);
            foreach (var issue in providerIssues)
            {
                activeIssues.Add((issue, provider.Descriptor.Importance, provider.Descriptor.Priority));
            }

            foreach (var action in contribution.Actions)
            {
                actions.Add((action with { ProviderId = action.ProviderId ?? contribution.ProviderId }, provider.Descriptor.Priority));
            }

            facts.AddRange(contribution.Facts);

            var highestSeverity = providerIssues.Count == 0
                ? (ApplicationIssueSeverity?)null
                : providerIssues.Max(issue => issue.Severity);

            summaries.Add(new ProviderSummary(
                contribution.ProviderId,
                provider.Descriptor.DisplayName,
                provider.EffectiveHealth,
                contribution.Activity,
                provider.LifecycleState,
                contribution.ObservedAt)
            {
                IsStale = provider.IsStale,
                Importance = provider.Descriptor.Importance,
                IssueCount = providerIssues.Count,
                HighestSeverity = highestSeverity,
                UnmetRequiredCapabilities = provider.UnmetRequiredCapabilities.ToArray()
            });
        }

        var orderedIssues = activeIssues
            .OrderByDescending(item => item.Issue.Severity)
            .ThenByDescending(item => item.Issue.RequiresUserAction)
            .ThenByDescending(item => item.Importance)
            .ThenBy(item => item.Priority)
            .ThenBy(item => item.Issue.CreatedAt)
            .ThenBy(item => item.Issue.DedupeKey, StringComparer.Ordinal)
            .Select(item => item.Issue)
            .ToList();

        ApplicationIssue? primaryIssue = orderedIssues.Count == 0 ? null : orderedIssues[0];
        var primaryIssueRule = primaryIssue is null
            ? null
            : "severity desc, RequiresUserAction desc, importance desc, priority asc, CreatedAt asc, DedupeKey asc";

        var orderedActions = actions
            .OrderByDescending(item => item.Action.IsPrimary)
            .ThenByDescending(item => item.Action.IsEnabled)
            .ThenBy(item => item.Action.Priority)
            .ThenBy(item => item.ContributorPriority)
            .ThenBy(item => item.Action.ActionId, StringComparer.Ordinal)
            .Select(item => item.Action)
            .ToList();

        var orderedProviders = summaries
            .OrderByDescending(summary => summary.Importance)
            .ThenBy(summary => providers.First(p => p.Descriptor.ProviderId == summary.ProviderId).Descriptor.Priority)
            .ThenBy(summary => summary.ProviderId, StringComparer.Ordinal)
            .ToList();

        var (state, stateRule) = EvaluateOverallState(providers, orderedIssues);
        var stateSummary = primaryIssue?.Summary ?? state.ToString();

        return new EvaluationResult(
            state,
            stateSummary,
            stateRule,
            primaryIssue,
            primaryIssueRule,
            orderedIssues,
            orderedActions,
            orderedProviders,
            facts);
    }

    public static bool AreSemanticallyEqual(ApplicationStateSnapshot left, ApplicationStateSnapshot right)
    {
        return left.State == right.State
            && string.Equals(left.StateSummary, right.StateSummary, StringComparison.Ordinal)
            && EqualProviders(left.Providers, right.Providers)
            && EqualIssues(left.ActiveIssues, right.ActiveIssues)
            && EqualIssue(left.PrimaryIssue, right.PrimaryIssue)
            && EqualActions(left.SuggestedActions, right.SuggestedActions)
            && EqualFacts(left.Facts, right.Facts);
    }

    private static (OverallApplicationState State, string Rule) EvaluateOverallState(
        IReadOnlyList<ProviderEvaluationInput> providers,
        IReadOnlyList<ApplicationIssue> orderedIssues)
    {
        // Only the "no providers registered" case can short-circuit here: Enumerable.All()
        // is vacuously true over an empty sequence, so rule 11 below would otherwise wrongly
        // match zero providers as "all Ready". "All Unknown" must NOT short-circuit past
        // rules 1-11, otherwise a real issue (e.g. a Warning) carried by an Unknown-health
        // provider would be masked as Unknown instead of its correct rule 1-11 outcome; it
        // falls through naturally to the rule-12 fallback at the bottom when nothing else matches.
        if (providers.Count == 0)
        {
            return (OverallApplicationState.Unknown, "12: no providers registered");
        }

        if (orderedIssues.Any(issue => issue.Severity == ApplicationIssueSeverity.Critical))
        {
            return (OverallApplicationState.Error, "1: Critical issue");
        }

        if (orderedIssues.Any(issue =>
                issue.Severity == ApplicationIssueSeverity.Error
                && GetImportance(providers, issue.ProviderId) == ApplicationContributorImportance.Critical))
        {
            return (OverallApplicationState.Error, "2: Error from Critical provider");
        }

        if (providers.Any(p =>
                p.Descriptor.Importance == ApplicationContributorImportance.Critical
                && p.UnmetRequiredCapabilities.Count > 0))
        {
            return (OverallApplicationState.Error, "3: unmet required capability for Critical provider");
        }

        if (orderedIssues.Any(issue =>
                issue.Severity == ApplicationIssueSeverity.Error
                && GetImportance(providers, issue.ProviderId) != ApplicationContributorImportance.Critical))
        {
            return (OverallApplicationState.Degraded, "4: Error from non-critical provider");
        }

        if (providers.Any(p =>
                p.Descriptor.Importance != ApplicationContributorImportance.Critical
                && p.UnmetRequiredCapabilities.Count > 0))
        {
            return (OverallApplicationState.Degraded, "5: unmet required capability for non-critical provider");
        }

        if (orderedIssues.Any(issue =>
                issue.Severity == ApplicationIssueSeverity.Warning && issue.RequiresUserAction))
        {
            return (OverallApplicationState.NeedsAttention, "6: Warning requiring user action");
        }

        if (providers.Any(p =>
                p.IsStale && p.Descriptor.Importance == ApplicationContributorImportance.Critical))
        {
            return (OverallApplicationState.Degraded, "7: stale Critical contributor");
        }

        if (orderedIssues.Any(issue => issue.Severity == ApplicationIssueSeverity.Warning)
            || providers.Any(p => p.EffectiveHealth == ContributorHealth.Degraded))
        {
            return (OverallApplicationState.Degraded, "8: Warning or Degraded provider");
        }

        if (providers.Any(p => p.Contribution.Activity == ContributorActivity.ReceivingData))
        {
            return (OverallApplicationState.Monitoring, "9: ReceivingData");
        }

        if (providers.Any(p =>
                p.Contribution.Activity is ContributorActivity.Waiting or ContributorActivity.Active))
        {
            return (OverallApplicationState.Waiting, "10: Waiting or Active");
        }

        if (providers.All(p => p.EffectiveHealth == ContributorHealth.Ready))
        {
            return (OverallApplicationState.Ready, "11: all Ready");
        }

        return (OverallApplicationState.Unknown, "12: fallback Unknown");
    }

    private static ApplicationContributorImportance GetImportance(
        IReadOnlyList<ProviderEvaluationInput> providers,
        string providerId)
    {
        var match = providers.FirstOrDefault(p =>
            string.Equals(p.Descriptor.ProviderId, providerId, StringComparison.Ordinal));
        return match?.Descriptor.Importance ?? ApplicationContributorImportance.Optional;
    }

    internal static IReadOnlyList<ApplicationIssue> NormalizeIssues(
        IReadOnlyCollection<ApplicationIssue> issues,
        string providerId,
        DateTimeOffset now)
    {
        var byKey = new Dictionary<string, ApplicationIssue>(StringComparer.Ordinal);
        foreach (var issue in issues)
        {
            if (issue.ExpiresAt is { } expiresAt && expiresAt <= now)
            {
                continue;
            }

            var normalized = issue with
            {
                ProviderId = string.IsNullOrWhiteSpace(issue.ProviderId) ? providerId : issue.ProviderId,
                UpdatedAt = issue.UpdatedAt == default ? issue.CreatedAt : issue.UpdatedAt,
                ActionIds = issue.ActionIds.ToArray()
            };

            if (byKey.TryGetValue(normalized.DedupeKey, out var existing))
            {
                if (normalized.Severity > existing.Severity)
                {
                    byKey[normalized.DedupeKey] = normalized with { CreatedAt = existing.CreatedAt };
                }
            }
            else
            {
                byKey[normalized.DedupeKey] = normalized;
            }
        }

        return byKey.Values.ToList();
    }

    private static bool EqualProviders(IReadOnlyCollection<ProviderSummary> left, IReadOnlyCollection<ProviderSummary> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.Zip(right).All(pair =>
            pair.First.ProviderId == pair.Second.ProviderId
            && pair.First.DisplayName == pair.Second.DisplayName
            && pair.First.Health == pair.Second.Health
            && pair.First.Activity == pair.Second.Activity
            && pair.First.LifecycleState == pair.Second.LifecycleState
            && pair.First.ObservedAt == pair.Second.ObservedAt
            && pair.First.IsStale == pair.Second.IsStale
            && pair.First.Importance == pair.Second.Importance
            && pair.First.IssueCount == pair.Second.IssueCount
            && pair.First.HighestSeverity == pair.Second.HighestSeverity
            && pair.First.UnmetRequiredCapabilities.SequenceEqual(pair.Second.UnmetRequiredCapabilities, StringComparer.Ordinal));
    }

    private static bool EqualIssues(IReadOnlyCollection<ApplicationIssue> left, IReadOnlyCollection<ApplicationIssue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.Zip(right).All(pair => EqualIssue(pair.First, pair.Second));
    }

    private static bool EqualIssue(ApplicationIssue? left, ApplicationIssue? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Code == right.Code
            && left.Severity == right.Severity
            && left.Summary == right.Summary
            && left.ProviderId == right.ProviderId
            && left.CreatedAt == right.CreatedAt
            && left.Detail == right.Detail
            && left.RequiresUserAction == right.RequiresUserAction
            && left.RelatedEntityId == right.RelatedEntityId
            && left.UpdatedAt == right.UpdatedAt
            && left.ExpiresAt == right.ExpiresAt
            && left.ActionIds.SequenceEqual(right.ActionIds, StringComparer.Ordinal);
    }

    private static bool EqualActions(IReadOnlyCollection<ApplicationAction> left, IReadOnlyCollection<ApplicationAction> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.Zip(right).All(pair =>
            pair.First.ActionId == pair.Second.ActionId
            && pair.First.Kind == pair.Second.Kind
            && pair.First.DisplayLabel == pair.Second.DisplayLabel
            && pair.First.Target == pair.Second.Target
            && pair.First.ProviderId == pair.Second.ProviderId
            && pair.First.RelatedEntityId == pair.Second.RelatedEntityId
            && pair.First.IsEnabled == pair.Second.IsEnabled
            && pair.First.DisabledReason == pair.Second.DisabledReason
            && pair.First.Priority == pair.Second.Priority
            && pair.First.IsPrimary == pair.Second.IsPrimary);
    }

    private static bool EqualFacts(IReadOnlyCollection<ApplicationFact> left, IReadOnlyCollection<ApplicationFact> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.Zip(right).All(pair =>
            pair.First.Key == pair.Second.Key
            && pair.First.Value.Equals(pair.Second.Value)
            && pair.First.Scope == pair.Second.Scope
            && pair.First.ObservedAt == pair.Second.ObservedAt
            && pair.First.Confidence == pair.Second.Confidence
            && pair.First.RelatedEntityId == pair.Second.RelatedEntityId);
    }
}
