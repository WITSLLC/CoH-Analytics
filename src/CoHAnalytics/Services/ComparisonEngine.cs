using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Pure comparison of two authoritative <see cref="CombatAnalyticsProjection"/> values.
/// Does not parse, dedup, run <c>CombatEngine</c>, load catalogs, persist, or rank.
/// </summary>
public sealed class ComparisonEngine
{
    public AnalyticalComparison Compare(AnalyticalComparisonInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Compare(input.Left, input.Right);
    }

    public AnalyticalComparison Compare(CombatAnalyticsProjection left, CombatAnalyticsProjection right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Compare(LiveView(left), LiveView(right));
    }

    public AnalyticalComparison Compare(AnalyticalProjectionView left, AnalyticalProjectionView right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(left.Projection);
        ArgumentNullException.ThrowIfNull(right.Projection);

        var leftContext = Context(left);
        var rightContext = Context(right);
        if (leftContext.AnalyticsSemanticVersion != AnalyticsSemanticVersion.Current
            || rightContext.AnalyticsSemanticVersion != AnalyticsSemanticVersion.Current
            || left.Projection.AnalyticsSemanticVersion != AnalyticsSemanticVersion.Current
            || right.Projection.AnalyticsSemanticVersion != AnalyticsSemanticVersion.Current)
        {
            return SemanticMismatch(leftContext, rightContext, left.Projection, right.Projection);
        }

        var session = CompareSession(left, right);
        var clock = CompareClock(left.Projection.Clock, right.Projection.Clock);
        var powers = ComparePowers(left.Projection.Powers, right.Projection.Powers);
        var actors = CompareActors(left.Projection.Actors, right.Projection.Actors);
        var outgoingTypes = CompareDamageTypes(
            left.Projection.DamageTypes,
            right.Projection.DamageTypes,
            left.Projection.DamageTypeBreakdown,
            right.Projection.DamageTypeBreakdown);
        var incomingTypes = CompareDamageTypes(
            left.Projection.IncomingDamageTypes,
            right.Projection.IncomingDamageTypes,
            left.Projection.IncomingDamageTypeBreakdown,
            right.Projection.IncomingDamageTypeBreakdown);
        var targets = CompareTargets(
            left.Projection.Targets,
            right.Projection.Targets,
            TargetCoverageComplete(left.Projection),
            TargetCoverageComplete(right.Projection));
        var build = CompareBuild(
            leftContext,
            rightContext,
            left.Projection.BuildContext,
            right.Projection.BuildContext);
        var attribution = leftContext.AttributionPolicyVersion != rightContext.AttributionPolicyVersion
            || leftContext.AttributionPolicyVersion != left.Projection.Attribution.AttributionPolicyVersion
            || rightContext.AttributionPolicyVersion != right.Projection.Attribution.AttributionPolicyVersion
            ? BlockedAttribution(
                left.Projection.Attribution,
                right.Projection.Attribution,
                ComparisonReason.AttributionPolicyMismatch,
                leftContext.AttributionPolicyVersion,
                rightContext.AttributionPolicyVersion)
            : CompareAttribution(left.Projection.Attribution, right.Projection.Attribution);
        var exactTargets = session.DistinctTargetCount.State == ComparisonState.Comparable
            && !session.DistinctTargetCount.Left.Coverage.GetValueOrDefault().LowerBound
            && !session.DistinctTargetCount.Right.Coverage.GetValueOrDefault().LowerBound
            && !session.DistinctTargetCount.Left.Coverage.GetValueOrDefault().Overflow
            && !session.DistinctTargetCount.Right.Coverage.GetValueOrDefault().Overflow;

        return new AnalyticalComparison
        {
            Left = leftContext,
            Right = rightContext,
            Compatibility = Overall(
                leftContext,
                rightContext,
                session,
                clock,
                powers,
                actors,
                outgoingTypes,
                incomingTypes,
                targets,
                build,
                attribution),
            Session = session,
            Clock = clock,
            Powers = powers,
            Actors = actors,
            OutgoingDamageTypes = outgoingTypes,
            IncomingDamageTypes = incomingTypes,
            Targets = targets,
            Build = build,
            Attribution = attribution,
            ExactTargetCardinalityComparable = exactTargets
        };
    }

    private static AnalyticalProjectionView LiveView(CombatAnalyticsProjection projection) =>
        new()
        {
            SourceKind = AnalyticalProjectionSourceKind.Live,
            Projection = projection
        };

    private static ComparisonSourceContext Context(AnalyticalProjectionView view)
    {
        var projection = view.Projection;
        var header = view.Header;
        return new ComparisonSourceContext
        {
            SourceKind = view.SourceKind,
            SegmentId = header?.SegmentId,
            CharacterRecordId = header?.CanonicalCharacterRecordId
                ?? header?.CharacterRecordId
                ?? projection.BuildContext.CharacterRecordId,
            CaptureStartUtc = header?.CaptureStartUtc ?? projection.Clock.CaptureStartUtc,
            CaptureEndUtc = header?.CaptureEndUtc ?? projection.Clock.CaptureEndUtc,
            AnalyticsSemanticVersion = header is null
                || view.SourceKind == AnalyticalProjectionSourceKind.HistoricalLegacy
                ? projection.AnalyticsSemanticVersion
                : header.AnalyticsSemanticVersion ?? 0,
            BuildManifestHash = header?.BuildManifestHash ?? projection.BuildContext.ManifestHash,
            BuildCatalogFingerprint = header?.BuildCatalogFingerprint
                ?? projection.BuildContext.BuildCatalogFingerprint,
            AttributionPolicyVersion = header is null
                || view.SourceKind == AnalyticalProjectionSourceKind.HistoricalLegacy
                ? projection.Attribution.AttributionPolicyVersion
                : header.AttributionPolicyVersion ?? 0,
            CoverageLimited = header?.CoverageLimited == true
                || projection.CoverageLimited
                || view.Coverage?.CoverageLimited == true,
            CaptureKind = header?.CaptureKind
        };
    }

    private static AnalyticalComparison SemanticMismatch(
        ComparisonSourceContext left,
        ComparisonSourceContext right,
        CombatAnalyticsProjection leftProjection,
        CombatAnalyticsProjection rightProjection)
    {
        var reason = ComparisonReason.SemanticVersionMismatch;
        return new AnalyticalComparison
        {
            Left = left,
            Right = right,
            Compatibility = AnalyticalComparisonCompatibility.IncompatibleSemanticVersion,
            Session = BlockedSession(leftProjection.Session.Metrics, rightProjection.Session.Metrics, reason),
            Clock = BlockedClock(leftProjection.Clock, rightProjection.Clock, reason),
            Build = CompareBuild(left, right, leftProjection.BuildContext, rightProjection.BuildContext),
            Attribution = BlockedAttribution(leftProjection.Attribution, rightProjection.Attribution, reason),
            ExactTargetCardinalityComparable = false
        };
    }

