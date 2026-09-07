using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Orchestration.Contributors;

public sealed class GameplaySessionContributorOptions
{
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public TimeSpan ReceivingDataWindow { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Application-facing aggregate adapter for per-context gameplay sessions and runtime character
/// identity. Never publishes character names, raw lines, paths, or committed event payloads.
/// </summary>
public sealed class GameplaySessionContributor
    : IApplicationContributor, IApplicationContributorLifecycle, IDisposable
{
    private readonly IGameplaySessionManager _manager;
    private readonly GameplaySessionContributorOptions _options;
    private bool _disposed;

    public GameplaySessionContributor(
        IGameplaySessionManager manager,
        GameplaySessionContributorOptions? options = null)
    {
        _manager = manager;
        _options = options ?? new GameplaySessionContributorOptions();
        if (_options.ReceivingDataWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        Descriptor = CreateDescriptor();
        _manager.StateChanged += OnStateChanged;
    }

    public ApplicationContributorDescriptor Descriptor { get; }

    public event EventHandler? ContributionChanged;

    public Task<ApplicationContribution> GetContributionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildContribution());
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _manager.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _manager.StopAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, GameplaySessionManagerChangedEventArgs e) =>
        ContributionChanged?.Invoke(this, EventArgs.Empty);

    private ApplicationContribution BuildContribution()
    {
        var now = _options.TimeProvider.GetUtcNow();
        var snapshot = _manager.Current;
        var diagnostics = _manager.GetDiagnostics();
        var sessions = snapshot.Sessions;

        var wholeManagerFailure = diagnostics.WorkQueueOverflowed;

        var hasContextFailure = diagnostics.FailedContextCount > 0;
        var hasConflict = sessions.Any(session =>
            session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Conflicted);
        var hasIdentityRequired = sessions.Any(session =>
            session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.IdentityRequired);

        var health = wholeManagerFailure
            ? ContributorHealth.Error
            : hasContextFailure || hasConflict || hasIdentityRequired
                ? ContributorHealth.Degraded
                : ContributorHealth.Ready;

        var activity = BuildActivity(sessions, diagnostics, health, now);

        return ContributorContributionFactory.Create(
            Descriptor,
            health,
            activity,
            BuildFacts(sessions, diagnostics, now),
            BuildIssues(sessions, diagnostics, now),
            _options.TimeProvider);
    }

    private ContributorActivity BuildActivity(
        IReadOnlyList<GameplaySessionSnapshot> sessions,
        GameplaySessionDiagnostics diagnostics,
        ContributorHealth health,
        DateTimeOffset now)
    {
        if (health == ContributorHealth.Error)
        {
            return ContributorActivity.Inactive;
        }

        if (sessions.Count == 0)
        {
            return ContributorActivity.Waiting;
        }

        if (diagnostics.LastCommittedEventAt is { } lastCommitted
            && now - lastCommitted <= _options.ReceivingDataWindow)
        {
            return ContributorActivity.ReceivingData;
        }

        if (sessions.Any(session => session.LifecycleState == GameplaySessionLifecycleState.Active))
        {
            return ContributorActivity.Active;
        }

        if (sessions.All(session => session.LifecycleState == GameplaySessionLifecycleState.Suspended))
        {
            return ContributorActivity.Waiting;
        }

        return ContributorActivity.Waiting;
    }

    private static IReadOnlyList<ApplicationFact> BuildFacts(
        IReadOnlyList<GameplaySessionSnapshot> sessions,
        GameplaySessionDiagnostics diagnostics,
        DateTimeOffset observedAt)
    {
        var facts = new List<ApplicationFact>
        {
            Count(
                ContributorFactKeys.Session.ActiveCount,
                sessions.Count(session => session.LifecycleState == GameplaySessionLifecycleState.Active),
                "Active gameplay sessions"),
            Count(
                ContributorFactKeys.Session.SuspendedCount,
                sessions.Count(session => session.LifecycleState == GameplaySessionLifecycleState.Suspended),
                "Suspended gameplay sessions"),
            Count(
                ContributorFactKeys.Session.NeedsAttentionCount,
                sessions.Count(session => session.NeedsAttention),
                "Sessions needing attention"),
            Count(
                ContributorFactKeys.Session.RetentionOverflowCount,
                sessions.Count(session => session.RetentionOverflowed),
                "Sessions with retention overflow"),
            Count(
                ContributorFactKeys.Session.IncompleteCount,
                sessions.Count(session => session.RetentionOverflowed),
                "Sessions with incomplete retained data"),
            Count(
                ContributorFactKeys.Session.CommittedEventCount,
                diagnostics.TotalCommittedEvents,
                "Committed gameplay-session events"),
            Count(
                ContributorFactKeys.Identity.ResolvedCount,
                sessions.Count(session =>
                    session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved),
                "Resolved identities"),
            Count(
                ContributorFactKeys.Identity.UnresolvedCount,
                sessions.Count(session =>
                    session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Unresolved),
                "Unresolved identities"),
            Count(
                ContributorFactKeys.Identity.CandidateCount,
                sessions.Count(session =>
                    session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Candidate),
                "Candidate identities"),
            Count(
                ContributorFactKeys.Identity.ConflictedCount,
                sessions.Count(session =>
                    session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Conflicted),
                "Conflicted identities"),
            Count(
                ContributorFactKeys.Identity.RequiredCount,
                sessions.Count(session =>
                    session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.IdentityRequired),
                "Identity-required sessions"),
            Count(
                ContributorFactKeys.Identity.ConfirmedCount,
                sessions.Count(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed),
                "Confirmed identities"),
            Count(
                ContributorFactKeys.Identity.InferredCount,
                sessions.Count(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Inferred),
                "Inferred identities"),
            Count(
                ContributorFactKeys.Identity.UnknownCount,
                sessions.Count(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Unknown),
                "Unknown identities")
        };

        if (diagnostics.LastCommittedEventAt is { } lastCommittedAt)
        {
            facts.Add(Timestamp(
                ContributorFactKeys.Session.LastCommittedEventAt,
                lastCommittedAt,
                "Last committed gameplay-session event"));
        }

        return facts;

        ApplicationFact Count(string key, long value, string label) =>
            ContributorContributionFactory.IntegerFact(
                key,
                value,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay(label));

        ApplicationFact Timestamp(string key, DateTimeOffset value, string label) =>
            ContributorContributionFactory.TimestampFact(
                key,
                value,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay(label));
    }

    private static IReadOnlyList<ApplicationIssue> BuildIssues(
        IReadOnlyList<GameplaySessionSnapshot> sessions,
        GameplaySessionDiagnostics diagnostics,
        DateTimeOffset now)
    {
        var issues = new List<ApplicationIssue>();

        if (diagnostics.WorkQueueOverflowed)
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.Session.ManagerFailed,
                ApplicationIssueSeverity.Error,
                "Gameplay session processing failed because the work queue overflowed.",
                ApplicationProviders.Session,
                now));
        }

        foreach (var session in sessions)
        {
            var contextId = session.ContextId.ToString();

            if (session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Candidate)
            {
                issues.Add(ContributorContributionFactory.Issue(
                    ContributorIssueCodes.Session.IdentitySelectionRequired,
                    ApplicationIssueSeverity.Information,
                    "Character identity could not be inferred automatically for one gameplay session.",
                    ApplicationProviders.Session,
                    now,
                    requiresUserAction: true,
                    relatedEntityId: contextId));
            }

            if (session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Conflicted)
            {
                issues.Add(ContributorContributionFactory.Issue(
                    ContributorIssueCodes.Session.IdentityConflict,
                    ApplicationIssueSeverity.Warning,
                    "Character identity evidence could not be reconciled to one account-scoped character.",
                    ApplicationProviders.Session,
                    now,
                    requiresUserAction: true,
                    relatedEntityId: contextId));
            }

            if (session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.IdentityRequired)
            {
                issues.Add(ContributorContributionFactory.Issue(
                    ContributorIssueCodes.Session.RetentionOverflow,
                    ApplicationIssueSeverity.Warning,
                    "Character identity could not be established. CoH Analytics has stopped saving gameplay activity for this session. Return to character select and log back in with the app running, or select the active character manually.",
                    ApplicationProviders.Session,
                    now,
                    requiresUserAction: true,
                    relatedEntityId: contextId));
            }
        }

        if (diagnostics.FailedContextCount > 0)
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.Session.ContextProcessingFailed,
                ApplicationIssueSeverity.Warning,
                $"{diagnostics.FailedContextCount} monitoring context(s) reported gameplay-session processing failures.",
                ApplicationProviders.Session,
                now));
        }

        return issues;
    }

    private static ApplicationContributorDescriptor CreateDescriptor() =>
        new(
            ApplicationProviders.Session,
            "Gameplay Sessions",
            [ApplicationCapabilities.SessionCurrent, ApplicationCapabilities.IdentityCurrent],
            [ApplicationCapabilities.ParserEvents],
            [ApplicationCapabilities.MonitoringContexts, ApplicationCapabilities.AccountDiscovery],
            ApplicationContributorImportance.Important,
            55)
        {
            Description = "Tracks per-context gameplay sessions and active character identity from classified Homecoming events.",
            IconKey = "live-session",
            SchemaVersion = ApplicationContributorDescriptor.ExpectedSchemaVersion
        };
}
