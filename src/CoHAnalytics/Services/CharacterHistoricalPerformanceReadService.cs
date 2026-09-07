using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Aggregates immutable performance observations before deriving character-level metrics.
/// </summary>
public sealed class CharacterHistoricalPerformanceReadService : ICharacterHistoricalPerformanceReadService
{
    private readonly ICharacterPerformanceObservationRepository _observationRepository;
    private readonly ICharacterRepository? _characterRepository;

    public CharacterHistoricalPerformanceReadService(
        ICharacterPerformanceObservationRepository observationRepository,
        ICharacterRepository? characterRepository = null)
    {
        _observationRepository = observationRepository
            ?? throw new ArgumentNullException(nameof(observationRepository));
        _characterRepository = characterRepository;
        _observationRepository.Changed += OnObservationRepositoryChanged;
        if (_characterRepository is not null)
        {
            _characterRepository.StateChanged += OnCharacterRepositoryChanged;
        }
    }

    public event EventHandler? Changed;

    public CharacterHistoricalPerformanceSnapshot GetLifetime(
        CharacterRecordId characterRecordId)
    {
        ArgumentNullException.ThrowIfNull(characterRecordId);

        var canonicalRecordId = _characterRepository?.ResolveCanonicalRecordId(characterRecordId)
            ?? characterRecordId;
        var recordIds = _characterRepository?.GetRecordIdsResolvingTo(characterRecordId)
            ?? [characterRecordId];
        var observations = recordIds
            .SelectMany(_observationRepository.GetByCharacter)
            .Where(observation =>
                recordIds.Contains(observation.CharacterRecordId)
                && observation.IncludeInOverview
                && observation.ObservedDuration > TimeSpan.Zero);

        return Aggregate(canonicalRecordId, observations);
    }

    public IReadOnlyList<CharacterHistoricalPerformanceSegment> GetSegments(
        CharacterRecordId characterRecordId)
    {
        ArgumentNullException.ThrowIfNull(characterRecordId);

        var canonicalRecordId = _characterRepository?.ResolveCanonicalRecordId(characterRecordId)
            ?? characterRecordId;
        var recordIds = _characterRepository?.GetRecordIdsResolvingTo(characterRecordId)
            ?? [characterRecordId];
        var identitySet = recordIds.ToHashSet();

        return recordIds
            .SelectMany(_observationRepository.GetByCharacter)
            .Where(observation => identitySet.Contains(observation.CharacterRecordId))
            .OrderByDescending(observation => observation.EndedAtUtc)
            .ThenByDescending(observation => observation.StartedAtUtc)
            .ThenBy(observation => observation.GameplaySessionId.Value)
            .ThenBy(observation => observation.SegmentOrdinal)
            .Select(observation => new CharacterHistoricalPerformanceSegment
            {
                Observation = observation,
                CanonicalCharacterRecordId = canonicalRecordId,
                Metrics = Aggregate(canonicalRecordId, [observation])
            })
            .ToList();
    }

    private void OnObservationRepositoryChanged(object? sender, EventArgs e) =>
        Changed?.Invoke(this, EventArgs.Empty);

    private void OnCharacterRepositoryChanged(object? sender, CharacterRepositoryChangedEventArgs e) =>
        Changed?.Invoke(this, EventArgs.Empty);

    internal static CharacterHistoricalPerformanceSnapshot Aggregate(
        CharacterRecordId characterRecordId,
        IEnumerable<CharacterPerformanceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(characterRecordId);
        ArgumentNullException.ThrowIfNull(observations);

        long observationCount = 0;
        long observedDurationTicks = 0;
        long damageDealtHundredths = 0;
        long attempts = 0;
        long hits = 0;
        long rolledAttempts = 0;
        long displayedChanceSumHundredths = 0;
        long rollSumHundredths = 0;
        long forcedHits = 0;
        long autohits = 0;
        long totalDefeated = 0;
        long myDefeats = 0;
        long experienceGained = 0;
        long gameplayInfluenceGained = 0;

        checked
        {
            foreach (var observation in observations)
            {
                if (observation.ObservedDuration <= TimeSpan.Zero)
                {
                    continue;
                }

                observationCount++;
                observedDurationTicks += observation.ObservedDuration.Ticks;
                damageDealtHundredths += observation.DamageDealt.Hundredths;
                attempts += observation.Attempts;
                hits += observation.Hits;
                rolledAttempts += observation.RolledAttempts;
                displayedChanceSumHundredths += observation.DisplayedChanceSumHundredths;
                rollSumHundredths += observation.RollSumHundredths;
                forcedHits += observation.ForcedHits;
                autohits += observation.Autohits;
                totalDefeated += observation.TotalDefeated;
                myDefeats += observation.MyDefeats;
                experienceGained += observation.ExperienceGained;
                gameplayInfluenceGained += observation.GameplayInfluenceGained;
            }
        }

        if (observationCount == 0)
        {
            return CharacterHistoricalPerformanceSnapshot.Empty(characterRecordId);
        }

        var observedDuration = TimeSpan.FromTicks(observedDurationTicks);
        var damageDealt = new CombatScaledAmount(damageDealtHundredths);
        var accuracy = new CombatAccuracyScopeSnapshot
        {
            Attempts = attempts,
            Hits = hits,
            Misses = checked(attempts - hits),
            RolledAttempts = rolledAttempts,
            DisplayedChanceSumHundredths = displayedChanceSumHundredths,
            RollSumHundredths = rollSumHundredths,
            ForcedHits = forcedHits,
            Autohits = autohits
        };

        return new CharacterHistoricalPerformanceSnapshot
        {
            CharacterRecordId = characterRecordId,
            ObservationCount = observationCount,
            ObservedDuration = observedDuration,
            DamageDealt = damageDealt,
            DamagePerSecondHundredths = CalculateDamagePerSecondHundredths(
                damageDealt,
                observedDuration),
            Accuracy = accuracy,
            HitPercent = attempts > 0
                ? CombatAccuracyPresentation.CalculatePercentTenths(hits, attempts) / 10m
                : null,
            AverageDisplayedChance = rolledAttempts > 0
                ? CombatAccuracyPresentation.CalculateAverageTenths(
                    displayedChanceSumHundredths,
                    rolledAttempts) / 10m
                : null,
            AverageRoll = rolledAttempts > 0
                ? CombatAccuracyPresentation.CalculateAverageTenths(
                    rollSumHundredths,
                    rolledAttempts) / 10m
                : null,
            TotalDefeated = totalDefeated,
            MyDefeats = myDefeats,
            ExperienceGained = experienceGained,
            ExperiencePerHour = GameplaySessionTelemetryPresentation.CalculateRatePerHour(
                experienceGained,
                observedDuration),
            GameplayInfluenceGained = gameplayInfluenceGained,
            GameplayInfluencePerHour = GameplaySessionTelemetryPresentation.CalculateRatePerHour(
                gameplayInfluenceGained,
                observedDuration)
        };
    }

    private static long CalculateDamagePerSecondHundredths(
        CombatScaledAmount damageDealt,
        TimeSpan observedDuration)
    {
        if (damageDealt.Hundredths <= 0 || observedDuration <= TimeSpan.Zero)
        {
            return 0;
        }

        var rate = decimal.Truncate(
            (decimal)damageDealt.Hundredths
            * TimeSpan.TicksPerSecond
            / observedDuration.Ticks);
        return checked((long)rate);
    }
}