    private static SessionMetricComparison CompareSession(
        AnalyticalProjectionView leftView,
        AnalyticalProjectionView rightView)
    {
        var left = leftView.Projection;
        var right = rightView.Projection;
        var lm = left.Session.Metrics;
        var rm = right.Session.Metrics;
        return new SessionMetricComparison
        {
            DamageDealt = CompareAmount(lm.DamageDealt, rm.DamageDealt),
            DamageDealtSelf = CompareAmount(lm.DamageDealtSelf, rm.DamageDealtSelf),
            DamageDealtOwnedPets = CompareAmount(lm.DamageDealtOwnedPets, rm.DamageDealtOwnedPets),
            DamageReceived = CompareAmount(lm.DamageReceived, rm.DamageReceived),
            DamageReceivedOwnedPets = CompareAmount(lm.DamageReceivedOwnedPets, rm.DamageReceivedOwnedPets),
            HealingDealt = CompareAmount(lm.HealingDealt, rm.HealingDealt),
            HealingReceived = CompareAmount(lm.HealingReceived, rm.HealingReceived),
            EnduranceGranted = CompareAmount(lm.EnduranceGranted, rm.EnduranceGranted),
            EnduranceReceived = CompareAmount(lm.EnduranceReceived, rm.EnduranceReceived),
            DamageEventCount = CompareLong(lm.DamageEventCount, rm.DamageEventCount),
            ActivationCount = CompareLong(lm.ActivationCount, rm.ActivationCount),
            AttackResolutionCount = CompareLong(lm.AttackResolutionCount, rm.AttackResolutionCount),
            DefeatCount = CompareLong(
                DefeatMetric(left.Session, leftView.SourceKind),
                DefeatMetric(right.Session, rightView.SourceKind)),
            MyDefeatCount = CompareLong(
                MyDefeatMetric(left.Session, leftView.SourceKind),
                MyDefeatMetric(right.Session, rightView.SourceKind)),
            ConfirmedRechargeCompletedCount = CompareLong(lm.ConfirmedRechargeCompletedCount, rm.ConfirmedRechargeCompletedCount),
            ConfirmedStillRechargingCount = CompareLong(lm.ConfirmedStillRechargingCount, rm.ConfirmedStillRechargingCount),
            UnmatchedRechargeCandidateCount = CompareLong(lm.UnmatchedRechargeCandidateCount, rm.UnmatchedRechargeCandidateCount),
            DistinctTargetCount = CompareLong(lm.DistinctTargetCount, rm.DistinctTargetCount),
            TheoreticalRecharge = CompareTime(lm.TheoreticalRecharge, rm.TheoreticalRecharge),
            PermaHasten = CompareTime(lm.PermaHasten, rm.PermaHasten),
            Overkill = CompareAmount(lm.Overkill, rm.Overkill),
            MezDuration = CompareTime(lm.MezDuration, rm.MezDuration),
            PetInstanceCount = CompareLong(lm.PetInstanceCount, rm.PetInstanceCount),
            Accuracy = CompareAccuracy(lm.Accuracy, rm.Accuracy)
        };
    }

    private static ClockMetricComparison CompareClock(SegmentClock left, SegmentClock right)
    {
        var wall = CompareTime(left.WallClockDuration, right.WallClockDuration);
        return new ClockMetricComparison
        {
            WallClockDuration = wall,
            ObservedAnalyticalSpan = CompareTime(left.ObservedAnalyticalSpan, right.ObservedAnalyticalSpan),
            TrackedPauseAdjustedDuration = CompareTime(left.TrackedPauseAdjustedDuration, right.TrackedPauseAdjustedDuration),
            ActiveDuration = CompareTime(left.ActiveDuration, right.ActiveDuration),
            WallClockDamagePerSecondHundredths = CompareLong(
                left.WallClockDamagePerSecondHundredths,
                right.WallClockDamagePerSecondHundredths),
            ActiveDamagePerSecondHundredths = CompareLong(
                left.ActiveDamagePerSecondHundredths,
                right.ActiveDamagePerSecondHundredths),
            WallClockDurationsEqual = wall.State == ComparisonState.Comparable
                && wall.AbsoluteDelta.HasCompleteValue
                && wall.AbsoluteDelta.Value == TimeSpan.Zero
        };
    }

    private static AccuracyMetricComparison CompareAccuracy(
        MetricRef<CombatAccuracyScopeSnapshot> left,
        MetricRef<CombatAccuracyScopeSnapshot> right)
    {
        var hits = CompareLong(AccuracyCount(left, static s => s.Hits), AccuracyCount(right, static s => s.Hits));
        var misses = CompareLong(AccuracyCount(left, static s => s.Misses), AccuracyCount(right, static s => s.Misses));
        var attempts = CompareLong(AccuracyCount(left, static s => s.Attempts), AccuracyCount(right, static s => s.Attempts));
        var components = new[] { hits.State, misses.State, attempts.State };
        var state = components.Contains(ComparisonState.Incompatible)
            ? ComparisonState.Incompatible
            : components.Contains(ComparisonState.Unavailable)
                ? ComparisonState.Unavailable
                : components.Contains(ComparisonState.Partial)
                    ? ComparisonState.Partial
                    : ComparisonState.Comparable;
        var reason = new[] { hits.Reason, misses.Reason, attempts.Reason }
            .FirstOrDefault(value => value != ComparisonReason.None);
        var pp = Metric<long>.NotCaptured();
        var ppReason = ComparisonReason.NotCaptured;
        if (state is ComparisonState.Comparable or ComparisonState.Partial
            && left.Value is { Attempts: > 0 } leftSnap
            && right.Value is { Attempts: > 0 } rightSnap)
        {
            var leftRate = (Int128)leftSnap.Hits * 10000 / leftSnap.Attempts;
            var rightRate = (Int128)rightSnap.Hits * 10000 / rightSnap.Attempts;
            var delta = rightRate - leftRate;
            if (delta > (Int128)long.MaxValue || delta < (Int128)long.MinValue)
            {
                ppReason = ComparisonReason.ArithmeticOverflow;
            }
            else
            {
                pp = state == ComparisonState.Partial
                    ? Metric<long>.Incomplete(
                        (long)delta,
                        coverage: Merge(left.Coverage, right.Coverage) ?? new CoverageInfo { LowerBound = true })
                    : Metric<long>.Available((long)delta, MetricEvidence.DerivedFromObserved);
                ppReason = ComparisonReason.None;
            }
        }
        else if (state is ComparisonState.Comparable or ComparisonState.Partial)
        {
            ppReason = ComparisonReason.ZeroBaseline;
        }

        return new AccuracyMetricComparison
        {
            State = state,
            Reason = reason,
            Hits = hits,
            Misses = misses,
            Attempts = attempts,
            HitRatePercentagePointDeltaHundredths = pp,
            HitRatePercentagePointReason = ppReason
        };
    }

