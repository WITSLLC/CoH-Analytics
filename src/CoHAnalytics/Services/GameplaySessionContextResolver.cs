using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Authoritative gameplay-session context selection for session-scoped presentation surfaces.
/// </summary>
public sealed class GameplaySessionContextResolver : IGameplaySessionContextResolver
{
    private readonly IGameplaySessionIdentityReadService _identityReadService;
    private readonly IViewedContextService _viewedContextService;

    public GameplaySessionContextResolver(
        IGameplaySessionIdentityReadService identityReadService,
        IViewedContextService viewedContextService)
    {
        _identityReadService = identityReadService;
        _viewedContextService = viewedContextService;
    }

    public GameplaySessionContextSelection Resolve() =>
        Resolve(_identityReadService.Current, _viewedContextService.Current);

    public GameplaySessionContextSelection ResolveForLiveMonitoring() =>
        ResolveForLiveMonitoring(_identityReadService.Current, _viewedContextService.Current);

    /// <summary>
    /// Pure resolution entry point used by tests and future session-scoped workspaces.
    /// </summary>
    public static GameplaySessionContextSelection Resolve(
        GameplaySessionIdentityReadModelSnapshot snapshot,
        ViewedContextState viewedContext)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(viewedContext);

        var applicableContexts = GetApplicableContexts(snapshot);

        if (!viewedContext.IsFollowingLive && viewedContext.HasCharacter)
        {
            var pinned = FindContextForViewedCharacter(applicableContexts, viewedContext);
            return pinned is null
                ? GameplaySessionContextSelection.Unresolved
                : GameplaySessionContextSelection.FromContext(
                    pinned,
                    GameplaySessionContextSelectionReason.PinnedViewedContext);
        }

        return ResolveFromApplicableContexts(applicableContexts, viewedContext);
    }

    /// <summary>
    /// Live Session identity resolution. Never uses Accounts viewed-character pinning.
    /// </summary>
    public static GameplaySessionContextSelection ResolveForLiveMonitoring(
        GameplaySessionIdentityReadModelSnapshot snapshot,
        ViewedContextState viewedContext)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(viewedContext);

        return ResolveFromApplicableContexts(GetLiveMonitoringContexts(snapshot), viewedContext);
    }

    private static GameplaySessionContextSelection ResolveFromApplicableContexts(
        IReadOnlyList<LiveMonitoringContextIdentityReadModel> applicableContexts,
        ViewedContextState viewedContext)
    {
        if (viewedContext.LiveFollowContextId is not null)
        {
            var followTarget = applicableContexts.FirstOrDefault(context =>
                context.ContextId == viewedContext.LiveFollowContextId);
            if (followTarget is not null)
            {
                return GameplaySessionContextSelection.FromContext(
                    followTarget,
                    GameplaySessionContextSelectionReason.LiveFollowContext);
            }
        }

        if (applicableContexts.Count == 1)
        {
            return GameplaySessionContextSelection.FromContext(
                applicableContexts[0],
                GameplaySessionContextSelectionReason.SoleActiveSession);
        }

        var soleLiveTarget = TryResolveSoleLiveMonitoringTarget(applicableContexts);
        if (soleLiveTarget is not null)
        {
            return GameplaySessionContextSelection.FromContext(
                soleLiveTarget,
                GameplaySessionContextSelectionReason.SoleActiveSession);
        }

        return GameplaySessionContextSelection.Unresolved;
    }

    internal static IReadOnlyList<LiveMonitoringContextIdentityReadModel> GetApplicableContexts(
        GameplaySessionIdentityReadModelSnapshot snapshot) =>
        snapshot.Contexts
            .Where(IsSessionTelemetryContext)
            .ToArray();

    internal static IReadOnlyList<LiveMonitoringContextIdentityReadModel> GetLiveMonitoringContexts(
        GameplaySessionIdentityReadModelSnapshot snapshot)
    {
        var contexts = new Dictionary<MonitoringContextId, LiveMonitoringContextIdentityReadModel>();
        foreach (var context in snapshot.Contexts)
        {
            if (IsSessionTelemetryContext(context))
            {
                contexts[context.ContextId] = context;
            }
        }

        foreach (var context in snapshot.Contexts)
        {
            if (context.ContextState == MonitoringContextState.Ready
                && !string.IsNullOrWhiteSpace(context.AccountStableId))
            {
                contexts.TryAdd(context.ContextId, context);
            }
        }

        return contexts.Values.ToList();
    }

    private static bool IsSessionTelemetryContext(LiveMonitoringContextIdentityReadModel context) =>
        context.SessionStartedAt is not null
        && (context.HasActiveSession
            || context.SessionLifecycleState is GameplaySessionLifecycleState.Suspended
                or GameplaySessionLifecycleState.Finalized);

    private static LiveMonitoringContextIdentityReadModel? TryResolveSoleLiveMonitoringTarget(
        IReadOnlyList<LiveMonitoringContextIdentityReadModel> applicableContexts)
    {
        var readyLiveTargets = applicableContexts
            .Where(context =>
                context.ContextState == MonitoringContextState.Ready
                && (context.HasActiveSession
                    || !string.IsNullOrWhiteSpace(context.AccountStableId)))
            .ToArray();
        if (readyLiveTargets.Length == 1)
        {
            return readyLiveTargets[0];
        }

        var activeSessionTargets = applicableContexts
            .Where(context => context.HasActiveSession)
            .ToArray();
        if (activeSessionTargets.Length == 1)
        {
            return activeSessionTargets[0];
        }

        return null;
    }

    private static LiveMonitoringContextIdentityReadModel? FindContextForViewedCharacter(
        IReadOnlyList<LiveMonitoringContextIdentityReadModel> applicableContexts,
        ViewedContextState viewedContext) =>
        applicableContexts.FirstOrDefault(context =>
            context.CharacterRecordId == viewedContext.CharacterRecordId
            && string.Equals(
                context.AccountStableId,
                viewedContext.AccountStableId,
                StringComparison.OrdinalIgnoreCase));
}
