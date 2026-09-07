using CoHAnalytics.Observations;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class InternalToolsViewModelDispatcherTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public InternalToolsViewModelDispatcherTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public async Task Observation_changed_from_background_thread_marshals_queue_refresh_without_throw()
    {
        FakeObservationService? observation = null;
        InternalToolsViewModel? viewModel = null;
        await _dispatcher.InvokeAsync(() =>
        {
            observation = new FakeObservationService();
            var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
            var classification = new AcquisitionClassificationService(
                StubInternalFeatureGate.Enabled,
                observation,
                catalog);

            viewModel = new InternalToolsViewModel(
                new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
                new FakeGameRuntimeService(),
                new AccountAnonymityService(StubInternalFeatureGate.Enabled),
                observation,
                catalog,
                classification);
        });

        try
        {
            var expected = new AcquisitionObservationListItem
            {
                NormalizedKey = "unmapped-test-item",
                ObservedText = "Unmapped Test Item",
                FamilyHint = ReferenceItemFamily.Salvage,
                ResolutionState = AcquisitionIdentityResolutionState.Unresolved,
                Disposition = ObservationDispositionStatus.New,
                OccurrenceCount = 1,
                FirstSeenUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow,
                EvidenceSampleCount = 1
            };

            await Task.Run(() => observation!.PublishChanged(expected));
            await _dispatcher.DrainAsync();
            await _dispatcher.InvokeAsync(() =>
            {
                var row = Assert.Single(viewModel!.NeedsClassification);
                Assert.Equal(expected.NormalizedKey, row.NormalizedKey);
                Assert.True(viewModel.HasObservationQueueItems);
            });
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => viewModel?.Dispose());
        }
    }

    private sealed class FakeObservationService : IAcquisitionObservationService
    {
        private AcquisitionObservationListItem[] _needsClassification = [];

        public event EventHandler? Changed;

        public AcquisitionObservationStatus GetStatus() => new()
        {
            Health = AcquisitionCaptureHealth.Active
        };

        public void RecordClassificationAttempt(bool resolved)
        {
        }

        public IReadOnlyList<AcquisitionObservationListItem> GetNeedsClassification(
            AcquisitionObservationFilter? filter = null) =>
            Volatile.Read(ref _needsClassification);

        public bool TryRecordUnresolvedAcquisition(AcquisitionObservationRecord observation) => true;

        public void PublishChanged(AcquisitionObservationListItem observation)
        {
            Volatile.Write(ref _needsClassification, [observation]);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