    private static IReadOnlyList<PowerMetricComparison> ComparePowers(
        IReadOnlyList<CombatPowerAnalysisRow> left,
        IReadOnlyList<CombatPowerAnalysisRow> right)
    {
        var leftMap = GroupMap(left, PowerKey);
        var rightMap = GroupMap(right, PowerKey);
        return leftMap.Keys.Union(rightMap.Keys)
            .OrderBy(key => key.Direction)
            .ThenBy(key => key.Scope)
            .ThenBy(key => key.PetNormalizedName, StringComparer.Ordinal)
            .ThenBy(key => key.PowerName, StringComparer.Ordinal)
            .ThenBy(key => key.IsOverflow)
            .Select(key =>
            {
                leftMap.TryGetValue(key, out var leftRows);
                rightMap.TryGetValue(key, out var rightRows);
                leftRows ??= [];
                rightRows ??= [];
                if (leftRows.Count > 1 || rightRows.Count > 1)
                {
                    return AmbiguousPower(key, leftRows, rightRows);
                }

                var leftRow = leftRows.SingleOrDefault();
                var rightRow = rightRows.SingleOrDefault();
                var presence = Presence(leftRow is not null, rightRow is not null);
                if (presence != ComparisonPresence.Matched)
                {
                    return UnmatchedPower(key, presence, leftRow, rightRow);
                }

                var matchedLeft = leftRow!;
                var matchedRight = rightRow!;
                var limited = matchedLeft.CoverageLimited || matchedRight.CoverageLimited;
                return new PowerMetricComparison
                {
                    Key = key,
                    Presence = ComparisonPresence.Matched,
                    Left = matchedLeft,
                    Right = matchedRight,
                    LeftCandidates = leftRows,
                    RightCandidates = rightRows,
                    DamageMagnitude = MaybePartial(CompareAmount(matchedLeft.DamageMagnitudeMetric, matchedRight.DamageMagnitudeMetric), limited),
                    HealingMagnitude = MaybePartial(CompareAmount(matchedLeft.HealingMagnitudeMetric, matchedRight.HealingMagnitudeMetric), limited),
                    EnduranceMagnitude = MaybePartial(CompareAmount(matchedLeft.EnduranceMagnitudeMetric, matchedRight.EnduranceMagnitudeMetric), limited),
                    EventCount = MaybePartial(CompareLong(ObservedLong(matchedLeft.EventCount), ObservedLong(matchedRight.EventCount)), limited),
                    ActivationCount = MaybePartial(CompareLong(ObservedLong(matchedLeft.ActivationCount), ObservedLong(matchedRight.ActivationCount)), limited),
                    DirectAmount = MaybePartial(
                        CompareAmount(DeliveryAmount(matchedLeft, matchedLeft.DirectAmount), DeliveryAmount(matchedRight, matchedRight.DirectAmount)),
                        limited),
                    DotAmount = MaybePartial(
                        CompareAmount(DeliveryAmount(matchedLeft, matchedLeft.DotAmount), DeliveryAmount(matchedRight, matchedRight.DotAmount)),
                        limited),
                    LargestHit = MaybePartial(CompareAmount(OptionalAmount(matchedLeft.LargestHit), OptionalAmount(matchedRight.LargestHit)), limited),
                    DistinctTargetCount = MaybePartial(
                        CompareLong(matchedLeft.DistinctTargetCountMetric, matchedRight.DistinctTargetCountMetric),
                        limited),
                    DamageTypes = CompareDamageTypes(
                        matchedLeft.DamageTypes,
                        matchedRight.DamageTypes,
                        matchedLeft.DamageTypeBreakdown,
                        matchedRight.DamageTypeBreakdown),
                    CoverageLimited = limited
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<ActorMetricComparison> CompareActors(
        IReadOnlyList<CombatActorSummary> left,
        IReadOnlyList<CombatActorSummary> right)
    {
        var leftMap = DistinctMap(left, ActorKey);
        var rightMap = DistinctMap(right, ActorKey);
        return leftMap.Keys.Union(rightMap.Keys)
            .OrderBy(key => key.Scope)
            .ThenBy(key => key.PetNormalizedName, StringComparer.Ordinal)
            .Select(key =>
            {
                leftMap.TryGetValue(key, out var leftRow);
                rightMap.TryGetValue(key, out var rightRow);
                var presence = Presence(leftRow is not null, rightRow is not null);
                var leftObserved = leftRow is not null && ActorObserved(leftRow);
                var rightObserved = rightRow is not null && ActorObserved(rightRow);
                var limited = leftRow?.CoverageLimited == true || rightRow?.CoverageLimited == true;
                if (presence != ComparisonPresence.Matched || !leftObserved || !rightObserved)
                {
                    var reason = presence == ComparisonPresence.LeftOnly
                        ? ComparisonReason.LeftOnly
                        : presence == ComparisonPresence.RightOnly
                            ? ComparisonReason.RightOnly
                            : ComparisonReason.NotCaptured;
                    return new ActorMetricComparison
                    {
                        Scope = key.Scope,
                        PetNormalizedName = key.PetNormalizedName,
                        IsOverflow = key.IsOverflow,
                        Presence = presence,
                        Left = leftRow,
                        Right = rightRow,
                        DamageDealt = MetricComparison<CombatScaledAmount>.Unavailable(
                            OptionalAmount(leftObserved ? leftRow!.DamageDealt : null),
                            OptionalAmount(rightObserved ? rightRow!.DamageDealt : null),
                            reason),
                        DamageReceived = UnavailableAmount(reason),
                        HealingDealt = UnavailableAmount(reason),
                        HealingReceived = UnavailableAmount(reason),
                        EnduranceGranted = UnavailableAmount(reason),
                        EnduranceReceived = UnavailableAmount(reason),
                        ActivationCount = UnavailableLong(reason),
                        Accuracy = new AccuracyMetricComparison
                        {
                            State = ComparisonState.Unavailable,
                            Reason = reason,
                            Hits = UnavailableLong(reason),
                            Misses = UnavailableLong(reason),
                            Attempts = UnavailableLong(reason),
                            HitRatePercentagePointReason = reason,
                            HitRatePercentagePointDeltaHundredths = Metric<long>.NotCaptured()
                        },
                        PetInstanceCount = CompareLong(
                            leftRow?.PetInstanceCount ?? Metric<long>.NotCaptured(),
                            rightRow?.PetInstanceCount ?? Metric<long>.NotCaptured()),
                        CoverageLimited = limited
                    };
                }

                return new ActorMetricComparison
                {
                    Scope = key.Scope,
                    PetNormalizedName = key.PetNormalizedName,
                    IsOverflow = key.IsOverflow,
                    Presence = ComparisonPresence.Matched,
                    Left = leftRow,
                    Right = rightRow,
                    DamageDealt = MaybePartial(CompareAmount(ObservedAmount(leftRow!.DamageDealt), ObservedAmount(rightRow!.DamageDealt)), limited),
                    DamageReceived = MaybePartial(CompareAmount(ObservedAmount(leftRow.DamageReceived), ObservedAmount(rightRow.DamageReceived)), limited),
                    HealingDealt = MaybePartial(CompareAmount(ObservedAmount(leftRow.HealingDealt), ObservedAmount(rightRow.HealingDealt)), limited),
                    HealingReceived = MaybePartial(CompareAmount(ObservedAmount(leftRow.HealingReceived), ObservedAmount(rightRow.HealingReceived)), limited),
                    EnduranceGranted = MaybePartial(CompareAmount(ObservedAmount(leftRow.EnduranceGranted), ObservedAmount(rightRow.EnduranceGranted)), limited),
                    EnduranceReceived = MaybePartial(CompareAmount(ObservedAmount(leftRow.EnduranceReceived), ObservedAmount(rightRow.EnduranceReceived)), limited),
                    ActivationCount = MaybePartial(CompareLong(ObservedLong(leftRow.ActivationCount), ObservedLong(rightRow.ActivationCount)), limited),
                    PetInstanceCount = CompareLong(leftRow.PetInstanceCount, rightRow.PetInstanceCount),
                    Accuracy = CompareAccuracy(
                        leftRow.Accuracy.HasAttempts
                            ? MetricRef<CombatAccuracyScopeSnapshot>.Available(leftRow.Accuracy)
                            : MetricRef<CombatAccuracyScopeSnapshot>.NotCaptured(),
                        rightRow.Accuracy.HasAttempts
                            ? MetricRef<CombatAccuracyScopeSnapshot>.Available(rightRow.Accuracy)
                            : MetricRef<CombatAccuracyScopeSnapshot>.NotCaptured()),
                    CoverageLimited = limited
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<DamageTypeMetricComparison> CompareDamageTypes(
        IReadOnlyList<CombatDamageTypeTotal> left,
        IReadOnlyList<CombatDamageTypeTotal> right,
        MetricRef<IReadOnlyList<CombatDamageTypeTotal>> leftCoverage,
        MetricRef<IReadOnlyList<CombatDamageTypeTotal>> rightCoverage)
    {
        if (leftCoverage.Availability is MetricAvailability.NotCaptured
            && rightCoverage.Availability is MetricAvailability.NotCaptured
            && left.Count == 0 && right.Count == 0)
        {
            return [];
        }

        var complete = leftCoverage.Availability == MetricAvailability.Available
            && rightCoverage.Availability == MetricAvailability.Available;
        var leftMap = DistinctMap(left, static row => (row.DamageType, row.IsOverflow));
        var rightMap = DistinctMap(right, static row => (row.DamageType, row.IsOverflow));
        return leftMap.Keys.Union(rightMap.Keys)
            .OrderBy(key => key.DamageType.Text, StringComparer.Ordinal)
            .ThenBy(key => key.DamageType.IsUnresistable)
            .ThenBy(key => key.DamageType.IsUnique)
            .ThenBy(key => key.IsOverflow)
            .Select(key =>
            {
                leftMap.TryGetValue(key, out var leftRow);
                rightMap.TryGetValue(key, out var rightRow);
                var presence = Presence(leftRow is not null, rightRow is not null);
                if (key.IsOverflow)
                {
                    return new DamageTypeMetricComparison
                    {
                        DamageType = key.DamageType,
                        IsOverflow = true,
                        Presence = presence,
                        Amount = UnavailableAmount(ComparisonReason.Overflow),
                        EventCount = UnavailableLong(ComparisonReason.Overflow)
                    };
                }

                if (!complete && presence != ComparisonPresence.Matched)
                {
                    return new DamageTypeMetricComparison
                    {
                        DamageType = key.DamageType,
                        Presence = presence,
                        Amount = MetricComparison<CombatScaledAmount>.Unavailable(
                            OptionalAmount(leftRow?.Amount),
                            OptionalAmount(rightRow?.Amount),
                            ComparisonReason.MissingDamageType),
                        EventCount = MetricComparison<long>.Unavailable(
                            leftRow is null ? Metric<long>.NotCaptured() : ObservedLong(leftRow.EventCount),
                            rightRow is null ? Metric<long>.NotCaptured() : ObservedLong(rightRow.EventCount),
                            ComparisonReason.MissingDamageType)
                    };
                }

                var leftAmount = leftRow is null ? ObservedAmount(CombatScaledAmount.Zero) : ObservedAmount(leftRow.Amount);
                var rightAmount = rightRow is null ? ObservedAmount(CombatScaledAmount.Zero) : ObservedAmount(rightRow.Amount);
                var leftCount = leftRow is null ? ObservedLong(0) : ObservedLong(leftRow.EventCount);
                var rightCount = rightRow is null ? ObservedLong(0) : ObservedLong(rightRow.EventCount);
                return new DamageTypeMetricComparison
                {
                    DamageType = key.DamageType,
                    Presence = presence,
                    Amount = CompareAmount(leftAmount, rightAmount),
                    EventCount = CompareLong(leftCount, rightCount)
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<TargetMetricComparison> CompareTargets(
        IReadOnlyList<CombatTargetSummary> left,
        IReadOnlyList<CombatTargetSummary> right,
        bool leftComplete,
        bool rightComplete)
    {
        var complete = leftComplete && rightComplete;
        var leftMap = DistinctMap(left, static row => (Name: row.NormalizedTargetName.ToUpperInvariant(), row.IsOverflow));
        var rightMap = DistinctMap(right, static row => (Name: row.NormalizedTargetName.ToUpperInvariant(), row.IsOverflow));
        return leftMap.Keys.Union(rightMap.Keys)
            .OrderBy(key => key.Name, StringComparer.Ordinal)
            .ThenBy(key => key.IsOverflow)
            .Select(key =>
            {
                leftMap.TryGetValue(key, out var leftRow);
                rightMap.TryGetValue(key, out var rightRow);
                var presence = Presence(leftRow is not null, rightRow is not null);
                var overflow = leftRow?.IsOverflow == true || rightRow?.IsOverflow == true;
                if (overflow)
                {
                    return new TargetMetricComparison
                    {
                        NormalizedTargetName = key.Name,
                        Presence = presence,
                        Left = leftRow,
                        Right = rightRow,
                        DamageDealt = UnavailableAmount(ComparisonReason.Overflow),
                        EventCount = UnavailableLong(ComparisonReason.Overflow),
                        Overflow = true
                    };
                }

                if (!complete && presence != ComparisonPresence.Matched)
                {
                    return new TargetMetricComparison
                    {
                        NormalizedTargetName = key.Name,
                        Presence = presence,
                        Left = leftRow,
                        Right = rightRow,
                        DamageDealt = MetricComparison<CombatScaledAmount>.Unavailable(
                            OptionalAmount(leftRow?.DamageDealt),
                            OptionalAmount(rightRow?.DamageDealt),
                            ComparisonReason.TargetLowerBound),
                        EventCount = MetricComparison<long>.Unavailable(
                            leftRow is null ? Metric<long>.NotCaptured() : ObservedLong(leftRow.EventCount),
                            rightRow is null ? Metric<long>.NotCaptured() : ObservedLong(rightRow.EventCount),
                            ComparisonReason.TargetLowerBound),
                        Overflow = overflow
                    };
                }

                var leftAmount = leftRow is null ? ObservedAmount(CombatScaledAmount.Zero) : ObservedAmount(leftRow.DamageDealt);
                var rightAmount = rightRow is null ? ObservedAmount(CombatScaledAmount.Zero) : ObservedAmount(rightRow.DamageDealt);
                return new TargetMetricComparison
                {
                    NormalizedTargetName = key.Name,
                    Presence = presence,
                    Left = leftRow,
                    Right = rightRow,
                    DamageDealt = CompareAmount(leftAmount, rightAmount),
                    EventCount = CompareLong(
                        leftRow is null ? ObservedLong(0) : ObservedLong(leftRow.EventCount),
                        rightRow is null ? ObservedLong(0) : ObservedLong(rightRow.EventCount)),
                    Overflow = overflow
                };
            })
            .ToArray();
    }

    private static BuildContextComparison CompareBuild(
        ComparisonSourceContext leftContext,
        ComparisonSourceContext rightContext,
        CombatBuildContextSummary left,
        CombatBuildContextSummary right) =>
        new()
        {
            LeftAvailability = left.Availability,
            RightAvailability = right.Availability,
            SameManifestHash = string.IsNullOrEmpty(leftContext.BuildManifestHash)
                || string.IsNullOrEmpty(rightContext.BuildManifestHash)
                ? null
                : string.Equals(leftContext.BuildManifestHash, rightContext.BuildManifestHash, StringComparison.Ordinal),
            SameCatalogFingerprint = string.IsNullOrEmpty(leftContext.BuildCatalogFingerprint)
                || string.IsNullOrEmpty(rightContext.BuildCatalogFingerprint)
                ? null
                : string.Equals(
                    leftContext.BuildCatalogFingerprint,
                    rightContext.BuildCatalogFingerprint,
                    StringComparison.Ordinal)
        };

    private static AttributionMetricComparison CompareAttribution(
        CombatProcAttributionSummary left,
        CombatProcAttributionSummary right)
    {
        if (left.AttributionPolicyVersion != right.AttributionPolicyVersion)
        {
            return BlockedAttribution(left, right, ComparisonReason.AttributionPolicyMismatch);
        }

        var proc = CompareAmount(left.ProcDamage, right.ProcDamage);
        var captured = AttributionCaptured(left) && AttributionCaptured(right);
        var counts = captured
            ? ComparisonState.Comparable
            : AttributionCaptured(left) || AttributionCaptured(right)
                ? ComparisonState.Unavailable
                : ComparisonState.Unavailable;
        var amountState = captured ? ComparisonState.Comparable : counts;
        return new AttributionMetricComparison
        {
            State = proc.State == ComparisonState.Incompatible
                ? proc.State
                : proc.State == ComparisonState.Partial || amountState == ComparisonState.Partial
                    ? ComparisonState.Partial
                    : proc.State == ComparisonState.Unavailable || !captured
                        ? ComparisonState.Unavailable
                        : ComparisonState.Comparable,
            Reason = captured ? proc.Reason : ComparisonReason.NotCaptured,
            LeftPolicyVersion = left.AttributionPolicyVersion,
            RightPolicyVersion = right.AttributionPolicyVersion,
            ProcDamage = proc,
            DirectDamage = captured
                ? CompareAmount(ObservedAmount(left.DirectDamage), ObservedAmount(right.DirectDamage))
                : MetricComparison<CombatScaledAmount>.Unavailable(
                    OptionalAmount(AttributionCaptured(left) ? left.DirectDamage : null),
                    OptionalAmount(AttributionCaptured(right) ? right.DirectDamage : null),
                    ComparisonReason.NotCaptured),
            BuildConfirmedProcDamage = captured
                ? CompareAmount(ObservedAmount(left.BuildConfirmedProcDamage), ObservedAmount(right.BuildConfirmedProcDamage))
                : MetricComparison<CombatScaledAmount>.Unavailable(
                    OptionalAmount(AttributionCaptured(left) ? left.BuildConfirmedProcDamage : null),
                    OptionalAmount(AttributionCaptured(right) ? right.BuildConfirmedProcDamage : null),
                    ComparisonReason.NotCaptured),
            UnattributedDamage = captured
                ? CompareAmount(ObservedAmount(left.UnattributedDamage), ObservedAmount(right.UnattributedDamage))
                : MetricComparison<CombatScaledAmount>.Unavailable(
                    OptionalAmount(AttributionCaptured(left) ? left.UnattributedDamage : null),
                    OptionalAmount(AttributionCaptured(right) ? right.UnattributedDamage : null),
                    ComparisonReason.NotCaptured),
            UnattributedProcDamage = captured
                ? CompareAmount(ObservedAmount(left.UnattributedProcDamage), ObservedAmount(right.UnattributedProcDamage))
                : MetricComparison<CombatScaledAmount>.Unavailable(
                    OptionalAmount(AttributionCaptured(left) ? left.UnattributedProcDamage : null),
                    OptionalAmount(AttributionCaptured(right) ? right.UnattributedProcDamage : null),
                    ComparisonReason.NotCaptured),
            DirectCount = captured
                ? CompareLong(ObservedLong(left.DirectCount), ObservedLong(right.DirectCount))
                : MetricComparison<long>.Unavailable(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), ComparisonReason.NotCaptured),
            BuildConfirmedCount = captured
                ? CompareLong(ObservedLong(left.BuildConfirmedCount), ObservedLong(right.BuildConfirmedCount))
                : MetricComparison<long>.Unavailable(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), ComparisonReason.NotCaptured),
            CorrelatedCount = MetricComparison<long>.Incompatible(
                Metric<long>.Unsupported(),
                Metric<long>.Unsupported(),
                ComparisonReason.Unsupported),
            UnattributedCount = captured
                ? CompareLong(ObservedLong(left.UnattributedCount), ObservedLong(right.UnattributedCount))
                : MetricComparison<long>.Unavailable(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), ComparisonReason.NotCaptured)
        };
    }

    private static AnalyticalComparisonCompatibility Overall(
        ComparisonSourceContext left,
        ComparisonSourceContext right,
        SessionMetricComparison session,
        ClockMetricComparison clock,
        IReadOnlyList<PowerMetricComparison> powers,
        IReadOnlyList<ActorMetricComparison> actors,
        IReadOnlyList<DamageTypeMetricComparison> outgoingTypes,
        IReadOnlyList<DamageTypeMetricComparison> incomingTypes,
        IReadOnlyList<TargetMetricComparison> targets,
        BuildContextComparison build,
        AttributionMetricComparison attribution)
    {
        var anyComparable = false;
        var anyGap = left.CoverageLimited
            || right.CoverageLimited
            || build.LeftAvailability != MetricAvailability.Available
            || build.RightAvailability != MetricAvailability.Available
            || build.SameManifestHash is null
            || build.SameCatalogFingerprint is null;
        var anyPartial = false;
        var anyIncompatible = false;

        void Metric<T>(MetricComparison<T> value) where T : struct =>
            ConsiderMetric(value, ref anyComparable, ref anyPartial, ref anyGap, ref anyIncompatible);

        Metric(session.DamageDealt);
        Metric(session.DamageDealtSelf);
        Metric(session.DamageDealtOwnedPets);
        Metric(session.DamageReceived);
        Metric(session.DamageReceivedOwnedPets);
        Metric(session.HealingDealt);
        Metric(session.HealingReceived);
        Metric(session.EnduranceGranted);
        Metric(session.EnduranceReceived);
        Metric(session.DamageEventCount);
        Metric(session.ActivationCount);
        Metric(session.AttackResolutionCount);
        Metric(session.DefeatCount);
        Metric(session.MyDefeatCount);
        Metric(session.ConfirmedRechargeCompletedCount);
        Metric(session.ConfirmedStillRechargingCount);
        Metric(session.UnmatchedRechargeCandidateCount);
        Metric(session.DistinctTargetCount);
        Metric(session.TheoreticalRecharge);
        Metric(session.PermaHasten);
        Metric(session.Overkill);
        Metric(session.MezDuration);
        Metric(session.PetInstanceCount);
        Metric(session.Accuracy.Hits);
        Metric(session.Accuracy.Misses);
        Metric(session.Accuracy.Attempts);
        Metric(clock.WallClockDuration);
        Metric(clock.ObservedAnalyticalSpan);
        Metric(clock.TrackedPauseAdjustedDuration);
        Metric(clock.ActiveDuration);
        Metric(clock.WallClockDamagePerSecondHundredths);
        Metric(clock.ActiveDamagePerSecondHundredths);

        foreach (var power in powers)
        {
            Metric(power.DamageMagnitude);
            Metric(power.HealingMagnitude);
            Metric(power.EnduranceMagnitude);
            Metric(power.EventCount);
            Metric(power.ActivationCount);
            Metric(power.DirectAmount);
            Metric(power.DotAmount);
            Metric(power.LargestHit);
            Metric(power.DistinctTargetCount);
            anyGap |= power.Presence == ComparisonPresence.Ambiguous;
        }

        foreach (var actor in actors)
        {
            Metric(actor.DamageDealt);
            Metric(actor.DamageReceived);
            Metric(actor.HealingDealt);
            Metric(actor.HealingReceived);
            Metric(actor.EnduranceGranted);
            Metric(actor.EnduranceReceived);
            Metric(actor.ActivationCount);
            Metric(actor.PetInstanceCount);
            Metric(actor.Accuracy.Hits);
        }

        foreach (var type in outgoingTypes.Concat(incomingTypes))
        {
            Metric(type.Amount);
            Metric(type.EventCount);
        }

        foreach (var target in targets)
        {
            Metric(target.DamageDealt);
            Metric(target.EventCount);
        }

        ConsiderAttribution(
            attribution,
            ref anyComparable,
            ref anyPartial,
            ref anyGap,
            ref anyIncompatible);

        if (!anyComparable && !anyPartial)
        {
            return anyIncompatible
                ? AnalyticalComparisonCompatibility.IncompatibleMetricSemantics
                : AnalyticalComparisonCompatibility.InsufficientData;
        }

        return anyPartial || anyGap || anyIncompatible
            ? AnalyticalComparisonCompatibility.PartiallyComparable
            : AnalyticalComparisonCompatibility.Comparable;
    }

    private static void ConsiderMetric<T>(
        MetricComparison<T> comparison,
        ref bool anyComparable,
        ref bool anyPartial,
        ref bool anyGap,
        ref bool anyIncompatible) where T : struct
    {
        if (comparison.Left.Availability == MetricAvailability.Unsupported
            && comparison.Right.Availability == MetricAvailability.Unsupported)
        {
            return;
        }

        if (comparison.Left.Availability == MetricAvailability.NotCaptured
            && comparison.Right.Availability == MetricAvailability.NotCaptured
            && comparison.Reason is ComparisonReason.NotCaptured or ComparisonReason.None)
        {
            return;
        }

        switch (comparison.State)
        {
            case ComparisonState.Comparable:
                anyComparable = true;
                break;
            case ComparisonState.Partial:
                anyPartial = true;
                anyComparable = true;
                break;
            case ComparisonState.Unavailable:
                anyGap = true;
                break;
            case ComparisonState.Incompatible:
                anyGap = true;
                anyIncompatible = true;
                break;
        }
    }

    private static void ConsiderAttribution(
        AttributionMetricComparison attribution,
        ref bool anyComparable,
        ref bool anyPartial,
        ref bool anyGap,
        ref bool anyIncompatible)
    {
        if (attribution.State is ComparisonState.Comparable)
        {
            anyComparable = true;
        }
        else if (attribution.State is ComparisonState.Partial)
        {
            anyPartial = true;
            anyComparable = true;
        }
        else if (attribution.State is ComparisonState.Incompatible)
        {
            anyGap = true;
            anyIncompatible = true;
        }
        else if (attribution.State is ComparisonState.Unavailable
                 && attribution.Reason is not ComparisonReason.NotCaptured)
        {
            anyGap = true;
        }
    }

    private static MetricComparison<CombatScaledAmount> CompareAmount(
        Metric<CombatScaledAmount> left,
        Metric<CombatScaledAmount> right) =>
        Compare(
            left,
            right,
            static (leftValue, rightValue) => new CombatScaledAmount(checked(rightValue.Hundredths - leftValue.Hundredths)),
            static value => value.Hundredths);

    private static MetricComparison<long> CompareLong(Metric<long> left, Metric<long> right) =>
        Compare(left, right, static (leftValue, rightValue) => checked(rightValue - leftValue), static value => value);

    private static MetricComparison<TimeSpan> CompareTime(Metric<TimeSpan> left, Metric<TimeSpan> right) =>
        Compare(
            left,
            right,
            static (leftValue, rightValue) => rightValue - leftValue,
            static value => value.Ticks);

    private static MetricComparison<T> Compare<T>(
        Metric<T> left,
        Metric<T> right,
        Func<T, T, T> subtract,
        Func<T, long> scalar) where T : struct
    {
        if (left.Availability == MetricAvailability.Unsupported
            || right.Availability == MetricAvailability.Unsupported)
        {
            return MetricComparison<T>.Incompatible(left, right, ComparisonReason.Unsupported);
        }

        if (left.Availability == MetricAvailability.NotCaptured
            || right.Availability == MetricAvailability.NotCaptured)
        {
            return MetricComparison<T>.Unavailable(left, right, ComparisonReason.NotCaptured);
        }

        if (left.Denominator != right.Denominator)
        {
            return MetricComparison<T>.Incompatible(left, right, ComparisonReason.DenominatorMismatch);
        }

        var leftValue = left.Value!.Value;
        var rightValue = right.Value!.Value;
        T delta;
        try
        {
            delta = subtract(leftValue, rightValue);
        }
        catch (OverflowException)
        {
            return MetricComparison<T>.Unavailable(left, right, ComparisonReason.ArithmeticOverflow);
        }

        var percent = TryPercentHundredths(
            scalar(leftValue),
            scalar(rightValue),
            out var percentReason);
        var partial = left.Availability == MetricAvailability.Incomplete
            || right.Availability == MetricAvailability.Incomplete;
        var coverage = Merge(left.Coverage, right.Coverage);
        if (partial && coverage is not { IsPartial: true })
        {
            coverage = new CoverageInfo { LowerBound = true };
        }

        var absolute = partial
            ? Metric<T>.Incomplete(delta, coverage: coverage)
            : Metric<T>.Available(delta, MetricEvidence.DerivedFromObserved);
        var relative = percent is null
            ? Metric<long>.NotCaptured()
            : partial
                ? Metric<long>.Incomplete(percent.Value, coverage: coverage)
                : Metric<long>.Available(percent.Value, MetricEvidence.DerivedFromObserved);

        return new MetricComparison<T>
        {
            Left = left,
            Right = right,
            State = partial ? ComparisonState.Partial : ComparisonState.Comparable,
            Reason = partial ? ComparisonReason.Incomplete : ComparisonReason.None,
            AbsoluteDelta = absolute,
            PercentDeltaHundredths = relative,
            PercentDeltaReason = percentReason,
            PercentagePointDeltaHundredths = Metric<long>.NotCaptured()
        };
    }

    private static MetricComparison<T> MaybePartial<T>(MetricComparison<T> comparison, bool coverageLimited)
        where T : struct
    {
        if (!coverageLimited || comparison.State is not ComparisonState.Comparable)
        {
            return comparison;
        }

        var coverage = new CoverageInfo { LowerBound = true };
        return comparison with
        {
            State = ComparisonState.Partial,
            Reason = ComparisonReason.CoverageLimited,
            AbsoluteDelta = comparison.AbsoluteDelta.Value is { } delta
                ? Metric<T>.Incomplete(delta, coverage: coverage)
                : comparison.AbsoluteDelta,
            PercentDeltaHundredths = comparison.PercentDeltaHundredths.Value is { } percent
                ? Metric<long>.Incomplete(percent, coverage: coverage)
                : comparison.PercentDeltaHundredths
        };
    }

    private static long? TryPercentHundredths(
        long left,
        long right,
        out ComparisonReason reason)
    {
        if (left == 0)
        {
            reason = ComparisonReason.ZeroBaseline;
            return null;
        }

        var denominator = left < 0 ? -(Int128)left : (Int128)left;
        // Int128 division is deterministic and truncates toward zero.
        var hundredths = ((Int128)right - left) * 10000 / denominator;
        if (hundredths > long.MaxValue || hundredths < long.MinValue)
        {
            reason = ComparisonReason.ArithmeticOverflow;
            return null;
        }

        reason = ComparisonReason.None;
        return (long)hundredths;
    }

    private static Metric<long> AccuracyCount(
        MetricRef<CombatAccuracyScopeSnapshot> snapshot,
        Func<CombatAccuracyScopeSnapshot, long> selector)
    {
        return snapshot.Availability switch
        {
            MetricAvailability.Available => Metric<long>.Available(
                selector(snapshot.Value!),
                snapshot.Evidence,
                snapshot.Confidence,
                coverage: snapshot.Coverage),
            MetricAvailability.Incomplete => Metric<long>.Incomplete(
                selector(snapshot.Value!),
                coverage: snapshot.Coverage ?? new CoverageInfo { LowerBound = true },
                confidence: snapshot.Confidence),
            MetricAvailability.Unsupported => Metric<long>.Unsupported(),
            _ => Metric<long>.NotCaptured()
        };
    }

    private static Metric<long> DefeatMetric(
        CombatSessionSummary session,
        AnalyticalProjectionSourceKind sourceKind) =>
        sourceKind == AnalyticalProjectionSourceKind.HistoricalLegacy || session.DefeatCount > 0
            ? Metric<long>.Available(session.DefeatCount)
            : Metric<long>.NotCaptured();

    private static Metric<long> MyDefeatMetric(
        CombatSessionSummary session,
        AnalyticalProjectionSourceKind sourceKind) =>
        sourceKind == AnalyticalProjectionSourceKind.HistoricalLegacy || session.MyDefeatCount > 0
            ? Metric<long>.Available(session.MyDefeatCount)
            : Metric<long>.NotCaptured();

    private static bool TargetCoverageComplete(CombatAnalyticsProjection projection) =>
        projection.Session.Metrics.DistinctTargetCount.Availability == MetricAvailability.Available;

    private static bool ActorObserved(CombatActorSummary actor) =>
        actor.CoverageLimited
        || actor.DamageDealt != CombatScaledAmount.Zero
        || actor.DamageReceived != CombatScaledAmount.Zero
        || actor.HealingDealt != CombatScaledAmount.Zero
        || actor.HealingReceived != CombatScaledAmount.Zero
        || actor.EnduranceGranted != CombatScaledAmount.Zero
        || actor.EnduranceReceived != CombatScaledAmount.Zero
        || actor.ActivationCount > 0
        || actor.Accuracy.HasAttempts;

    private static bool AttributionCaptured(CombatProcAttributionSummary summary) =>
        summary.ProcDamage.Availability is MetricAvailability.Available or MetricAvailability.Incomplete
        || summary.DirectCount > 0
        || summary.BuildConfirmedCount > 0
        || summary.UnattributedCount > 0
        || summary.DirectDamage != CombatScaledAmount.Zero
        || summary.UnattributedDamage != CombatScaledAmount.Zero;

    private static Metric<CombatScaledAmount> ObservedAmount(CombatScaledAmount amount) =>
        Metric<CombatScaledAmount>.Available(amount);

    private static Metric<CombatScaledAmount> OptionalAmount(CombatScaledAmount? amount) =>
        amount is { } value ? Metric<CombatScaledAmount>.Available(value) : Metric<CombatScaledAmount>.NotCaptured();

    private static Metric<long> ObservedLong(long value) => Metric<long>.Available(value);

    private static MetricComparison<CombatScaledAmount> UnavailableAmount(ComparisonReason reason) =>
        MetricComparison<CombatScaledAmount>.Unavailable(
            Metric<CombatScaledAmount>.NotCaptured(),
            Metric<CombatScaledAmount>.NotCaptured(),
            reason);

    private static MetricComparison<long> UnavailableLong(ComparisonReason reason) =>
        MetricComparison<long>.Unavailable(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), reason);

    private static Metric<CombatScaledAmount> DeliveryAmount(CombatPowerAnalysisRow row, CombatScaledAmount amount) =>
        row.DamageMagnitudeMetric.Availability is MetricAvailability.Available or MetricAvailability.Incomplete
            ? row.DamageMagnitudeMetric.Availability == MetricAvailability.Incomplete
                ? Metric<CombatScaledAmount>.Incomplete(
                    amount,
                    coverage: row.DamageMagnitudeMetric.Coverage ?? new CoverageInfo { LowerBound = true })
                : Metric<CombatScaledAmount>.Available(amount)
            : Metric<CombatScaledAmount>.NotCaptured();

    private static ComparisonPowerKey PowerKey(CombatPowerAnalysisRow row) =>
        new()
        {
            Direction = row.Direction,
            Scope = row.Scope,
            PowerName = row.PowerName,
            PetNormalizedName = row.PetNormalizedName?.ToUpperInvariant() ?? "",
            IsOverflow = row.IsOverflow
        };

    private static (CombatAnalyticsScope Scope, string PetNormalizedName, bool IsOverflow) ActorKey(CombatActorSummary row) =>
        (row.Scope, row.PetNormalizedName?.ToUpperInvariant() ?? "", row.IsOverflow);

    private static ComparisonPresence Presence(bool hasLeft, bool hasRight) =>
        hasLeft && hasRight ? ComparisonPresence.Matched
        : hasLeft ? ComparisonPresence.LeftOnly
        : ComparisonPresence.RightOnly;

    private static PowerMetricComparison UnmatchedPower(
        ComparisonPowerKey key,
        ComparisonPresence presence,
        CombatPowerAnalysisRow? left,
        CombatPowerAnalysisRow? right)
    {
        var reason = presence == ComparisonPresence.LeftOnly ? ComparisonReason.LeftOnly : ComparisonReason.RightOnly;
        return new PowerMetricComparison
        {
            Key = key,
            Presence = presence,
            Left = left,
            Right = right,
            LeftCandidates = left is null ? [] : [left],
            RightCandidates = right is null ? [] : [right],
            DamageMagnitude = MetricComparison<CombatScaledAmount>.Unavailable(
                left?.DamageMagnitudeMetric ?? Metric<CombatScaledAmount>.NotCaptured(),
                right?.DamageMagnitudeMetric ?? Metric<CombatScaledAmount>.NotCaptured(),
                reason),
            HealingMagnitude = UnavailableAmount(reason),
            EnduranceMagnitude = UnavailableAmount(reason),
            EventCount = UnavailableLong(reason),
            ActivationCount = UnavailableLong(reason),
            DirectAmount = UnavailableAmount(reason),
            DotAmount = UnavailableAmount(reason),
            LargestHit = UnavailableAmount(reason),
            DistinctTargetCount = UnavailableLong(reason),
            CoverageLimited = left?.CoverageLimited == true || right?.CoverageLimited == true
        };
    }

    private static PowerMetricComparison AmbiguousPower(
        ComparisonPowerKey key,
        IReadOnlyList<CombatPowerAnalysisRow> left,
        IReadOnlyList<CombatPowerAnalysisRow> right)
    {
        const ComparisonReason reason = ComparisonReason.DuplicateKey;
        return new PowerMetricComparison
        {
            Key = key,
            Presence = ComparisonPresence.Ambiguous,
            LeftCandidates = left,
            RightCandidates = right,
            DamageMagnitude = UnavailableAmount(reason),
            HealingMagnitude = UnavailableAmount(reason),
            EnduranceMagnitude = UnavailableAmount(reason),
            EventCount = UnavailableLong(reason),
            ActivationCount = UnavailableLong(reason),
            DirectAmount = UnavailableAmount(reason),
            DotAmount = UnavailableAmount(reason),
            LargestHit = UnavailableAmount(reason),
            DistinctTargetCount = UnavailableLong(reason),
            CoverageLimited = true
        };
    }

    private static Dictionary<TKey, IReadOnlyList<TValue>> GroupMap<TKey, TValue>(
        IEnumerable<TValue> rows,
        Func<TValue, TKey> keySelector) where TKey : notnull =>
        rows.GroupBy(keySelector).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<TValue>)group.ToArray());

    private static Dictionary<TKey, TValue> DistinctMap<TKey, TValue>(
        IEnumerable<TValue> rows,
        Func<TValue, TKey> keySelector) where TKey : notnull
    {
        var map = new Dictionary<TKey, TValue>();
        foreach (var row in rows)
        {
            map.TryAdd(keySelector(row), row);
        }

        return map;
    }

    private static CoverageInfo? Merge(CoverageInfo? left, CoverageInfo? right)
    {
        if (left is null && right is null)
        {
            return null;
        }

        return new CoverageInfo
        {
            Overflow = left?.Overflow == true || right?.Overflow == true,
            LowerBound = left?.LowerBound == true || right?.LowerBound == true,
            PetNameRollup = left?.PetNameRollup == true || right?.PetNameRollup == true,
            MissingDamageType = left?.MissingDamageType == true || right?.MissingDamageType == true,
            MissingTarget = left?.MissingTarget == true || right?.MissingTarget == true,
            MissingBuildContext = left?.MissingBuildContext == true || right?.MissingBuildContext == true,
            AmbiguousProcParent = left?.AmbiguousProcParent == true || right?.AmbiguousProcParent == true,
            UnidentifiedProcSource = left?.UnidentifiedProcSource == true || right?.UnidentifiedProcSource == true
        };
    }

    private static SessionMetricComparison BlockedSession(
        CombatSessionMetricSet left,
        CombatSessionMetricSet right,
        ComparisonReason reason) =>
        new()
        {
            DamageDealt = MetricComparison<CombatScaledAmount>.Incompatible(left.DamageDealt, right.DamageDealt, reason),
            DamageDealtSelf = MetricComparison<CombatScaledAmount>.Incompatible(left.DamageDealtSelf, right.DamageDealtSelf, reason),
            DamageDealtOwnedPets = MetricComparison<CombatScaledAmount>.Incompatible(left.DamageDealtOwnedPets, right.DamageDealtOwnedPets, reason),
            DamageReceived = MetricComparison<CombatScaledAmount>.Incompatible(left.DamageReceived, right.DamageReceived, reason),
            DamageReceivedOwnedPets = MetricComparison<CombatScaledAmount>.Incompatible(left.DamageReceivedOwnedPets, right.DamageReceivedOwnedPets, reason),
            HealingDealt = MetricComparison<CombatScaledAmount>.Incompatible(left.HealingDealt, right.HealingDealt, reason),
            HealingReceived = MetricComparison<CombatScaledAmount>.Incompatible(left.HealingReceived, right.HealingReceived, reason),
            EnduranceGranted = MetricComparison<CombatScaledAmount>.Incompatible(left.EnduranceGranted, right.EnduranceGranted, reason),
            EnduranceReceived = MetricComparison<CombatScaledAmount>.Incompatible(left.EnduranceReceived, right.EnduranceReceived, reason),
            DamageEventCount = MetricComparison<long>.Incompatible(left.DamageEventCount, right.DamageEventCount, reason),
            ActivationCount = MetricComparison<long>.Incompatible(left.ActivationCount, right.ActivationCount, reason),
            AttackResolutionCount = MetricComparison<long>.Incompatible(left.AttackResolutionCount, right.AttackResolutionCount, reason),
            DefeatCount = MetricComparison<long>.Incompatible(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), reason),
            MyDefeatCount = MetricComparison<long>.Incompatible(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), reason),
            ConfirmedRechargeCompletedCount = MetricComparison<long>.Incompatible(left.ConfirmedRechargeCompletedCount, right.ConfirmedRechargeCompletedCount, reason),
            ConfirmedStillRechargingCount = MetricComparison<long>.Incompatible(left.ConfirmedStillRechargingCount, right.ConfirmedStillRechargingCount, reason),
            UnmatchedRechargeCandidateCount = MetricComparison<long>.Incompatible(left.UnmatchedRechargeCandidateCount, right.UnmatchedRechargeCandidateCount, reason),
            DistinctTargetCount = MetricComparison<long>.Incompatible(left.DistinctTargetCount, right.DistinctTargetCount, reason),
            Accuracy = new AccuracyMetricComparison
            {
                State = ComparisonState.Incompatible,
                Reason = reason,
                Hits = MetricComparison<long>.Incompatible(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), reason),
                Misses = MetricComparison<long>.Incompatible(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), reason),
                Attempts = MetricComparison<long>.Incompatible(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), reason),
                HitRatePercentagePointReason = reason,
                HitRatePercentagePointDeltaHundredths = Metric<long>.NotCaptured()
            },
            Overkill = MetricComparison<CombatScaledAmount>.Incompatible(left.Overkill, right.Overkill, reason),
            MezDuration = MetricComparison<TimeSpan>.Incompatible(left.MezDuration, right.MezDuration, reason),
            PetInstanceCount = MetricComparison<long>.Incompatible(left.PetInstanceCount, right.PetInstanceCount, reason),
            TheoreticalRecharge = MetricComparison<TimeSpan>.Incompatible(left.TheoreticalRecharge, right.TheoreticalRecharge, reason),
            PermaHasten = MetricComparison<TimeSpan>.Incompatible(left.PermaHasten, right.PermaHasten, reason)
        };

    private static ClockMetricComparison BlockedClock(SegmentClock left, SegmentClock right, ComparisonReason reason) =>
        new()
        {
            WallClockDuration = MetricComparison<TimeSpan>.Incompatible(left.WallClockDuration, right.WallClockDuration, reason),
            ObservedAnalyticalSpan = MetricComparison<TimeSpan>.Incompatible(left.ObservedAnalyticalSpan, right.ObservedAnalyticalSpan, reason),
            TrackedPauseAdjustedDuration = MetricComparison<TimeSpan>.Incompatible(left.TrackedPauseAdjustedDuration, right.TrackedPauseAdjustedDuration, reason),
            ActiveDuration = MetricComparison<TimeSpan>.Incompatible(left.ActiveDuration, right.ActiveDuration, reason),
            WallClockDamagePerSecondHundredths = MetricComparison<long>.Incompatible(
                left.WallClockDamagePerSecondHundredths,
                right.WallClockDamagePerSecondHundredths,
                reason),
            ActiveDamagePerSecondHundredths = MetricComparison<long>.Incompatible(
                left.ActiveDamagePerSecondHundredths,
                right.ActiveDamagePerSecondHundredths,
                reason)
        };

    private static AttributionMetricComparison BlockedAttribution(
        CombatProcAttributionSummary left,
        CombatProcAttributionSummary right,
        ComparisonReason reason,
        int? leftPolicyVersion = null,
        int? rightPolicyVersion = null) =>
        new()
        {
            State = ComparisonState.Incompatible,
            Reason = reason,
            LeftPolicyVersion = leftPolicyVersion ?? left.AttributionPolicyVersion,
            RightPolicyVersion = rightPolicyVersion ?? right.AttributionPolicyVersion,
            ProcDamage = MetricComparison<CombatScaledAmount>.Incompatible(left.ProcDamage, right.ProcDamage, reason),
            DirectDamage = UnavailableAmount(reason) with { State = ComparisonState.Incompatible },
            BuildConfirmedProcDamage = UnavailableAmount(reason) with { State = ComparisonState.Incompatible },
            UnattributedDamage = UnavailableAmount(reason) with { State = ComparisonState.Incompatible },
            UnattributedProcDamage = UnavailableAmount(reason) with { State = ComparisonState.Incompatible },
            DirectCount = MetricComparison<long>.Incompatible(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), reason),
            BuildConfirmedCount = MetricComparison<long>.Incompatible(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), reason),
            CorrelatedCount = MetricComparison<long>.Incompatible(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), reason),
            UnattributedCount = MetricComparison<long>.Incompatible(Metric<long>.NotCaptured(), Metric<long>.NotCaptured(), reason)
        };
}
