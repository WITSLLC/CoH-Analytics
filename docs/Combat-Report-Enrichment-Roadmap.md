# Combat Report Enrichment Roadmap

Status: implementation roadmap · File: `docs/Combat-Report-Enrichment-Roadmap.md` · Scope: historical Segment HTML reporting

---

## Purpose

The historical HTML combat report is intended to move toward the visual structure of `CoH_Analytics_Detailed_Combat_Mockup_v2(2).html`. The mockup is a design reference, not an analytical contract: its example values, filters, findings, charts, percentages, and labels are not evidence that the current engine can produce those results.

The immediate redesign must consume the authoritative aggregate already persisted in each durable `Segment`. `HistoricalSegmentReadService` exposes that aggregate as the same `CombatAnalyticsProjection` contract used by the live analytical path. `HtmlReportRenderer` remains a presentation-only consumer of the historical DTO. It must not replay events, consult mutable build or catalog state, or calculate analytics that the projection does not expose.

This document records:

- data that the current report can safely present;
- small, additive engine and projection enrichments that unlock the next report tier;
- capabilities blocked by the current persistence and replay model;
- mockup concepts intentionally deferred; and
- boundaries that future implementation must preserve.

The governing architecture remains `docs/Analytics-Engine-Architecture.md`. This roadmap narrows that architecture to combat-report enrichment and does not supersede its authority, versioning, attribution, persistence, or comparison rules.

## Presentation Contract

The redesigned player-facing report must follow these rules:

1. `NotCaptured` metrics are omitted. Do not render their cards, placeholder rows, or empty columns.
2. `Unsupported` metrics are normally omitted. They may appear only in a deliberately diagnostic view where the unsupported state itself is useful.
3. `Incomplete` and `CoverageLimited` values may render when the value remains useful. Present a subtle, specific caveat instead of raw engine-state text.
4. Empty sections and tables disappear. A table column disappears when no rendered row has a captured value for it.
5. The renderer performs formatting, ordering, labeling, conditional visibility, and layout only.
6. The renderer does not own formulas. Shares, rates, averages, deltas, percentages, findings, and grouped totals must arrive as authoritative DTO fields.
7. The report does not parse or replay raw logs or the retained normalized spine.
8. The report does not consult the current item or power catalog to reinterpret a historical capture.
9. The report does not compare a historical capture with the character's current build unless an explicit frozen comparison or bridge DTO supplies that result.
10. The report does not fabricate timelines, active intervals, pet instances, enemy classifications, coverage percentages, or other unsupported telemetry.
11. Rate metrics must carry an explicit denominator contract. Labels such as DPS, per minute, and active rate are not interchangeable.
12. Bounded overflow rows remain distinct from literal values named “Other.” The renderer must not manufacture an “Other” bucket by summing hidden rows.

These rules apply to ordinary player-facing output. A collapsed diagnostics section may expose version, retention, replay, hash, and coverage facts that would be distracting in the main report.

## Supported Now

The following sections can be built from current authoritative DTOs without presentation-layer analytics.

### Historical context

`HistoricalSegmentHeader`, `SegmentAnnotations`, `SegmentClock`, and `CombatBuildContextSummary` support:

- character display name, level, archetype, and primary/secondary powersets at capture;
- capture start and end timestamps;
- optional user display name for the Segment;
- capture kind, Segment identity, and compatibility state;
- capture-wall duration and conditional tracked pause-adjusted duration;
- frozen build-context presence, manifest hash, and catalog fingerprint; and
- a static report path or breadcrumb.

Active combat duration is not supported. A build manifest being present means that capture-time build context was attached; it does not mean that the historical build matches the character's current build.

### Summary metrics

`CombatSessionMetricSet`, `SegmentClock`, and `CombatProcAttributionSummary` support:

- total owner damage;
- local-player damage;
- owned-pet damage;
- validated proc damage and overall proc contribution;
- capture-wall DPS and capture-wall duration;
- self damage received and separately scoped owned-pet damage received;
- healing dealt and received;
- endurance granted and received;
- raw session accuracy counts when captured; and
- optional observed activation and recharge-state counts.

The report must label capture-wall DPS explicitly. `ActiveDamagePerSecondHundredths` and `ActiveDuration` are unsupported and must not be inferred from capture gaps or current engagement state.

