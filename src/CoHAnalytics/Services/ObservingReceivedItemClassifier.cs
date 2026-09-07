using CoHAnalytics.Models;
using CoHAnalytics.Observations;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Services;

/// <summary>
/// Decorates <see cref="IGameplayReceivedItemClassifier"/> to capture unresolved acquisitions
/// without altering classification results.
/// </summary>
internal sealed class ObservingReceivedItemClassifier : IGameplayReceivedItemClassifier
{
    private readonly IGameplayReceivedItemClassifier _inner;
    private readonly IAcquisitionObservationService _observationService;
    private readonly Func<string?> _catalogVersionProvider;
    private readonly TimeProvider _timeProvider;

    public ObservingReceivedItemClassifier(
        IGameplayReceivedItemClassifier inner,
        IAcquisitionObservationService observationService,
        Func<string?> catalogVersionProvider,
        TimeProvider? timeProvider = null)
    {
        _inner = inner;
        _observationService = observationService;
        _catalogVersionProvider = catalogVersionProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryClassify(
        string rawItemText,
        out GameplaySessionRewardCategory category,
        out ReceivedItemClassificationMetadata? metadata)
    {
        var result = Classify(rawItemText);
        category = result.PresentationCategory;
        metadata = result.Metadata;
        return result.HasKnownPresentation;
    }

    public ReceivedItemClassificationResult Classify(string rawItemText)
    {
        var result = _inner.Classify(rawItemText);
        _observationService.RecordClassificationAttempt(result.ResolutionState);

        if (result.ResolutionState is AcquisitionIdentityResolutionState.Resolved)
        {
            return result;
        }

        if (string.IsNullOrWhiteSpace(rawItemText))
        {
            return result;
        }

        try
        {
            var observedText = rawItemText.Trim();
            var observation = new AcquisitionObservationRecord
            {
                ProducerId = AcquisitionObservationConstants.ProducerId,
                ObservationKind = AcquisitionObservationConstants.ObservationKind,
                ObservedText = observedText,
                NormalizedLookupKey = ItemReferenceLookup.NormalizeLookupKey(observedText),
                CapturedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime(),
                FailedCatalogVersion = _catalogVersionProvider() ?? string.Empty,
                GrammarSource = AcquisitionObservationGrammarInference.InferGrammarSource(observedText),
                FamilyHint = result.FamilyHint,
                ResolutionStateAtCapture = result.ResolutionState
            };

            _observationService.TryRecordUnresolvedAcquisition(observation);
        }
        catch
        {
        }

        return result;
    }
}
