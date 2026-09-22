using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class LiveSessionFirstUseCandidateTests
{
    [Fact]
    public void Confirming_log_derived_picker_option_establishes_manual_trust_then_confirms()
    {
        var contextId = MonitoringContextId.CreateNew();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        MonitoringContextId? confirmedContextId = null;
        CharacterRecordId? confirmedRecordId = null;
        var gameplay = new TestGameplaySessionContextSupport.FakeGameplaySessionManager
        {
            ConfirmCharacterInvoked = (id, recordId) =>
            {
                confirmedContextId = id;
                confirmedRecordId = recordId;
            }
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(new LiveMonitoringContextIdentityReadModel
            {
                ContextId = contextId,
                ContextState = MonitoringContextState.Ready,
                AccountStableId = "acct-1",
                AccountDisplayName = "acct-1",
                CharacterIdentityConfidence = CharacterIdentityConfidence.Unknown,
                CharacterIdentityResolutionState = CharacterIdentityResolutionState.Candidate,
                SessionLifecycleState = GameplaySessionLifecycleState.Active,
                HasActiveSession = true,
                CandidateCount = 1,
                RequiresManualSelection = true,
                IdentityStatusLabel = "Unknown",
                IdentityDetail = "Identity candidates were observed. Select a character or wait for a Welcome message.",
                PickerCharacters =
                [
                    new CharacterPickerOptionReadModel
                    {
                        RecordId = null,
                        DisplayName = "Hell's Vengence",
                        LastObservedAt = DateTimeOffset.UtcNow
                    }
                ]
            })
        };

        try
        {
            var viewed = new ViewedContextService(identity, repository);
            using var viewModel = new LiveSessionViewModel(
                new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
                new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running },
                gameplay,
                identity,
                new GameplaySessionContextResolver(identity, viewed),
                viewed,
                characterRepository: repository);
            DrainDispatcher();

            Assert.True(viewModel.ViewedRequiresManualSelection);
            viewModel.OpenCharacterPickerCommand.Execute(null);
            Assert.Equal("Hell's Vengence", viewModel.ViewedContextPanel!.SelectedPickerCharacter!.DisplayName);
            Assert.Null(viewModel.ViewedContextPanel.SelectedPickerCharacter.RecordId);

            viewModel.ConfirmSelectedCharacterCommand.Execute(null);
            DrainDispatcher();

            var record = repository.TryFindTrustedByDisplayName("acct-1", "Hell's Vengence");
            Assert.NotNull(record);
            Assert.Equal(CharacterTrustState.TrustedFromManualConfirmation, record.TrustState);
            Assert.Equal(contextId, confirmedContextId);
            Assert.Equal(record.RecordId, confirmedRecordId);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        LiveMonitoringContextIdentityReadModel context) =>
        GameplaySessionIdentityReadModelSnapshot.Create([context], DateTimeOffset.UtcNow, revision: 1);

    private static void DrainDispatcher()
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(DispatcherPriority.Background, static () => { });
    }

    private sealed class FakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current { get; set; } =
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