The current projection exposes accuracy attempts, hits, and misses, but not a typed accuracy percentage. The immediate redesign may show the counts. It must not calculate the rate in HTML.

`CombatSessionSummary.DefeatCount` and `MyDefeatCount` are persisted scalar counters, but the projection does not yet expose a typed enemy-defeat metric distinct from all defeat observations and player deaths. The first redesign should avoid using the mockup's unqualified “Defeats” card until that semantic distinction is explicit.

### Offense and power detail

`CombatPowerAnalysisRow` supports separate outgoing rows for self, owned-pet aggregate, and normalized per-pet scopes. The report may present:

- power name and analytical scope;
- damage, healing, and endurance magnitude where captured;
- event count;
- observed activation count;
- direct-delivery and DoT-delivery amounts;
- largest observed hit for the power;
- per-power damage-type totals;
- distinct-target count with coverage; and
- confirmed recharge and still-recharging observation counts.

Rows may be sorted by authoritative damage amount for presentation. That ranking does not authorize contribution percentages, averages, efficiency judgments, or other derived analysis.

`HitResolutionCount` must not be presented as a hit count. The accumulator increments it for attack-resolution observations generally; it does not provide per-power hit and miss outcomes.

### Damage types and survivability

The projection supports authoritative outgoing and incoming damage-type amounts and event counts, including missing-type and overflow coverage. Incoming-direction power rows support:

- ranked incoming powers by damage;
- incoming damage and direct/DoT amounts per power;
- per-power maximum observed hit; and
- victim scope, including self and normalized owned-pet rollups.

The current aggregate does not retain an enemy source actor, enemy rank, or a full leaderboard of individual incoming hits. “Largest observed hits” can therefore mean only one retained maximum per incoming power, not the top individual hit events shown in the mockup.

### Pet contribution

`CombatActorSummary` and per-pet power rows support normalized pet-name rollups with:

- display and normalized pet name;
- damage dealt and received;
- healing dealt and received;
- endurance granted and received;
- observed activations; and
- raw accuracy attempts, hits, and misses.

This is name-level authority, not pet-instance authority. Same-name pets are combined. Pet rows carry coverage limitation because exact instance separation is unsupported.

### Proc attribution

`CombatProcAttributionSummary` and `CombatProcParentRow` support:

- attribution policy version;
- validated proc total and engine-derived overall contribution;
- Direct, BuildConfirmed, Correlated, and Unattributed modes;
- exact proc identity where recognized;
- parent power ID and display name where supported;
- damage and event count by parent/proc row;
- evidence, confidence, remaining candidates, and metric availability; and
- explicit indication that bounded parent rows are incomplete.

Under current policy, Correlated is structurally represented but is not emitted. BuildConfirmed rows are the safe high-confidence parent-power presentation. Incomplete or unattributed rows may still be useful when their caveat is clear.

### Healing, support, targets, and lifecycle

The immediate redesign may add:

- per-power healing and endurance tables;
- actor and pet support totals;
- target display/normalized name, damage, and damage-event count;
- target overflow and lower-bound caveats;
- session and per-power activation observations; and
- confirmed recharge-completed and still-recharging observations.

Target rank or classification is not captured. Lifecycle intervals remain `NotCaptured`, while theoretical recharge and related build-derived timing metrics remain `Unsupported`; those fields should disappear from normal output.

### Coverage and verification

A collapsed verification panel may use:

- metric availability and `CoverageInfo` flags;
- projection-level and row-level `CoverageLimited` state;
- frozen build availability;
- analytics semantic, Segment schema, spine schema, grammar, dedup, and attribution-policy versions;
- logical event, ignored duplicate, and retained spine counts;
- spine retention limit and truncation state;
- the `LosslessReplayCoverageMatrix`;
- manifest hash and catalog fingerprint; and
- historical compatibility and component status.

Parser diagnostics and raw samples are deliberately not persisted in a Segment. The report must not convert their absence into an invented parser-confidence percentage.

## Near-Term DTO / Engine Enrichment

Every percentage, rate, average, and share below must be computed by the engine or projection layer and persisted as part of the authoritative aggregate when it is needed historically. The HTML renderer may only format the supplied value.

