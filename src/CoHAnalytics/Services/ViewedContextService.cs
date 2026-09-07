using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Owns viewed navigation context and live-follow behavior at the application shell level.
/// </summary>
public sealed class ViewedContextService : IViewedContextService
{
    private readonly IGameplaySessionIdentityReadService _identityReadService;
    private readonly ICharacterRepository _characterRepository;
    private ViewedContextState _current = new() { IsFollowingLive = true };

    public ViewedContextService(
        IGameplaySessionIdentityReadService identityReadService,
        ICharacterRepository characterRepository)
    {
        _identityReadService = identityReadService;
        _characterRepository = characterRepository;
        _identityReadService.Changed += OnIdentityChanged;
        ApplyInitialState();
    }

    public ViewedContextState Current => _current;

    public event EventHandler<ViewedContextChangedEventArgs>? Changed;

    public void SelectViewedAccount(string accountStableId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountStableId);

        SetState(new ViewedContextState
        {
            AccountStableId = accountStableId,
            CharacterRecordId = null,
            IsFollowingLive = false,
            LiveFollowContextId = _current.LiveFollowContextId
        });
    }

    public void SelectViewedCharacter(string accountStableId, CharacterRecordId characterRecordId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountStableId);
        ArgumentNullException.ThrowIfNull(characterRecordId);

        SetState(new ViewedContextState
        {
            AccountStableId = accountStableId,
            CharacterRecordId = characterRecordId,
            IsFollowingLive = false,
            LiveFollowContextId = _current.LiveFollowContextId
        });
    }

    public void SelectGameplaySessionContext(MonitoringContextId contextId)
    {
        var context = GetApplicableGameplaySessionContexts()
            .FirstOrDefault(candidate => candidate.ContextId == contextId);
        if (context is null)
        {
            return;
        }

        SetState(BuildGameplaySessionContextState(context));
    }

    public void ReturnToLive()
    {
        var applicable = GetApplicableGameplaySessionContexts();
        if (applicable.Count == 0)
        {
            SetState(_current with { IsFollowingLive = true });
            return;
        }

        if (applicable.Count == 1)
        {
            SetState(BuildGameplaySessionContextState(applicable[0]));
            return;
        }

        var target = ResolveLiveFollowTarget(applicable, preferExistingFollowTarget: true);
        if (target is null)
        {
            target = applicable.OrderBy(context => context.ContextId.Value).First();
        }

        SetState(BuildGameplaySessionContextState(target));
    }

    public void ResetForNewRuntimeGeneration()
    {
        SetState(new ViewedContextState
        {
            IsFollowingLive = true,
            LiveFollowContextId = null
        });
    }

    private void OnIdentityChanged(object? sender, GameplaySessionIdentityReadModelChangedEventArgs e)
    {
        if (_current.IsFollowingLive)
        {
            TryApplyLiveFollow();
        }

        if (!_current.HasCharacter)
        {
            ApplyInitialDefaultCharacter();
        }

        PublishIfStateChanged();
    }

    private void ApplyInitialState()
    {
        if (!_current.HasCharacter)
        {
            ApplyInitialDefaultCharacter();
        }

        if (_current.IsFollowingLive)
        {
            TryApplyLiveFollow();
        }

        PublishIfChanged();
    }

    private void ApplyInitialDefaultCharacter()
    {
        var records = _characterRepository.Current.Records;
        if (records.Count == 0)
        {
            return;
        }

        var mostRecent = records
            .OrderByDescending(record => record.LastObservedAt != default)
            .ThenByDescending(record => record.LastObservedAt)
            .ThenBy(record => record.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
            .First();

        _current = new ViewedContextState
        {
            AccountStableId = mostRecent.AccountStableId,
            CharacterRecordId = mostRecent.RecordId,
            IsFollowingLive = true,
            LiveFollowContextId = _current.LiveFollowContextId
        };
    }

    private void TryApplyLiveFollow()
    {
        var applicable = GetApplicableGameplaySessionContexts();
        if (applicable.Count == 0)
        {
            if (_current.IsFollowingLive && _current.LiveFollowContextId is not null)
            {
                _current = _current with { LiveFollowContextId = null };
            }

            return;
        }

        if (applicable.Count == 1)
        {
            ApplyGameplaySessionContext(applicable[0]);
            return;
        }

        if (_current.LiveFollowContextId is not null)
        {
            var existing = applicable.FirstOrDefault(context =>
                context.ContextId == _current.LiveFollowContextId);
            if (existing is not null)
            {
                ApplyGameplaySessionContext(existing);
                return;
            }

            var cleared = _current with { LiveFollowContextId = null };
            if (!StatesEqual(_current, cleared))
            {
                _current = cleared;
            }
        }

        var followTarget = ResolveLiveFollowTarget(applicable, preferExistingFollowTarget: true);
        if (followTarget is not null)
        {
            ApplyGameplaySessionContext(followTarget);
        }
    }

    private LiveMonitoringContextIdentityReadModel? ResolveLiveFollowTarget(
        IReadOnlyList<LiveMonitoringContextIdentityReadModel> applicable,
        bool preferExistingFollowTarget)
    {
        if (applicable.Count == 0)
        {
            return null;
        }

        if (applicable.Count == 1)
        {
            return applicable[0];
        }

        if (preferExistingFollowTarget && _current.LiveFollowContextId is not null)
        {
            var existing = applicable.FirstOrDefault(context =>
                context.ContextId == _current.LiveFollowContextId);
            if (existing is not null)
            {
                return existing;
            }
        }

        return null;
    }

    private void ApplyGameplaySessionContext(LiveMonitoringContextIdentityReadModel context)
    {
        var next = BuildGameplaySessionContextState(context);
        if (StatesEqual(_current, next))
        {
            return;
        }

        _current = next;
    }

    private static ViewedContextState BuildGameplaySessionContextState(
        LiveMonitoringContextIdentityReadModel context) =>
        new()
        {
            AccountStableId = context.AccountStableId,
            CharacterRecordId = context.CharacterRecordId,
            IsFollowingLive = true,
            LiveFollowContextId = context.ContextId
        };

    private static bool StatesEqual(ViewedContextState left, ViewedContextState right) =>
        string.Equals(left.AccountStableId, right.AccountStableId, StringComparison.OrdinalIgnoreCase)
        && left.CharacterRecordId == right.CharacterRecordId
        && left.IsFollowingLive == right.IsFollowingLive
        && left.LiveFollowContextId == right.LiveFollowContextId;

    private void PublishIfStateChanged()
    {
        Changed?.Invoke(this, new ViewedContextChangedEventArgs { State = _current });
    }

    private List<LiveMonitoringContextIdentityReadModel> GetApplicableGameplaySessionContexts() =>
        GameplaySessionContextResolver.GetLiveMonitoringContexts(_identityReadService.Current).ToList();

    private void SetState(ViewedContextState state)
    {
        if (StatesEqual(_current, state))
        {
            return;
        }

        _current = state;
        PublishIfStateChanged();
    }

    private void PublishIfChanged()
    {
        PublishIfStateChanged();
    }
}
