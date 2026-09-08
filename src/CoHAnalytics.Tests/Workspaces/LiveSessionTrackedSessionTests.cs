using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(CoHAnalytics.Tests.WpfDispatcherCollection.Name)]
public sealed class LiveSessionTrackedSessionTests
{
    [Fact]
    public void Start_captures_presentation_baselines_and_accumulates_totals()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 1_000, influence: 200)
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();

        Assert.True(viewModel.StartTrackedSessionCommand.CanExecute(null));
        viewModel.StartTrackedSessionCommand.Execute(null);
        Assert.Equal("Running", viewModel.TrackedSessionStatusLabel);
        Assert.Equal("0", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("0", viewModel.TrackedSessionGameplayInfluenceLabel);

        identity.Current = Snapshot(contextId, startedAt, experience: 2_500, influence: 450);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal("1,500", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("250", viewModel.TrackedSessionGameplayInfluenceLabel);
        Assert.Equal("2,500", viewModel.SessionExperienceLabel);
        Assert.Equal("450", viewModel.SessionGameplayInfluenceLabel);
    }

    [Fact]
    public void Pause_freezes_tracked_values_while_panel_one_continues()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 1_000, influence: 100)
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);

        identity.Current = Snapshot(contextId, startedAt, experience: 2_000, influence: 200);
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.ToggleTrackedSessionPauseCommand.Execute(null);
        Assert.Equal("Paused", viewModel.TrackedSessionStatusLabel);

        identity.Current = Snapshot(contextId, startedAt, experience: 9_000, influence: 900);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal("9,000", viewModel.SessionExperienceLabel);
        Assert.Equal("1,000", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("100", viewModel.TrackedSessionGameplayInfluenceLabel);
    }

    [Fact]
    public void Resume_continues_accumulation_after_pause()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 500, influence: 50)
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);

        identity.Current = Snapshot(contextId, startedAt, experience: 1_000, influence: 100);
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.ToggleTrackedSessionPauseCommand.Execute(null);

        identity.Current = Snapshot(contextId, startedAt, experience: 4_000, influence: 400);
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.ToggleTrackedSessionPauseCommand.Execute(null);

        identity.Current = Snapshot(contextId, startedAt, experience: 4_500, influence: 450);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal("1,000", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("100", viewModel.TrackedSessionGameplayInfluenceLabel);
    }

    [Fact]
    public void Reset_clears_only_tracked_panel()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 3_000, influence: 300)
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);
        identity.Current = Snapshot(contextId, startedAt, experience: 5_000, influence: 500);
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.ResetTrackedSessionCommand.Execute(null);

        Assert.Equal("Not Started", viewModel.TrackedSessionStatusLabel);
        Assert.Equal("0", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("5,000", viewModel.SessionExperienceLabel);
        Assert.Equal("500", viewModel.SessionGameplayInfluenceLabel);
    }

    [Fact]
    public void Save_session_promotes_tracked_snapshot_to_saved()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                experience: 2_000,
                influence: 200,
                accountDisplayName: "TestAccount")
        };
        var store = new RecordingSessionStore();

        using var viewModel = CreateViewModel(identity, store);
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);
        identity.Current = Snapshot(
            contextId,
            startedAt,
            experience: 4_000,
            influence: 400,
            accountDisplayName: "TestAccount");
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.SaveTrackedSessionCommand.Execute(null);

        Assert.NotNull(store.LastSaved);
        Assert.Equal(SessionType.Tracked, store.LastSaved!.SessionType);
        Assert.Equal("Example Hero", store.LastSaved.Character);
        Assert.Equal("TestAccount", store.LastSaved.Account);
        Assert.Equal(2_000, store.LastSaved.Earnings.Experience);
        Assert.Equal(200, store.LastSaved.Earnings.Influence);
        Assert.Equal(SessionSaveOutcome.Saved, store.LastSaveOutcome);
    }

    [Fact]
    public void Completed_live_session_auto_persists_recent_history()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 3_000, influence: 300)
        };
        var store = new RecordingSessionStore();

        using var viewModel = CreateViewModel(identity, store);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            Array.Empty<LiveMonitoringContextIdentityReadModel>(),
            DateTimeOffset.UtcNow,
            1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.NotNull(store.LastCompletedLive);
        Assert.Equal(SessionType.Live, store.LastCompletedLive!.SessionType);
        Assert.Equal(3_000, store.LastCompletedLive.Earnings.Experience);
        Assert.Equal(300, store.LastCompletedLive.Earnings.Influence);
    }

    [Fact]
    public void Completed_tracked_session_auto_persists_recent_history()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var secondStartedAt = startedAt.AddHours(1);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 1_000, influence: 100)
        };
        var store = new RecordingSessionStore();

        using var viewModel = CreateViewModel(identity, store);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);
        identity.Current = Snapshot(contextId, startedAt, experience: 3_000, influence: 300);
        identity.RaiseChanged();
        DrainDispatcher();

        identity.Current = Snapshot(contextId, secondStartedAt, experience: 0, influence: 0);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.NotNull(store.LastCompletedTracked);
        Assert.Equal(SessionType.Tracked, store.LastCompletedTracked!.SessionType);
        Assert.Equal(2_000, store.LastCompletedTracked.Earnings.Experience);
        Assert.Equal(200, store.LastCompletedTracked.Earnings.Influence);
    }

    [Fact]
    public void Clear_session_does_not_persist_or_split_live_session_lifecycle()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                experience: 5_000,
                influence: 500,
                currencies:
                [
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Reward Merit",
                        Quantity = 20
                    }
                ],
                salvage:
                [
                    new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 10 }
                ])
        };
        var store = new RecordingSessionStore();

        using var viewModel = CreateViewModel(identity, store);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        var liveSessionId = (Guid?)GetPrivateField(viewModel, "_currentLiveSessionId");
        Assert.NotNull(liveSessionId);

        viewModel.ClearSessionCommand.Execute(null);

        identity.Current = Snapshot(
            contextId,
            startedAt,
            experience: 5_500,
            influence: 550,
            currencies:
            [
                new GameplaySessionRewardCurrencyTotal
                {
                    CurrencyDisplayName = "Reward Merit",
                    Quantity = 25
                }
            ],
            salvage:
            [
                new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 12 }
            ]);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(liveSessionId, (Guid?)GetPrivateField(viewModel, "_currentLiveSessionId"));
        Assert.Null(store.LastCompletedLive);

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            Array.Empty<LiveMonitoringContextIdentityReadModel>(),
            DateTimeOffset.UtcNow,
            1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.NotNull(store.LastCompletedLive);
        Assert.Equal(liveSessionId, store.LastCompletedLive!.SessionId);
        Assert.Equal(5_500, store.LastCompletedLive.Earnings.Experience);
        Assert.Equal(25, store.LastCompletedLive.Currencies.Single(currency => currency.Name == "Reward Merit").Quantity);
        Assert.Equal(12, store.LastCompletedLive.Loot.Salvage.Single().Quantity);
    }

    [Fact]
    public void Completed_live_session_persists_authoritative_currencies_and_loot()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                experience: 3_000,
                influence: 300,
                currencies:
                [
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Incarnate Thread",
                        Quantity = 7
                    }
                ],
                recipes:
                [
                    new GameplaySessionItemTotal
                    {
                        DisplayName = "Invention: Endurance Red. (Recipe)",
                        Quantity = 1
                    }
                ],
                enhancements:
                [
                    new GameplaySessionItemTotal { DisplayName = "Energy Weapon", Quantity = 2 }
                ],
                inspirations:
                [
                    new GameplaySessionItemTotal { DisplayName = "Luck", Quantity = 4 }
                ])
        };
        var store = new RecordingSessionStore();

        using var viewModel = CreateViewModel(identity, store);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            Array.Empty<LiveMonitoringContextIdentityReadModel>(),
            DateTimeOffset.UtcNow,
            1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.NotNull(store.LastCompletedLive);
        Assert.Equal(7, store.LastCompletedLive!.Currencies.Single().Quantity);
        Assert.Equal("Invention: Endurance Red. (Recipe)", store.LastCompletedLive.Loot.Recipes.Single().Name);
        Assert.Equal(2, store.LastCompletedLive.Loot.Enhancements.Single().Quantity);
        Assert.Equal(4, store.LastCompletedLive.Loot.Inspirations.Single().Quantity);
    }

    [Fact]
    public void Tracked_session_persists_only_post_start_economy_and_loot()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                experience: 1_000,
                influence: 100,
                currencies:
                [
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Reward Merit",
                        Quantity = 10
                    }
                ],
                salvage:
                [
                    new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 5 }
                ])
        };
        var store = new RecordingSessionStore();

        using var viewModel = CreateViewModel(identity, store);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);

        identity.Current = Snapshot(
            contextId,
            startedAt,
            experience: 1_500,
            influence: 180,
            currencies:
            [
                new GameplaySessionRewardCurrencyTotal
                {
                    CurrencyDisplayName = "Reward Merit",
                    Quantity = 13
                }
            ],
            salvage:
            [
                new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 8 }
            ]);
        identity.RaiseChanged();
        DrainDispatcher();

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            Array.Empty<LiveMonitoringContextIdentityReadModel>(),
            DateTimeOffset.UtcNow,
            1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.NotNull(store.LastCompletedTracked);
        Assert.Equal(500, store.LastCompletedTracked!.Earnings.Experience);
        Assert.Equal(80, store.LastCompletedTracked.Earnings.Influence);
        Assert.Equal(3, store.LastCompletedTracked.Currencies.Single().Quantity);
        Assert.Equal(3, store.LastCompletedTracked.Loot.Salvage.Single().Quantity);
    }

    [Fact]
    public void Tracked_pause_excludes_paused_rewards_from_persisted_totals()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                experience: 1_000,
                influence: 100,
                currencies:
                [
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Reward Merit",
                        Quantity = 2
                    }
                ],
                salvage:
                [
                    new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 1 }
                ])
        };
        var store = new RecordingSessionStore();

        using var viewModel = CreateViewModel(identity, store);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);

        identity.Current = Snapshot(
            contextId,
            startedAt,
            experience: 1_200,
            influence: 140,
            currencies:
            [
                new GameplaySessionRewardCurrencyTotal
                {
                    CurrencyDisplayName = "Reward Merit",
                    Quantity = 4
                }
            ],
            salvage:
            [
                new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 3 }
            ]);
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.ToggleTrackedSessionPauseCommand.Execute(null);

        identity.Current = Snapshot(
            contextId,
            startedAt,
            experience: 9_000,
            influence: 900,
            currencies:
            [
                new GameplaySessionRewardCurrencyTotal
                {
                    CurrencyDisplayName = "Reward Merit",
                    Quantity = 40
                }
            ],
            salvage:
            [
                new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 30 }
            ]);
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.ToggleTrackedSessionPauseCommand.Execute(null);

        identity.Current = Snapshot(
            contextId,
            startedAt,
            experience: 9_100,
            influence: 940,
            currencies:
            [
                new GameplaySessionRewardCurrencyTotal
                {
                    CurrencyDisplayName = "Reward Merit",
                    Quantity = 41
                }
            ],
            salvage:
            [
                new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 31 }
            ]);
        identity.RaiseChanged();
        DrainDispatcher();

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            Array.Empty<LiveMonitoringContextIdentityReadModel>(),
            DateTimeOffset.UtcNow,
            1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.NotNull(store.LastCompletedTracked);
        Assert.Equal(300, store.LastCompletedTracked!.Earnings.Experience);
        Assert.Equal(80, store.LastCompletedTracked.Earnings.Influence);
        Assert.Equal(3, store.LastCompletedTracked.Currencies.Single().Quantity);
        Assert.Equal(3, store.LastCompletedTracked.Loot.Salvage.Single().Quantity);
    }

    [Fact]
    public void Save_tracked_session_promotes_economy_and_loot()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 2_000, influence: 200)
        };
        var store = new RecordingSessionStore();

        using var viewModel = CreateViewModel(identity, store);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);
        identity.Current = Snapshot(
            contextId,
            startedAt,
            experience: 4_000,
            influence: 400,
            currencies:
            [
                new GameplaySessionRewardCurrencyTotal
                {
                    CurrencyDisplayName = "Empyrean Merit",
                    Quantity = 2
                }
            ],
            inspirations:
            [
                new GameplaySessionItemTotal { DisplayName = "Insight", Quantity = 1 }
            ]);
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.SaveTrackedSessionCommand.Execute(null);

        Assert.NotNull(store.LastSaved);
        Assert.Equal(2_000, store.LastSaved!.Earnings.Experience);
        Assert.Equal("Empyrean Merit", store.LastSaved.Currencies.Single().Name);
        Assert.Equal(2, store.LastSaved.Currencies.Single().Quantity);
        Assert.Equal("Insight", store.LastSaved.Loot.Inspirations.Single().Name);
    }

    [Fact]
    public void Live_and_tracked_sessions_persist_independently()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 1_000, influence: 100)
        };
        var store = new RecordingSessionStore();

        using var viewModel = CreateViewModel(identity, store);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);
        identity.Current = Snapshot(contextId, startedAt, experience: 2_500, influence: 250);
        identity.RaiseChanged();
        DrainDispatcher();

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            Array.Empty<LiveMonitoringContextIdentityReadModel>(),
            DateTimeOffset.UtcNow,
            1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.NotNull(store.LastCompletedLive);
        Assert.NotNull(store.LastCompletedTracked);
        Assert.NotEqual(store.LastCompletedLive!.SessionId, store.LastCompletedTracked!.SessionId);
    }

    [Fact]
    public void Save_session_skips_duplicate_unchanged_tracked_snapshot()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 2_000, influence: 200)
        };
        var directory = Path.Combine(Path.GetTempPath(), "coh-analytics-tracked-save-" + Guid.NewGuid().ToString("N"));
        var store = new SessionStore(directory);

        try
        {
            using var viewModel = CreateViewModel(identity, store);
            DrainDispatcher();
            identity.RaiseChanged();
            DrainDispatcher();

            viewModel.StartTrackedSessionCommand.Execute(null);
            identity.Current = Snapshot(contextId, startedAt, experience: 4_000, influence: 400);
            identity.RaiseChanged();
            DrainDispatcher();

            viewModel.SaveTrackedSessionCommand.Execute(null);
            viewModel.SaveTrackedSessionCommand.Execute(null);

            Assert.Single(store.GetSavedSessions());
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void New_gameplay_session_stops_active_tracking_and_preserves_values()
    {
        var contextId = MonitoringContextId.CreateNew();
        var firstStartedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var secondStartedAt = firstStartedAt.AddHours(1);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, firstStartedAt, experience: 1_000, influence: 100)
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);

        identity.Current = Snapshot(contextId, firstStartedAt, experience: 3_000, influence: 300);
        identity.RaiseChanged();
        DrainDispatcher();

        identity.Current = Snapshot(contextId, secondStartedAt, experience: 0, influence: 0);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal("Stopped", viewModel.TrackedSessionStatusLabel);
        Assert.Equal("2,000", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("200", viewModel.TrackedSessionGameplayInfluenceLabel);
        Assert.True(viewModel.StartTrackedSessionCommand.CanExecute(null));
    }

    [Fact]
    public void Homecoming_exit_freezes_tracked_session_values()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 1_000, influence: 100)
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);

        identity.Current = Snapshot(contextId, startedAt, experience: 2_500, influence: 250);
        identity.RaiseChanged();
        DrainDispatcher();

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            Array.Empty<LiveMonitoringContextIdentityReadModel>(),
            DateTimeOffset.UtcNow,
            1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal("Stopped", viewModel.TrackedSessionStatusLabel);
        Assert.Equal("1,500", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("150", viewModel.TrackedSessionGameplayInfluenceLabel);
    }

    [Fact]
    public void Zero_elapsed_tracked_rates_do_not_throw()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt)
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);

        Assert.Equal("—", viewModel.TrackedSessionExperienceRateLabel);
        Assert.Equal("—", viewModel.TrackedSessionGameplayInfluenceRateLabel);
    }

    [Fact]
    public void Start_after_long_gameplay_session_begins_tracked_duration_at_zero()
    {
        var contextId = MonitoringContextId.CreateNew();
        var gameplayStartedAt = DateTimeOffset.UtcNow.AddMinutes(-45);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                gameplayStartedAt,
                experience: 1_000_000,
                influence: 800_000)
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);

        Assert.Equal("00:00:00", viewModel.TrackedSessionDurationLabel);
        Assert.Equal("0", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("0", viewModel.TrackedSessionGameplayInfluenceLabel);
        Assert.Equal("—", viewModel.TrackedSessionExperienceRateLabel);
        Assert.Equal("—", viewModel.TrackedSessionGameplayInfluenceRateLabel);

        SetTrackedStartedAt(viewModel, DateTimeOffset.UtcNow.AddMinutes(-5));
        RefreshTrackedSessionPresentation(viewModel, identity.Current.Contexts[0]);

        Assert.Equal("00:05:00", viewModel.TrackedSessionDurationLabel);
        Assert.NotEqual("00:50:00", viewModel.TrackedSessionDurationLabel);
    }

    [Fact]
    public void Pause_resume_excludes_paused_time_from_tracked_duration()
    {
        var contextId = MonitoringContextId.CreateNew();
        var gameplayStartedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(contextId, gameplayStartedAt, experience: 10_000, influence: 5_000)
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);
        SetTrackedStartedAt(viewModel, DateTimeOffset.UtcNow.AddMinutes(-25));

        viewModel.ToggleTrackedSessionPauseCommand.Execute(null);
        SetTrackedPauseStartedAt(viewModel, DateTimeOffset.UtcNow.AddMinutes(-5));

        viewModel.ToggleTrackedSessionPauseCommand.Execute(null);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal("00:20:00", viewModel.TrackedSessionDurationLabel);
    }

    [Fact]
    public void Clear_session_does_not_alter_tracked_duration_clock()
    {
        var contextId = MonitoringContextId.CreateNew();
        var gameplayStartedAt = DateTimeOffset.UtcNow.AddMinutes(-45);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                gameplayStartedAt,
                experience: 1_000_000,
                influence: 800_000)
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);
        var trackedStartedAt = DateTimeOffset.UtcNow.AddMinutes(-12);
        SetTrackedStartedAt(viewModel, trackedStartedAt);
        RefreshTrackedSessionPresentation(viewModel, identity.Current.Contexts[0]);

        Assert.Equal("00:12:00", viewModel.TrackedSessionDurationLabel);

        viewModel.ClearSessionCommand.Execute(null);

        Assert.Equal(trackedStartedAt, GetTrackedStartedAt(viewModel));
        RefreshTrackedSessionPresentation(viewModel, identity.Current.Contexts[0]);
        Assert.Equal("00:12:00", viewModel.TrackedSessionDurationLabel);
    }

    [Fact]
    public void Tracked_earnings_duration_advances_from_wall_clock_without_new_snapshot()
    {
        var contextId = MonitoringContextId.CreateNew();
        var gameplayStartedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                gameplayStartedAt,
                experience: 50_000,
                influence: 20_000,
                trackedEarnings: new TrackedEarningsScopeSnapshot
                {
                    IsTracking = true,
                    StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    ActiveElapsed = TimeSpan.FromSeconds(1),
                    ExperienceGained = 1_500,
                    InfluenceGained = 900
                })
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);

        SetTrackedStartedAt(viewModel, DateTimeOffset.UtcNow.AddMinutes(-2));
        RefreshTrackedSessionPresentation(viewModel, identity.Current.Contexts[0]);

        Assert.Equal("00:02:00", viewModel.TrackedSessionDurationLabel);
        Assert.Equal("1,500", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("900", viewModel.TrackedSessionGameplayInfluenceLabel);
        // Stale snapshot ActiveElapsed must not win over wall-clock elapsed.
        Assert.NotEqual("00:00:01", viewModel.TrackedSessionDurationLabel);

        SetTrackedStartedAt(viewModel, DateTimeOffset.UtcNow.AddMinutes(-3));
        RefreshTrackedSessionPresentation(viewModel, identity.Current.Contexts[0]);

        Assert.Equal("00:03:00", viewModel.TrackedSessionDurationLabel);
        Assert.Equal("1,500", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("900", viewModel.TrackedSessionGameplayInfluenceLabel);
    }

    [Fact]
    public void Paused_tracked_earnings_duration_does_not_advance_on_refresh()
    {
        var contextId = MonitoringContextId.CreateNew();
        var gameplayStartedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                gameplayStartedAt,
                experience: 10_000,
                influence: 5_000,
                trackedEarnings: new TrackedEarningsScopeSnapshot
                {
                    IsTracking = true,
                    StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                    ActiveElapsed = TimeSpan.FromMinutes(10),
                    ExperienceGained = 400,
                    InfluenceGained = 200
                })
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);
        SetTrackedStartedAt(viewModel, DateTimeOffset.UtcNow.AddMinutes(-10));
        RefreshTrackedSessionPresentation(viewModel, identity.Current.Contexts[0]);
        Assert.Equal("00:10:00", viewModel.TrackedSessionDurationLabel);
        Assert.Equal("400", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("200", viewModel.TrackedSessionGameplayInfluenceLabel);

        viewModel.ToggleTrackedSessionPauseCommand.Execute(null);
        SetTrackedPauseStartedAt(viewModel, DateTimeOffset.UtcNow.AddMinutes(-1));

        identity.Current = Snapshot(
            contextId,
            gameplayStartedAt,
            experience: 10_000,
            influence: 5_000,
            trackedEarnings: new TrackedEarningsScopeSnapshot
            {
                IsTracking = true,
                IsPaused = true,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
                ActiveElapsed = TimeSpan.FromMinutes(20),
                ExperienceGained = 400,
                InfluenceGained = 200
            });
        RefreshTrackedSessionPresentation(viewModel, identity.Current.Contexts[0]);

        Assert.Equal("00:10:00", viewModel.TrackedSessionDurationLabel);
        Assert.Equal("400", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("200", viewModel.TrackedSessionGameplayInfluenceLabel);
    }

    [Fact]
    public void Frozen_tracked_earnings_duration_does_not_advance_on_refresh()
    {
        var contextId = MonitoringContextId.CreateNew();
        var gameplayStartedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var identity = new TrackedFakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                gameplayStartedAt,
                experience: 10_000,
                influence: 5_000,
                trackedEarnings: new TrackedEarningsScopeSnapshot
                {
                    IsTracking = true,
                    StartedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
                    ActiveElapsed = TimeSpan.FromMinutes(8),
                    ExperienceGained = 700,
                    InfluenceGained = 350
                })
        };

        using var viewModel = CreateViewModel(identity);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.StartTrackedSessionCommand.Execute(null);
        SetTrackedStartedAt(viewModel, DateTimeOffset.UtcNow.AddMinutes(-8));
        RefreshTrackedSessionPresentation(viewModel, identity.Current.Contexts[0]);
        Assert.Equal("00:08:00", viewModel.TrackedSessionDurationLabel);

        SetPrivateField(viewModel, "_isTrackedSessionFrozen", true);
        SetPrivateField(viewModel, "_isTrackedSessionRunning", false);

        identity.Current = Snapshot(
            contextId,
            gameplayStartedAt,
            experience: 10_000,
            influence: 5_000,
            trackedEarnings: new TrackedEarningsScopeSnapshot
            {
                IsTracking = false,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                ActiveElapsed = TimeSpan.FromMinutes(30),
                ExperienceGained = 700,
                InfluenceGained = 350
            });
        SetTrackedStartedAt(viewModel, DateTimeOffset.UtcNow.AddMinutes(-30));
        RefreshTrackedSessionPresentation(viewModel, identity.Current.Contexts[0]);

        Assert.Equal("00:08:00", viewModel.TrackedSessionDurationLabel);
        Assert.Equal("700", viewModel.TrackedSessionExperienceLabel);
        Assert.Equal("350", viewModel.TrackedSessionGameplayInfluenceLabel);
    }

    private static void SetTrackedStartedAt(LiveSessionViewModel viewModel, DateTimeOffset startedAt) =>
        SetPrivateField(viewModel, "_trackedStartedAt", startedAt);

    private static void SetTrackedPauseStartedAt(LiveSessionViewModel viewModel, DateTimeOffset pauseStartedAt) =>
        SetPrivateField(viewModel, "_trackedPauseStartedAt", pauseStartedAt);

    private static DateTimeOffset? GetTrackedStartedAt(LiveSessionViewModel viewModel) =>
        (DateTimeOffset?)GetPrivateField(viewModel, "_trackedStartedAt");

    private static void RefreshTrackedSessionPresentation(
        LiveSessionViewModel viewModel,
        LiveMonitoringContextIdentityReadModel active)
    {
        var method = typeof(LiveSessionViewModel).GetMethod(
            "RefreshTrackedSessionPresentation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method!.Invoke(viewModel, [active]);
    }

    private static void SetPrivateField(LiveSessionViewModel viewModel, string fieldName, object? value)
    {
        var field = typeof(LiveSessionViewModel).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(viewModel, value);
    }

    private static object? GetPrivateField(LiveSessionViewModel viewModel, string fieldName)
    {
        var field = typeof(LiveSessionViewModel).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        return field!.GetValue(viewModel);
    }

    private static LiveSessionViewModel CreateViewModel(
        TrackedFakeIdentityReadService identity,
        ISessionStore? store = null) =>
        TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity, sessionStore: store);

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        MonitoringContextId contextId,
        DateTimeOffset startedAt,
        long experience = 0,
        long influence = 0,
        string accountDisplayName = "acct-1",
        IReadOnlyList<GameplaySessionRewardCurrencyTotal>? currencies = null,
        IReadOnlyList<GameplaySessionItemTotal>? salvage = null,
        IReadOnlyList<GameplaySessionItemTotal>? recipes = null,
        IReadOnlyList<GameplaySessionItemTotal>? enhancements = null,
        IReadOnlyList<GameplaySessionItemTotal>? inspirations = null,
        TrackedEarningsScopeSnapshot? trackedEarnings = null)
    {
        var context = new LiveMonitoringContextIdentityReadModel
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: Example Hero",
            CharacterDisplayName = "Example Hero",
            AccountDisplayName = accountDisplayName,
            AccountStableId = "acct-stable",
            SessionStartedAt = startedAt,
            SessionExperienceGained = experience,
            SessionGameplayInfluenceGained = influence,
            RewardCurrencyTotals = currencies ?? [],
            SalvageTotals = salvage ?? [],
            RecipeTotals = recipes ?? [],
            EnhancementTotals = enhancements ?? [],
            InspirationTotals = inspirations ?? [],
            TrackedEarnings = trackedEarnings ?? TrackedEarningsScopeSnapshot.Empty
        };

        return GameplaySessionIdentityReadModelSnapshot.Create([context], DateTimeOffset.UtcNow, 1);
    }

    private static void DrainDispatcher()
    {
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }

    private sealed class TrackedFakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current { get; set; } =
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed;

        public void Dispose()
        {
        }

        public void RaiseChanged() =>
            Changed?.Invoke(
                this,
                new GameplaySessionIdentityReadModelChangedEventArgs { Snapshot = Current });
    }

    private sealed class TrackedFakeApplicationOrchestrator : IApplicationOrchestrator
    {
        public ApplicationStateSnapshot Current { get; set; } =
            ApplicationStateSnapshot.Empty(DateTimeOffset.UtcNow);

        public event EventHandler<ApplicationStateChangedEventArgs>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(string? providerId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TrackedFakeGameplaySessionManager : IGameplaySessionManager
    {
        public GameplaySessionManagerSnapshot Current { get; set; } = GameplaySessionManagerSnapshot.Empty;

        public GameplaySessionDiagnostics Diagnostics { get; set; } =
            GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(isRunning: false);

        public event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<GameplaySessionEventsAvailableEventArgs>? CommittedEventsAvailable
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public GameplaySessionOperationResult ConfirmCharacter(
            MonitoringContextId contextId,
            CharacterRecordId characterRecordId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ClearIdentity(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult StartTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult PauseTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ResumeTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult StopTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ResetTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public void ResetForNewRuntimeGeneration()
        {
        }

        public GameplaySessionDiagnostics GetDiagnostics() => Diagnostics;
    }

    private sealed class RecordingSessionStore : ISessionStore
    {
        public PersistedSessionDocument? LastCompletedLive { get; private set; }

        public PersistedSessionDocument? LastCompletedTracked { get; private set; }

        public PersistedSessionDocument? LastSaved { get; private set; }

        public SessionSaveOutcome? LastSaveOutcome { get; private set; }

        public string SessionRootDirectory => "test-sessions";

        public IReadOnlyList<string> LegacyBenchmarkFilePaths => [];

        public IReadOnlyList<string> MalformedFileReports => [];

        public IReadOnlyList<PersistedSessionDocument> GetRecentLiveSessions() =>
            LastCompletedLive is null ? [] : [LastCompletedLive];

        public IReadOnlyList<PersistedSessionDocument> GetRecentTrackedSessions() =>
            LastCompletedTracked is null ? [] : [LastCompletedTracked];

        public IReadOnlyList<PersistedSessionDocument> GetSavedSessions() =>
            LastSaved is null ? [] : [LastSaved];

        public PersistedSessionDocument? GetSession(Guid sessionId) =>
            LastSaved?.SessionId == sessionId
                ? LastSaved
                : LastCompletedLive?.SessionId == sessionId
                    ? LastCompletedLive
                    : LastCompletedTracked?.SessionId == sessionId
                        ? LastCompletedTracked
                        : null;

        public string PersistCompletedLiveSession(PersistedSessionDocument document)
        {
            LastCompletedLive = document;
            return Path.Combine(SessionRootDirectory, "Live", $"{document.SessionId}.json");
        }

        public string PersistCompletedTrackedSession(PersistedSessionDocument document)
        {
            LastCompletedTracked = document;
            return Path.Combine(SessionRootDirectory, "Tracked", $"{document.SessionId}.json");
        }

        public SessionSaveResult SaveSession(PersistedSessionDocument document)
        {
            var duplicate = LastSaved is not null
                && SessionStore.DocumentsAreEquivalent(LastSaved, document);
            LastSaved = document;
            LastSaveOutcome = duplicate
                ? SessionSaveOutcome.DuplicateSkipped
                : SessionSaveOutcome.Saved;
            return new SessionSaveResult(
                LastSaveOutcome.Value,
                Path.Combine(SessionRootDirectory, "Saved", $"{document.SessionId}.json"));
        }

        public void DeleteSavedSession(Guid sessionId)
        {
            if (LastSaved?.SessionId == sessionId)
            {
                LastSaved = null;
            }
        }
    }
}