| Capability | Why useful | Current limitation | Intended authoritative source | Likely layer to change | Notes / semantic requirements |
| --- | --- | --- | --- | --- | --- |
| Power contribution/share | Enables ranked contribution charts and `% Total` | Power damage exists; share does not | Session owner damage and each outgoing power row | `CombatEngine`, `CombatAnalyticsProjection` | Define denominator and scope. Self, pet aggregate, and per-pet rows must not double-count one another. |
| Pet contribution/share | Supports the pet summary card and table | Pet damage exists; share does not | Owner total damage and non-overlapping pet scope | Engine/projection | Carry pet-name-rollup coverage into the result. |
| Damage-type share | Enables amount-plus-share charts | Type amounts exist; share does not | Complete outgoing or incoming type breakdown and matching total | Engine/projection | Missing types or overflow may make the share incomplete; do not normalize incomplete known rows to 100%. |
| Target share | Shows how damage was distributed across targets | Target damage exists; share does not | Owner damage and target cube | Engine/projection | Preserve target overflow/lower-bound semantics. |
| Proc-parent row share | Supports the mockup proc table | Overall proc contribution exists; per-row share does not | Parent-row proc damage and an explicit denominator | Attribution projection | State whether denominator is all owner damage or validated proc damage. |
| Session direct/DoT totals and shares | Supports a session composition panel | Direct and DoT exist only per power | Engine-maintained session delivery totals | `CombatEngine`, projection DTO | Proc attribution overlaps delivery mode. Do not model Direct + DoT + Proc as a mutually exclusive partition unless the engine introduces and names such a taxonomy. |
| Typed session accuracy percentage | Supports the accuracy card without renderer math | Counts are available; rate is not a metric | Session accuracy accumulator | Engine/projection | Define attempts, autohits, forced hits, rounding, scale, availability, and evidence. |
| Per-power attempts/hits/misses/accuracy | Supports truthful power diagnostics | Only a general resolution count exists per power | Per-power resolution outcome accumulator | `CombatEngine`, `CombatPowerAnalysisRow` | Do not reinterpret `HitResolutionCount`. Preserve forced-hit and autohit semantics if applicable. |
| Typed per-power activation/recharge availability | Distinguishes observed zero from uncaptured telemetry | Current per-power values are scalars | Per-power lifecycle accumulator | Projection DTO, possibly engine observation flags | Add availability wrappers rather than treating zero as coverage. |
| Pet DPS | Supports pet performance comparison | Pet damage exists; no rate exists | Pet damage plus explicit clock metric | Engine/projection | Denominator must be named: capture wall, tracked pause-adjusted, or a future pet-active duration. Never label one as another. |
| Enemy-defeat metric | Supports an unambiguous Defeats card | Existing counters mix all defeat observations and player deaths | Defeat event actor/target semantics | Normalizer/engine/projection | Expose enemy defeats separately from `MyDefeatCount`; availability must be typed. |
| Bounded incoming-hit leaderboard | Supports multiple largest-hit rows with source metadata | Only one maximum per incoming power survives | Engine-maintained bounded top-N observations | Engine, projection, Segment aggregate | Decide bounds, stable ordering, enemy identity/rank evidence, privacy, schema impact, and overflow behavior before implementation. |
| Findings DTO | Enables deterministic observations without renderer inference | No findings contract exists | Engine-derived facts from supported aggregates or future time series | Analytical service/projection | Findings must be typed, versioned, evidence-backed, and presentation-neutral. Timeline-dependent findings remain blocked until timeline support exists. |

Share DTOs should use the same fixed-scale conventions as existing percentage metrics and retain availability, evidence, denominator, and coverage. A zero denominator must produce an unavailable derived metric rather than infinity or a fabricated zero percentage.

## Persistence / Timeline Enrichment

The mockup's pacing chart, range selection, active-combat analysis, and time-dependent findings require a separate persistence design. They are not small renderer changes.

Future architectural work may include:

- a persisted, versioned damage-over-time series;
- an incoming-pressure series with explicit magnitude and denominator semantics;
- a defined active-combat state machine and persisted active/idle intervals;
- engine-side range projection over supported time buckets;
- historical range filters that return a new authoritative projection;
- a timeline coverage contract for gaps, bucket width, late events, and timestamp precision; and
- compatibility behavior for Segments captured before the series exists.

The current normalized spine is bounded to the first 1,024 logical events in apply order. That spine:

- is not representative sampling;
- is not a substitute for the authoritative aggregate cube;
- contains no raw log text;
- may be truncated while aggregate totals remain complete;
- cannot support faithful full-session replay when truncated; and
- does not make clock, frozen-build, or proc-attribution state replayable merely because individual events are retained.

`LosslessReplayCoverageMatrix` keeps aggregate authority and spine replay capability separate. A dimension may be aggregate-authoritative while remaining `NotRecomputable` from the spine. A future timeline design must preserve that distinction and must never silently replace full-session totals with results from a partial spine.

Until this work exists, the historical report must omit:

- damage-over-time and incoming-pressure charts;
- active and idle bands;
- active duration, active DPS, and non-combat percentages;
- peak sustained intervals;
- drag or range selection;
- arbitrary cross-filtering by time, power, target, or damage type;
- pet stability and pressure-spike analysis; and
- timeline-derived findings.

The live `RollingCombatScopeSnapshot` exposes scalar 1-, 2-, 5-, 10-, and 15-minute windows. It is not a chart series, is unavailable after finalization, and is not persisted into the historical Segment aggregate. It does not solve historical timeline reporting.

## Pet Attribution Future Work

Normalized pet-name rollup is the current authoritative contract. `CombatActorSummary.PetInstanceCount` is `Unsupported`, and same-name instances must remain combined in the report.

The current Slice 8 attribution boundary also remains important: owned-pet damage never uses player parentage. A proc observed through an owned pet does not currently BuildConfirm back to the slot in the player's summon power. The report must not infer that relationship from matching names, timing, build slots, or ownership alone.

Future pet-to-summon-power attribution needs an explicit evidence model. That design must define:

- how a pet instance or normalized pet family is linked to a summon power;
- whether the evidence survives historical persistence;
- how multiple simultaneous same-name pets are handled;
- how pseudopets, global effects, and Incarnate effects differ;
- how ambiguous or stale build context affects availability and confidence; and
- which attribution-policy or semantic-version change governs the new behavior.

Until then, show owned-pet damage as pet damage and preserve proc-parent uncertainty.

## Findings / Narrative Analysis

Narrative conclusions must arrive through an engine-derived findings DTO. `HtmlReportRenderer` must never inspect rows and generate claims such as “strong,” “efficient,” “stable,” “largest pressure spike,” or “review accuracy.” Sorting a table by an authoritative amount is presentation work; declaring why that result occurred or whether it is good is analysis.

A future findings contract should provide, at minimum:

- a stable finding kind;
- authoritative structured values used by the finding;
- availability, evidence, confidence, and coverage;
- semantic and policy version context;
- any comparison or time-range dependency; and
- presentation-neutral parameters rather than preformatted HTML.

Largest aggregate contributor can be identified from current power amounts, but its contribution percentage still needs a DTO. Peak sustained damage, pressure spikes, and pet stability require time-series support and remain blocked until that support is persisted and projected authoritatively.

## Compare-Specific Enrichment

Session deltas belong in an explicit Compare result. The ordinary Analysis report must not automatically choose a “previous saved session,” because previous is a selection policy rather than an analytical fact.

`ComparisonEngine` already compares two authoritative projections and supplies:

- absolute and relative session metric deltas;
- capture-wall rate deltas;
- accuracy percentage-point delta;
- power, actor, damage-type, and target comparisons;
- build hash and catalog-fingerprint equality; and
- proc-attribution comparison.

Its compatibility gates remain authoritative. Analytics semantic-version mismatch blocks incompatible numeric comparison. Attribution-policy mismatch blocks attribution comparison. Coverage and overflow may downgrade individual dimensions to partial or unavailable. Report presentation must not bypass these states or recalculate deltas from the two displayed reports.

## Build Analysis Boundary

The combat report may safely surface frozen capture-time build facts already present in `CombatBuildContextSummary` and `FrozenBuildManifest`:

- whether build context was captured;
- manifest hash;
- capture-time catalog fingerprint;
- build/proc mapping availability and coverage; and
- BuildConfirmed proc-parent rows.

These facts do not authorize use of current Build Analysis results. Build set analysis, bonuses, current enhancement state, theoretical recharge, resistance utilization, and other current-catalog calculations belong to a separate subsystem. Historical combat reporting may include them only after an explicit frozen bridge DTO defines the capture-time input, analytical semantics, availability, and compatibility behavior.

The renderer must never load the current build or catalog to fill a historical report gap.

## Deferred Mockup Features

The first redesign intentionally omits the following mockup concepts:

| Feature | Reason for deferral |
| --- | --- |
| Browser navigation back into WPF | Exported HTML has no supported app-navigation contract. |
| Decorative Overview/Detailed toggle | The report has no two underlying modes to switch between. |
| “Vs prior saved session” in Analysis | Prior-session selection and deltas belong in Compare. |
| Time, target, power, and damage-type filters | Persisted aggregates cannot recompute all dependent panels for arbitrary intersections. |
| Damage and pressure timelines | No persisted authoritative time series exists. |
| Active-combat duration, DPS, and percentage | No session-total active-combat model exists. |
| Numeric coverage/confidence percentages | Segment persistence exposes typed availability and flags, not those percentages. |
| Per-power accuracy and average per hit | Per-power outcome counts and authoritative derived metrics are absent. |
| Pet DPS and share | Damage exists; rate and share DTOs do not. |
| Enemy rank/classification filters | Rank is not retained in the aggregate. |
| Exact same-name pet-instance rows | Current authority is normalized name rollup. |
| Multiple largest incoming-hit events with source/rank | Only a maximum per incoming power is retained. |
| Evaluative status labels | No authoritative findings or evaluation policy exists. |
| Narrative findings generated in HTML | Analytical conclusions belong in an engine-derived findings DTO. |
| Renderer-generated “Other” groups | Summing hidden rows is analytics; only explicit overflow rows are authoritative. |
| Current Build Analysis metrics | No frozen bridge DTO ties those calculations to the historical capture. |

## Suggested Implementation Order

1. Redesign historical HTML using only current authoritative DTOs. Implement omission and subtle caveat behavior before adding visual density.
2. Add low-risk share and rate DTOs with explicit denominators, availability, evidence, and coverage.
3. Add per-power attempts, hits, misses, accuracy, and typed lifecycle availability.
4. Add pet DPS/share and a typed enemy-defeat metric.
5. Decide whether a bounded incoming-hit leaderboard provides enough value to justify new aggregate and Segment fields.
6. Design timeline persistence, active-combat semantics, and range projection as a separate architecture change.
7. Add findings and range analysis only after their supporting aggregate or time-series facts are authoritative and persisted.

Each engine or persistence change must carry the appropriate analytics semantic, attribution-policy, replay-matrix, and Segment compatibility review. The renderer redesign itself should not force a Segment schema change.

## Traceability

The principal existing contracts are:

- `docs/Analytics-Engine-Architecture.md` — governing analytical architecture, availability, persistence, versioning, attribution, and comparison rules.
- `Models/CombatAnalyticsProjection.cs` — session, power, damage-type, actor/pet, target, build-context, and proc-attribution DTOs.
- `Services/CombatEngine.cs` — authoritative accumulation, clock rates, delivery dimensions, bounded spine retention, and proc attribution.
- `Services/HistoricalSegmentReadService.cs` — historical aggregate loading, optional component selection, and compatibility/status handling.
- `Models/LosslessReplayCoverageMatrix.cs` — separation of aggregate authority from replay capability.
- `Models/FrozenBuildManifest.cs` — immutable capture-time build/proc evidence and manifest identity.
- `Services/ComparisonEngine.cs` and `Models/AnalyticalComparison.cs` — comparison deltas and compatibility gates.
- `Services/HtmlReportRenderer.cs` — presentation-only historical report consumer.

## Non-Goals

This roadmap:

- does not authorize analytics formulas in `HtmlReportRenderer` or another presentation helper;
- does not change the current Segment schema or replay matrix by itself;
- does not commit the project to implementing every concept in the mockup;
- does not require timeline support for the next report redesign;
- does not authorize raw-log or spine replay inside report generation;
- does not authorize mutable catalog or current-build reinterpretation of historical captures; and
- does not define UI styling, CSS, chart libraries, or final report layout.
