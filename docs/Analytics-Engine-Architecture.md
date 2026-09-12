# CoH Analytics — Analytics Engine Architecture

Status: frozen baseline (design only) · File: `docs/Analytics-Engine-Architecture.md` · Baseline release: 0.1.3 Beta

---

## 1. Executive recommendation

**GO, with staged replacement of the combat interpretation layer and additive replacement of the persistence layer.**

The current engine is a solid *provenance-preserving log pipeline* with a *shallow single-stage combat interpreter* and a *thin aggregate-only observation store*. The provenance side (`ParserRawEvent` → `ParserEvent` with byte offsets, source-segment identity, binding generation, sequence) is genuinely good and should be **retained**. The interpretation side (`CombatEventParser`, `CombatEvent`, `CombatActorRole`) and the persistence side (`CharacterPerformanceObservation`) are **too narrow to carry** Historical/Compare and must be **replaced by new domain models rather than stretched**.

Core recommendations:

1. **Split parsing from normalization.** Grammar matching (text → grammar hit) becomes a distinct stage from normalization (grammar hit → canonical event). This is the single most important structural change; the current parser fuses both and that is why pet-prefix, "with their", multiword types, and the live "health points" heal grammar are all stuck.
2. **Introduce a new `CanonicalCombatEvent`** (superset of today's `CombatEvent`) with an explicit `Actor`/`Target` identity model, damage-type as a value object, an `EventFamily`, a mirror-classification handle, and an attached `Provenance` record. Keep `CombatScaledAmount` (hundredths) as-is.
3. **Hybrid persistence (Option D).** Immutable capture metadata + persisted aggregate cube + a **bounded, down-sampled normalized event spine** (not raw chat). Aggregates power the fast view; the event spine preserves future recompute for drilldowns the aggregates didn't foresee.
4. **Freeze a compact, content-addressed build/proc manifest into each segment.** The mutable "latest build" (`builds/{recordId}.json`) is insufficient historically. Segments must carry their own frozen proc-attribution context. Because the frozen build can itself *prove* a proc's unique slotting, attribution supports a deterministic **BuildConfirmed** mode that is stronger than heuristic correlation.
5. **Conservative, allowlist-driven deduplication.** Duplicate collapse is **evidence-driven**: only channel/message-family pairs proven (by sanitized fixtures) to be alternate representations of the same logical event are eligible to merge. Semantic similarity alone never merges events. When uncertain, **keep both** and flag coverage. This favors under-merging over data loss.
6. **Multi-axis versioning.** Separate `SegmentSchemaVersion`, `AnalyticsSemanticVersion`, `GrammarSetVersion`, `DedupPolicyVersion`, `AttributionPolicyVersion`. App version is provenance only, never a compatibility gate.
7. **Availability-typed metrics.** Every metric carries `Availability` (`Available | NotCaptured | Unsupported | Incomplete`) and, where relevant, `Confidence`/`Coverage`. `0` must never stand in for "not captured."
8. **One accumulator model for live and historical.** Live feeds it from the tail; historical feeds it from the persisted event spine; both emit the identical projection DTOs. UI never owns a formula.

MODIFY/NO-GO conditions are enumerated in §32.

---

## 2. Current architecture assessment

Grounded in the repository as it stands at 0.1.3.

**Pipeline today**
`ParserWorker` (tails claimed file, `FileShare.ReadWrite`) → `ParserRawEvent` → `ParserClassifier` (structural `ParserEvent`) → `GameplaySessionManager.ApplyCombatTelemetryLocked` → `CombatEventParser.TryParse` → `CombatEvent` → `CombatAggregator` (+ `Tracked`, `Rolling`, `Accuracy`) → on finalize `CharacterPerformanceCombatProjection` diffs baseline→current → `CharacterPerformanceObservation` persisted by `CharacterPerformanceObservationRepository`.

**Strong, retain:**
- `ParserEvent` / `ParserRawEvent` provenance: `ContextId`, `LogSourceId`, `ParserSourceSegmentId`, `BindingGeneration`, `Sequence`, `ObservedAt`, `SourceByteStart/End`, `SourceTimestamp`, `LineStatus`, `ClassificationRuleId`. This is a real, testable provenance chain and dedup/persistence can lean on it.
- `LogSourceId` — account-stable-id + normalized path + identity generation; correctly encodes that every account reuses the same daily filename, and advances a generation on file replacement.
- `CombatScaledAmount` — integer hundredths; avoids float drift. Keep as the amount primitive everywhere.
- `CharacterPerformanceObservationRepository` write discipline — atomic temp-then-move, deterministic filename, `flushToDisk`, malformed-file quarantine, duplicate/conflict resolution. This *pattern* is exactly right and should be generalized to the new segment store.
- `CharacterPerformanceCombatProjection` — pure baseline→delta with monotonic-counter regression detection. The projection *discipline* survives; its field set does not.
- Identity: `CharacterRecord` (`RecordId` + `AccountStableId` + `NormalizedCharacterName` + `Aliases`) already models "display name is not identity" and cross-account same-name distinctness.

**Weak / blocking:**
- `CombatEventParser` fuses grammar+normalization; regexes are anchored to player-only shapes (`^You hit`, `^HIT`). Pet-prefix (`Imp:  …`) never matches. `ActorRole.OwnPet` exists but is never assigned. `CompanionMissSummary` is discarded. Heal grammar expects `hit points with` but live logs emit `with {power} for {amount} health points`. Incoming damage regex requires single-token `[A-Za-z]+` type, so `unresistable Unique` fails; `with their` is not handled.
- `CombatEvent` has no actor identity beyond a 3-value role, no provenance handle, no proc-parent, no pet identity, no channel/family tag, no mirror classification.
- `CombatAggregator` accumulates only scalar totals — no per-power, per-target, per-type, per-pet, DoT-vs-direct split. Per-event detail lives only in a 1024-entry ring (`MaxRetainedCombatEvents`) that is *not persisted*.
- `CharacterPerformanceObservation` persists only damage-dealt + accuracy + defeats + XP + inf. Healing, incoming, activations, types, pets, procs, mez — none persisted. Duration is derived; there is no active-vs-wallclock distinction, no rate-denominator contract.
- No dedup stage at all. With max channels the same logical heal/pet event is mirrored many times (audit: `hits you with their` = 1510; Panacea heal delivered+received mirrored; pet lines duplicate player lines). Nothing collapses these — but equally, nothing must over-collapse legitimately identical repeats.
- Build store is a single mutable "latest" per character (`builds/{recordId}.json`, schema v1), with no historical binding to a segment.

**Net:** the substrate (provenance, identity, amount, atomic IO) is good; the *combat semantics and durable capture* are the gap.

---

## 3. Design principles

1. **Engine owns all formulas.** No WPF/HTML types in analytical models; consumers read DTOs.
2. **Never fabricate telemetry.** Absence is typed, not zeroed. Confidence is explicit where attribution is inferred.
3. **Provenance is sacred.** Every canonical event traces to `(SourceId, SourceSegmentId, BindingGeneration, Sequence, ByteStart..ByteEnd)`.
4. **Determinism.** Parsing, dedup, attribution, aggregation are pure functions of input + versioned policy. Same log + same policy versions ⇒ byte-identical canonical stream and aggregates.
5. **Versioned semantics over silent drift.** Any change that would alter historical numbers bumps a semantic version and is recorded on the segment.
6. **Bounded durability.** A segment is self-contained and size-bounded; it must recompute the supported views without the live log.
7. **Additive, non-destructive migration.** Old segments render at their best supported fidelity; they are never rewritten just to look new.
8. **Conservatism in attribution.** "Convenient" ≠ "proven." Attribution is a four-mode contract (`Direct`, `BuildConfirmed`, `Correlated`, `Unattributed`). `BuildConfirmed` is permitted only when the *frozen captured build itself* proves uniqueness — a deterministic fact, not a heuristic — and only when there is no established indication that the synced build was out-of-date relative to the character at capture time.
9. **Under-merge over data loss.** Deduplication collapses only proven mirror representations from an explicit allowlist. When evidence is insufficient, keep both events and record coverage. Merging is never used to "tidy" ambiguous data, and no post-merge counter is permitted to disguise an unsafe merge.

---

## 4. Canonical telemetry pipeline

```
raw file bytes
  → ParserWorker            (retain) line framing + byte offsets
  → ParserRawEvent          (retain) context-tagged raw line
  → ParserClassifier        (retain, extend) structural ParserEvent + channel/family hint
  → GrammarMatcher          (NEW)    ParserEvent → GrammarMatch (grammarId + captured groups), no semantics
  → Normalizer              (NEW)    GrammarMatch → CanonicalCombatEvent (actors, type VO, amount, family, channel, provenance)
  → Deduplicator            (NEW)    mirror-candidate classification (allowlist) → collapse only proven mirror pairs
  → Enricher                (NEW)    actor classification, proc-parent attribution (frozen manifest), DoT/AoE coalescing
  → CombatEngine accumulators (NEW; replaces CombatAggregator scope)
  → AnalyticalProjection DTOs (NEW)  consumed by Live / Historical / Compare
```

Two consumers feed the accumulators through the **same** interface:
- **Live**: `GameplaySessionManager` streams normalized events as they arrive.
- **Historical**: a replay reader streams the persisted event spine.

Dedup boundary summary: mirror collapse is decided at the **normalized canonical** stage (post-normalization, pre-aggregation), and only for family pairs on the versioned allowlist. Never at raw-text level, never inside aggregation, never by semantic key alone.

---

## 5. Raw grammar / normalization boundary

**Separate the two stages.**

- **`GrammarMatcher`** owns the regex table and returns a `GrammarMatch { GrammarId, Captures }`. It knows nothing about actors, damage-type semantics, or dedup. It is the only place that knows text shapes. It handles the **pet prefix at this layer**: a single outer rule strips `^(?<entity>[^:]{1,40}):  ` and records `PrefixEntity`, then re-matches the inner grammar. This means every inner grammar automatically gains a pet-scoped variant with zero duplication. `GrammarSetVersion` versions this table.
- **`Normalizer`** turns a `GrammarMatch` + `ParserEvent` provenance into a `CanonicalCombatEvent`: resolves actor/target roles, parses damage type into a `DamageType` value object (multiword, `unresistable`, `Unique`), strips optional `their`, maps "health points"/"hit points"/"granting … endurance" into `EventFamily`, tags the `ChannelFamily`, and records mirror-classification inputs. `AnalyticsSemanticVersion` versions normalization decisions.

This boundary is what unblocks: pet lines, incoming "with their", multiword types, the live heal grammar, endurance grants, mez lines, and the distinct pet incoming-roll grammar (`… HITS you! … had a X% chance to hit and rolled a Y`), all as **new grammars + normalization rules** without touching provenance or aggregation.

New grammar coverage to add (from audit), each with a fixture:
- Player heal (live): `You heal {t} with {power} for {amt} health points[ over time].`
- Heal received (live): `{s} heals you with their {power} for {amt} health points[ over time].`
- Incoming with power + `their` + multiword/`unresistable Unique` type.
- Pet-prefixed variants of all outgoing/incoming/roll/heal grammars.
- Pet incoming roll grammar (`HITS you! … had a … chance to hit and rolled a …`).
- Endurance grant (`granting them/you {n} points of endurance[ over time]`).
- Mez: `You Hold/Stun/Immobilize/… {t} with your {power}[ (OVERPOWER)].`
- Knock: `You knock {t} off their feet with your {power}!`
- Companion summary: `{power} missed!` → retained as low-confidence family, **not** discarded.
- Scorch/env: `{prefix?}The {power} scorches you for {n} points of {type} damage!`
- Keep crit grammar present but **CONDITIONAL** (no crit samples in the audit log).

---

## 6. Canonical combat event model

New record `CanonicalCombatEvent` (superset; `CombatEvent` is retired from the hot path but kept until migration completes):

```
CanonicalCombatEvent
  Provenance          EventProvenance     // §8, required
  Sequence            long                // parser sequence (ordering within source segment)
  ObservedAt          DateTimeOffset
  SourceTimestamp     DateTime?           // game clock, no tz inferred
  Family              CombatEventFamily   // enum, §below
  GrammarId           CombatGrammarId     // extended
  Actor               ActorRef            // §7
  Target              ActorRef?           // §7
  PowerName           string?             // as-surfaced (may be a proc/global)
  Attribution         PowerAttribution?   // §11, set by enricher
  Amount              CombatScaledAmount
  Magnitude           MagnitudeKind       // HitPoints | Endurance | None
  DamageType          DamageType?         // value object (multiword, unresistable flag)
  Delivery            DeliveryFlags       // DoT, Containment, Overpower, Autohit, Forced, Critical
  Outcome             AttackOutcome?      // Hit | Miss (rolled) ; null for non-roll
  DisplayedChanceHundredths long?
  RollHundredths      long?
  Channel             ChannelFamily       // inferred source channel bucket
  MirrorClass         MirrorClassification // §9: mirror-candidacy metadata (NOT a semantic dedup hash)
  DuplicateOf         long?               // sequence of the surviving canonical event, if collapsed as a proven mirror
```

`CombatEventFamily` (replaces the overloaded `CombatEventKind`): `DamageDealt, DamageReceived, HealDealt, HealReceived, EnduranceGrantDealt, EnduranceGrantReceived, AttackResolution, Activation, Defeat, Mez, Knock, CompanionMissSummary, EnvironmentDamage, Unparsed`.

Rationale: today's `CombatEventKind` conflates magnitude semantics (heal vs endurance) with family, and can't express mez/knock/endurance. `DeliveryFlags` collapses the scattered `IsOverTime/WasForced/IsAutohit` booleans and adds Containment/Overpower/Critical. `Magnitude` disambiguates "health points" heals from "endurance" grants that share the `You hit … granting` shape.

`MirrorClass` deliberately replaces the earlier "semantic DedupKey." It carries the *inputs* needed to test allowlisted mirror pairs (channel/family, actor/target identity, canonical power, amount, type, timestamp-second, sequence band) but is **not** itself a merge trigger. Two events are only ever collapsed when their families form an allowlisted mirror pair **and** provenance confirms one logical event (§9).

---

## 7. Actor and pet identity model

**Actor model.** `ActorRef` is a small value object; over-modeling is avoided.

```
ActorRef
  Type         ActorType     // Self | OwnPet | OtherPlayer | OtherPet | Enemy | Environment | Unknown
  DisplayName  string?       // raw surfaced name (may collide)
  PetKey       PetInstanceKey? // only when Type in {OwnPet, OtherPet}
```

`ActorType` is the durable enum. **Lore/temp/pseudopet is NOT an ActorType.** It is *classification metadata* resolved by the enricher into `PetClassification { Permanent | LoreOrTemp | Pseudopet | Unresolved }` with a `Confidence`. The log never tags entity type (audit-confirmed), so classification is enrichment-derived (build manifest + reference catalog + name heuristics) and **persisted only with its confidence**; `Unresolved` is a first-class, common outcome.

**Pet identity when names collide.** Pet name alone is not identity (multiple "Imp"). `PetInstanceKey` = `(OwnerRecordId?, NormalizedPetName, InstanceOrdinal)` where `InstanceOrdinal` is assigned by a **session-scoped instance resolver** using name + first-seen ordering + activity gaps. This is best-effort and flagged `CoverageLimited`; per-pet analytics roll up to `NormalizedPetName` by default and only split by instance when confidence is high. Persisted per-pet aggregates key on `NormalizedPetName` with an optional instance breakdown that consumers may ignore.

Own-pet defeat attribution stays **UNSUPPORTED** (audit: no reliable `Imp has defeated …`; "Boomer Imp" is a teammate, not the pet).

Pet-prefix normalization: `Imp:  You hit X …` ⇒ Actor=`OwnPet(Imp)`, Target=`Enemy(X)`. `Imp:  Y hits you …` ⇒ Actor=`Enemy(Y)`, Target=`OwnPet(Imp)`. The word "you" inside a pet-prefixed line refers to the prefixed pet, not Self — the Normalizer encodes this rule explicitly.

---

## 8. Provenance model

`EventProvenance` copies the durable subset of `ParserEvent` and must survive parsing **and** persistence:

```
EventProvenance
  SourceId            string   // LogSourceId.Value
  AccountStableId     string
  SourceSegmentId     Guid     // ParserSourceSegmentId
  BindingGeneration   long
  ParserSequence      long
  ByteStart / ByteEnd long
  ObservedAt          DateTimeOffset
  SourceTimestamp     DateTime?
  GrammarSetVersion   int
  ClassificationRuleId string
```

In the **persisted event spine**, provenance is stored in a **compact, columnar/interned** form (source ids and grammar ids dictionary-encoded per segment) so it does not bloat storage. Provenance is what makes conservative dedup and future recompute defensible and auditable — in particular, `ByteStart`/`ByteEnd` and `ParserSequence` are the evidence that distinguishes a **true repeated event** from a **mirror representation**.

---

## 9. Deduplication architecture

Dedup is deterministic, testable, versioned (`DedupPolicyVersion`), conservative, and occurs **once**, at the canonical stage. Its default posture is **under-merge over data loss**.

### 9.1 Core rule

> Only collapse event pairs/families that are **empirically proven** to be alternate representations of the same logical event, and only when the pair's families appear on the versioned **Mirror Compatibility Allowlist**.

Semantic similarity alone **never** makes two events duplicates. A hash of `(actor, target, power, amount, type, timestampSecond)` is explicitly **not** a merge trigger. It may only be used as one matching input *inside* an already-allowlisted mirror-pair evaluation.

### 9.2 Two-phase evaluation

**Phase A — Mirror-candidate classification.**
Each canonical event is tagged with its `ChannelFamily`/`Family`. The `Deduplicator` consults the **Mirror Compatibility Policy** — a versioned allowlist of family pairs proven (by sanitized fixtures) to be mirror representations. Only events whose family participates in an allowlisted pair are *eligible* for collapse. Everything else passes through untouched.

Initial allowlist (each entry justified by a committed fixture; nothing is added without one):
- `HealDealt ↔ HealReceived` where the same heal is surfaced on both delivery and receipt channels (Panacea/Transfusion pattern).
- Player/pet **mirrored channel** pairs where one channel restates the other for the same logical hit (e.g. a pet line and its player-channel echo proven identical in fixtures).
- `EnduranceGrantDealt ↔ EnduranceGrantReceived` mirrors, when proven.

Anything not on the allowlist (including `DamageDealt ↔ DamageDealt`) is **never** eligible — so two identical damage ticks, or two identical pet hits, in the same second are structurally ineligible to merge and remain two events.

**Phase B — Same-logical-event decision (only for eligible pairs).**
For an allowlisted candidate pair, collapse occurs only when provenance + semantics jointly confirm one logical event:
- families form an allowlisted mirror pair, **and**
- actor/target identities are the mirror-consistent counterparts, **and**
- canonical power + amount (hundredths) + damage type + magnitude match, **and**
- `SourceTimestamp` second matches, **and**
- `ParserSequence` values fall within the policy's narrow mirror band **and** the pair's `ByteStart` ranges are distinct log lines (a mirror is two different lines describing one event; identical byte range would be the same line).

If any condition is unmet, **keep both** events and record a coverage note. Survivor = earliest `ParserSequence`; the other gets `DuplicateOf`. The survivor's magnitude is unchanged.

### 9.3 What is explicitly forbidden

- No merging by semantic key alone.
- No merging across non-allowlisted families.
- No `RepeatCount` on the survivor to "absorb" a suppressed event. `RepeatCount` is **removed from dedup entirely** and does not exist as a mirror-merge artifact. (It survives only as an explicit, validated **analytical coalescing** structure for DoT chains — §10 — where the coalesced rows are proven ticks of one power, never a dedup guess.)
- No collapse of true repeated identical events.

### 9.4 Versioning

`DedupPolicyVersion` governs: the allowlist membership, the mirror band width, actor/target counterpart rules, and survivor selection. Any change bumps the version, which is stamped on the segment so Compare never mixes policies silently. The default (v1) allowlist is intentionally minimal; families are added only as fixtures prove them.

Output: a **conservatively deduped canonical stream** plus per-family diagnostics: `MirrorCollapsedCount` (proven merges) and `MirrorCandidatesHeldCount` (eligible pairs that failed Phase B and were kept separate) — both surfaced as coverage (§13, §25).

---

## 10. Enrichment architecture

Enricher runs after dedup, before aggregation. Responsibilities:

1. **Actor classification** — resolve `ActorType` refinements and `PetClassification` using the frozen build manifest (§11) + reference catalog + heuristics; attach confidence.
2. **Proc-parent attribution** — §11 (four-mode).
3. **Validated coalescing** — mark DoT tick chains and AoE fan-outs (same canonical power + same `SourceTimestamp` second across many targets) so aggregation can compute "activations vs ticks" defensibly. Coalesced tick-chain rows may carry a `TickCount` because they are proven repeats of one power on one target — this is an explicit analytical structure, not a dedup fallback, and it never hides mirror uncertainty.
4. **Canonical power identity** — map surfaced `PowerName` to a catalog power id where resolvable (`IHomecomingPowerReferenceCatalog.TryResolve`), keeping the raw string too. Enables per-power aggregation that survives display aliasing and feeds attribution matching.

Enrichment is **pure over (event + frozen manifest + catalog snapshot)**; it never reads the mutable latest build at historical replay time.

---

## 11. Build / proc correlation architecture

**Attribution model (four modes):** `PowerAttribution { Mode, ParentPowerId?, Candidates?, Evidence?, Confidence, PolicyVersion }` with `Mode ∈ { Direct, BuildConfirmed, Correlated, Unattributed }`.

- **Direct** — the log telemetry itself identifies the parent power for that occurrence (normal attack: `You hit … with your Fire Ball …`). No build needed. Highest confidence.
- **BuildConfirmed** — the log identifies the **exact proc/effect identity** (e.g. `Armageddon: Chance for Fire Damage`) **and** the **frozen captured build** contains **exactly one** matching slotted occurrence of that exact proc, which resolves to **exactly one** parent power (e.g. `Hot Feet`). The parent is then **deterministically resolved from the captured build** — no timing heuristic, no ambiguity. This is stronger than heuristic correlation and weaker than Direct (the log did not state the parent; the frozen build proved it).
  - Uniqueness is judged **against the captured build itself**, not any game-wide rule. If the build proves uniqueness, that is sufficient.
  - **Not** BuildConfirmed if any of the downgrade conditions below hold.
- **Correlated** — more than one possible parent exists in the frozen build (or evidence is otherwise non-deterministic), but deterministic contextual evidence (activation context, timing, co-occurrence) favors one candidate. Must retain `Candidates[]` and `Evidence` and a graded `Confidence`. Correlation is never promoted to BuildConfirmed.
- **Unattributed** — no defensible unique or correlated parent. The parent is unknown; the design does not guess.

**Immutability of the frozen manifest.** A frozen build manifest **never becomes stale after capture**. It is, by definition, the immutable record of the build context bound to the segment at capture time, and it remains the historical source of truth for build-based attribution for the life of the segment. There is no notion of a frozen manifest "expiring" or "drifting" once written.

**BuildConfirmed downgrade conditions.** `BuildConfirmed` is withheld (attribution falls to `Correlated` or `Unattributed` as the evidence allows) when any of the following holds:
1. **No frozen build manifest available** — the segment has no manifest bound to it (the manifest reference is null because no build was synced/available at capture).
2. **Capture-time build was unsynced or out-of-date** — the underlying synced build was already out-of-date relative to the character at capture time (the user changed slotting/powers and did not resync before the segment was captured), **and** the engine can detect or establish that condition. This is a capture-time property, not a property of the frozen manifest degrading afterward. No detection mechanism is mandated here; if the engine cannot establish this condition, it does not fabricate one — but where it can, BuildConfirmed must not be asserted.
3. **Proc evidence is ambiguous** — the exact proc is slotted in more than one power in the frozen manifest (multi-power ⇒ not BuildConfirmed).
4. **Proc cannot be matched** — the proc/effect identity cannot be matched to a slotted occurrence in the frozen manifest or to a catalog enhancement.

**Incarnate / global effects** (`Reactive Interface`, `Doublehit`, `Particle Burst`, and similar) use **separate attribution semantics**: their "parent" is the Incarnate/global slot, not an attack power. They are attributed to a `GlobalEffectSource` category (BuildConfirmed against the manifest's Incarnate/global slots when uniquely present, else Unattributed) and are **excluded** from per-attack proc-parent contribution so they never inflate a specific power's proc share.

**Proc totals remain reliable independent of parentage.** Proc damage totals and proc contribution % of overall damage are computed from the (deduped) proc events themselves and can be `Available`/RELIABLE even when every parent is `Unattributed`. Only **proc-by-parent-power** attribution depends on the attribution mode and carries its confidence.

**Historical binding.** A mutable latest build is insufficient. Each segment **freezes a compact, content-addressed build/proc manifest** — the historical source of truth for build-based attribution:

```
FrozenBuildManifest
  ManifestHash        string   // content address (sha256 of normalized manifest)
  SourceBuildRecordId CharacterRecordId
  SourceBuildFile     string?
  SourceLastWriteUtc  DateTimeOffset?
  CapturedAtUtc       DateTimeOffset
  BuildCatalogVersion int
  Powers[]  { CanonicalPowerId, RawPowerToken, PowerSetToken, Category }
  ProcSlots[] { EnhancementToken, CanonicalEnhancementId?, ExactProcIdentity, SlottedInPowerIds[], IsAttuned, IsGlobalOrIncarnate }
```

`ExactProcIdentity` is the log-comparable proc name/effect identity used for BuildConfirmed matching. `SlottedInPowerIds` length == 1 (and `IsGlobalOrIncarnate` == false) is the precise BuildConfirmed precondition — subject to the downgrade conditions above. Segments store the manifest hash + the manifest (small — the audit build had ~37 powers and a handful of proc slots); content-addressing dedupes identical manifests to one blob (`builds/manifests/{hash}.json`) while each segment references it immutably. Build-changed-after-capture, Incarnate/global effects, pseudopets, temp powers, and catalog-version drift are all handled because the segment reasons **only** over its frozen manifest and the `BuildCatalogVersion` it was built with. If no build was synced at capture, the manifest reference is null and all proc parentage is `Unattributed` — never guessed, never BuildConfirmed.

`AttributionPolicyVersion` governs BuildConfirmed matching rules (including how a detectable capture-time unsynced/out-of-date condition suppresses BuildConfirmed), global/Incarnate handling, correlation thresholds, and confidence grading.

---

## 12. Analytical accumulator / domain model

**One accumulator model for live and historical.** `CombatEngine` consumes `CanonicalCombatEvent` and maintains a set of dimensioned accumulators:

- **Scalar totals** per family (damage dealt/received, heal dealt/received, endurance granted, defeats).
- **Per-power cube**: key `(Scope, CanonicalPowerId|RawName, AttributionMode)` → { total, hits, ticks, directVsDoT split, type breakdown, largestHit, targetsSeen }.
- **Per-type**: `DamageType → total`.
- **Per-target**: `NormalizedTargetName → damageTotal` (cardinality-bounded, §26).
- **Per-pet**: `NormalizedPetName → { damage, dps-basis, heals, damageTaken, accuracy }` (+ optional instance split).
- **Accuracy**: retains the existing `CombatAccuracyAccumulator` semantics (rolled attempts, forced, autohit-tracked-separately) — this logic is correct and is **reused**.
- **Proc**: two independent views — (a) `proc total` and `proc contribution %` (parent-independent, reliable); (b) `(ParentPowerId|BuildConfirmed|Correlated|Unattributed) → procDamage` with attribution mode carried through.
- **Mez/knock counts**: per family + per power.
- **Rolling windows**: retain `RollingCombatAccumulator` concept for live.

`Scope ∈ { Self, OwnPetsAggregate, PerPet }` so "player vs pet" is a first-class split rather than a filter.

Each accumulator emits an **immutable projection DTO** (`CombatAnalyticsProjection`) — POCO, no WPF/HTML. Live and Historical both produce this identical DTO; Compare consumes two of them. This is the "engine owns formulas" guarantee.

Bounded per-dimension cardinality with an explicit `Other`/overflow bucket and a `CoverageLimited` flag when overflow occurs.

---

## 13. Metric availability / confidence model

A metric is not a bare number. Introduce a small generic:

```
Metric<T>
  Value        T?
  Availability MetricAvailability  // Available | NotCaptured | Unsupported | Incomplete
  Confidence   MetricConfidence?   // High | Medium | Low   (only when inferred)
  Coverage     CoverageInfo?       // optional: sampled %, overflow, mirror-candidates-held
```

- `Available` — computed from sufficient evidence.
- `NotCaptured` — this segment's schema/coverage never recorded the inputs (old segment, disabled channel). **Never rendered as 0.**
- `Unsupported` — the game/log cannot express it (overkill, buff uptime, mez duration).
- `Incomplete` / `CoverageLimited` — partial evidence (cardinality overflow, sampled spine, low-confidence attribution, mirror candidates held apart under uncertainty).

**Attribution-driven confidence mapping:**
- `Direct` / `BuildConfirmed` ⇒ proc-by-parent metric `Available`, `Confidence = High` (BuildConfirmed labeled as such so consumers can distinguish log-stated from build-proven).
- `Correlated` ⇒ `Available`, `Confidence = Medium/Low` with candidate evidence.
- `Unattributed` ⇒ proc-by-parent metric `Incomplete` for that parent bucket, **but** proc total / proc contribution % remain `Available` (parent-independent).

**Avoid infecting every type:** raw accumulators stay as plain numbers internally; the `Metric<T>` wrapper is applied **only at the projection boundary**, driven by (a) segment coverage descriptor, (b) semantic-version support matrix, (c) per-attribution confidence rolled up. Confidence lives at three deliberate layers only: **per-attribution** (proc parent), **per-metric** (rolled up), **per-segment coverage** (what was captured). Not per-event-field.

---

## 14. Time / rate semantics

Standardize a single `SegmentClock` to prevent Compare mixing denominators:

```
SegmentClock
  CaptureStartUtc / CaptureEndUtc   // wall-clock bounds (immutable)
  WallClockDuration                 // End - Start
  ActiveDuration                    // sum of engaged intervals (idle gaps > IdleThreshold removed)
  RateDenominatorPolicy             // enum: WallClock | Active   (versioned)
  IdleThresholdSeconds              // from CombatActivityDefaults, recorded
```

- **Event timestamp**: prefer `SourceTimestamp` (game clock, second resolution) for ordering within a segment; `ObservedAt` for arrival. Persist both.
- **Rate denominator**: every rate (DPS, dmg/min, heal/min, XP/hr) declares whether it used `WallClock` or `Active`. Persist the policy on the segment. Compare **refuses** to difference two rates computed under different `RateDenominatorPolicy` unless both can be recomputed from the spine to a common policy.
- **Rolling windows**: live-only presentation concept; not persisted as historical truth.
- Paused/inactive periods are represented as gaps in `ActiveDuration`, computed deterministically from the event stream, not wall clock.

This directly fixes today's ambiguity where DPS uses raw elapsed with no active/idle distinction.

---

## 15. Durable segment model

A segment is a **self-contained analytical capture**. Hybrid (Option D) with clearly separated layers:

```
Segment (on disk = a bounded document set in one directory)
  1. CaptureMetadata          (immutable)      §16
  2. SegmentClock             (immutable)      §14
  3. CoverageDescriptor       (immutable)      captured families/dims, channel set, dedup/attr/grammar versions, mirror diagnostics
  4. FrozenBuildManifest ref  (immutable)      §11 (content-addressed, may be null)
  5. AggregateCube            (immutable)      §12 outputs, the fast path
  6. NormalizedEventSpine     (immutable)      bounded, down-sampled canonical events, §17
  7. Annotations              (MUTABLE)        §16 (DisplayName, IsBeta, IncludeInOverview)
```

Layers 1–6 are written once, atomically, and never rewritten. Layer 7 is a **separate small sidecar file** so renaming/annotating never rewrites the large payload.

---

## 16. Segment metadata and annotations

**Immutable capture metadata** (all persisted):
`CharacterRecordId`, `AccountStableId`, `AccountDisplayNameAtCapture`, `CharacterDisplayNameAtCapture`, `LevelAtCapture`, `Archetype`, `PrimaryPowerSet`, `SecondaryPowerSet`, `CaptureStartUtc`, `CaptureEndUtc`, `Duration` (bounds stored), `AppVersion` (provenance only), `SegmentSchemaVersion`, `AnalyticsSemanticVersion`, `GrammarSetVersion`, `DedupPolicyVersion`, `AttributionPolicyVersion`, `BuildManifestHash?`, `GameplaySessionId`, `SegmentOrdinal`.

**Mutable annotations** (sidecar, immutable metadata untouched):
`UserDisplayName?`, `IsBeta` (simple boolean — no shard/environment abstraction), `IncludeInOverview`, free-text note (optional).

Identity rule enforced: `AccountStableId + CharacterRecordId` is the identity; `CharacterDisplayNameAtCapture` is descriptive and frozen at capture. `adelbert/Hell's Vengence` and `RivenForest/Hell's Vengence` remain distinct by `AccountStableId`. Renaming a segment edits only the sidecar.

---

## 17. Persistence format recommendation

**Hybrid, bounded.** Do **not** copy raw chat logs. Do **not** persist aggregates-only.

- **AggregateCube** — JSON (camelCase), the primary read path. Small, bounded by per-dimension caps. Powers Historical/Compare instantly.
- **NormalizedEventSpine** — a **down-sampled, columnar, compressed** canonical event log. Retention policy:
  - Keep **all** low-frequency, high-value events (defeats, mez, activations, heals, largest-hit candidates, proc events, incoming hits, and both members of any *held* (non-collapsed) mirror-candidate pair so recompute can re-evaluate them).
  - For high-frequency DoT ticks / AoE fan-out, keep **validated coalesced tick-chain records** (power, target, tickCount, sumAmount, min/max, window) rather than every tick. This is the key bound; coalescing is proven-repeat compression, never dedup.
  - Store columnar with dictionary-encoded strings (power names, actor names, source ids), Deflate/GZip the payload. Target: a 40-minute max-telemetry farm → single-digit MB spine.
- **File layout** under `%LocalAppData%\CoH Analytics\Characters\Segments\{gameplaySessionId}_{ordinal:D10}\`:
  - `metadata.json`, `coverage.json`, `aggregates.json`, `spine.bin` (compressed), `annotations.json` (mutable sidecar).
  - Content-addressed manifests in shared `builds/manifests/{hash}.json`.
- **Atomic writes**: reuse the existing `CharacterPerformanceObservationRepository` discipline (temp file, `flushToDisk`, move, malformed-file quarantine, deterministic naming). Generalize it into a `SegmentStore`.
- **Corruption/recovery**: each file independently validated; a bad `spine.bin` degrades to aggregate-only (`Incomplete`), not segment loss. A bad `aggregates.json` triggers recompute-from-spine when the spine is intact.

**Intentionally NOT retained:** raw chat text, per-tick DoT rows, unbounded per-target rows beyond cap, rolling-window state, live session scaffolding, presentation state, any dedup-merge counter.

---

## 18. Versioning and semantic compatibility

Compatibility is **multi-axis**; app version is never the gate.

| Axis | Governs | Effect on compat |
|---|---|---|
| `SegmentSchemaVersion` | file/field layout | reader migrates layout in memory |
| `AnalyticsSemanticVersion` | metric definitions | Compare gates on equality/known-mapping |
| `GrammarSetVersion` | grammar table | recompute-from-spine only |
| `DedupPolicyVersion` | mirror allowlist + band + survivor rules | Compare refuses cross-policy rate/aggregate diffs unless recomputable |
| `AttributionPolicyVersion` | Direct/BuildConfirmed/Correlated/Unattributed rules, global handling, thresholds | affects proc-by-parent metrics' mode/confidence only |
| `RateDenominatorPolicy` | rate math | Compare normalizes or blocks |

Rule: a reader **declares a support matrix** mapping (semanticVersion, families) → supported metrics. Opening an older segment yields `Available` only for metrics its versions support; everything else is `NotCaptured`/`Unsupported`, never zero. Newer numbers are derived from the spine **only if** the persisted evidence suffices. Because dedup defaults to under-merging and retains held mirror-candidate pairs in the spine, a later, better `DedupPolicyVersion` can safely re-evaluate them on recompute; an earlier over-merge could not be undone, which is precisely why over-merging is prohibited.

---

## 19. Historical replay / recalculation strategy

- **Primary read path**: load `aggregates.json` → project to DTO. O(cube size), instant.
- **Recompute path**: when a newer `AnalyticsSemanticVersion`/`GrammarSetVersion`/`DedupPolicyVersion`/`AttributionPolicyVersion` can extract more from the spine, replay `spine.bin` through the *current* engine. Because held mirror candidates and full proc events are retained, recompute can apply a newer allowlist or a newer BuildConfirmed rule without data loss. This is **non-destructive** — the on-disk segment is not rewritten; an optional user-initiated "re-bake" may write a new segment version alongside, never overwriting.
- **Aggregate-only fallback**: if spine is absent/corrupt or coverage insufficient, serve aggregates with `Incomplete` coverage.
- Determinism guarantee: replay of the same spine under pinned policy versions reproduces the persisted aggregates exactly (a test invariant).

---

## 20. Cross-account / stable-character identity handling

Reuse existing identity substrate:
- `CharacterRecord` (`RecordId`, `AccountStableId`, `NormalizedCharacterName`, `CurrentDisplayName`, `Aliases`) is the canonical identity; keep it.
- Segments key on `(AccountStableId, CharacterRecordId)`; display name is frozen-at-capture descriptive metadata only.
- Same display name on different accounts ⇒ different `AccountStableId` ⇒ distinct. Rename ⇒ same `RecordId`, alias appended; historical segments keep their captured name.
- Historical/Compare enumerate segments across **all** accounts/characters on the machine by scanning the `Segments/` root, grouping by `(AccountStableId, CharacterRecordId)`, resolving current display via `CharacterRecord` while showing captured display where relevant.

---

## 21. Comparison engine model

`ComparisonEngine.Compare(SegmentProjection a, SegmentProjection b)` → `ComparisonResult` of typed deltas. No user math.

For each metric:
```
MetricComparison
  Left / Right      Metric<T>
  AbsoluteDelta     Metric<T>          // Right - Left, when both Available & same units
  PercentDelta      Metric<double>     // for ratios/totals
  PercentagePoint   Metric<double>     // for rates expressed as % (hit rate, contribution %)
  State             ComparisonState    // Comparable | Incompatible | Unavailable | Normalized
```

Rules:
- **Raw totals vs normalized rates** are distinct (§14): totals compare directly only when durations equal; otherwise engine compares **normalized rates** and marks totals `Incompatible` unless the user explicitly wants raw.
- **Zero-baseline**: `PercentDelta` from a zero left is `Unavailable` (not ∞); absolute delta still shown.
- **Denominator/semantic/dedup/attribution mismatch**: `Incompatible` with reason, or `Normalized` if recomputable from both spines to a common policy.
- `NotCaptured` on either side ⇒ `Unavailable` (never treated as 0 difference).

Compare is arbitrary-segment: same char before/after build change (frozen manifest hash differs — surfaced), two chars, cross-account same-name, cross-AT, Live-vs-Beta (`IsBeta` shown, never blocks).

---

## 22. Supported metric classification

**RELIABLE** (Direct log evidence, player-scope):
- total damage, DPS, damage/min, damage by power, per-power DPS, hits, misses, hit rate, attack roll/chance, largest hit, DoT vs direct split, damage-type mix, damage by target / top targets, activations, defeats (player `You have defeated`), defeats/min, XP/hr, Influence/hr.
- **proc damage total** and **proc contribution %** (parent-independent).

**CONDITIONALLY RELIABLE** (needs conservative dedup, new grammar, enrichment, or attribution; flagged with confidence/coverage):
- healing given/received, healing/min, healing by power (needs live "health points" grammar + allowlisted mirror collapse); incoming damage total/by source/by enemy power/by type, avg & largest incoming hit (needs `their`/multiword + mirror handling); average damage per activation (needs validated AoE/DoT coalescing); pet damage, pet DPS, pet contribution %, pet healing, pet damage taken, per-pet contribution, pet hit/miss/accuracy (needs pet-prefix parsing + instance model); **proc damage by parent power** (Direct/BuildConfirmed ⇒ High; Correlated ⇒ Medium/Low; Unattributed ⇒ Incomplete for that bucket); mez event counts (counts only); enemy hit/miss/roll where surfaced; permanent-vs-Lore/temp classification (confidence-graded).

**UNSUPPORTED** (no log/game evidence — do not build):
- overkill, target HP reconstruction, full encounter reconstruction, exact mez duration, buff uptime, debuff uptime, server-hidden state, own-pet defeat attribution. Crit metrics remain **UNSUPPORTED until real crit fixtures exist** (none in audit log).

---

## 23. Backward compatibility with existing saved observations

- `CharacterPerformanceObservation` v1/v2 files are **retained and readable**. Provide a `LegacyObservationAdapter` that maps an old observation into the new `SegmentProjection` shape: damage dealt, accuracy, defeats, XP, inf ⇒ `Available`; **everything else ⇒ `NotCaptured`** (healing, incoming, pets, procs, types). No zero-filling.
- Old observations have no spine and no build manifest ⇒ recompute disabled, aggregate-only, `Incomplete` coverage; proc parentage `NotCaptured` (never Unattributed-vs-BuildConfirmed, since no manifest existed). They remain usable in Overview/Historical at real fidelity and comparable (rate-normalized) against new segments with `Unavailable` on missing axes.
- The existing `PerformanceObservations/` directory and repository stay; new segments live under `Segments/`. No destructive migration; Overview enumerates both stores.

---

## 24. Test / replay-fixture architecture

- **Sanitized immutable fixtures** derived from the real max-telemetry log, checked into the test project (never referencing the user's live path). Sanitization: replace character/account/teammate names with stable pseudonyms via a deterministic map, keep power names, types, amounts, timestamps, and channel prefixes intact. Store under `CoHAnalytics.Tests/Fixtures/Combat/`.
- **Fixture families** (each a focused file + expected canonical/aggregate JSON): player damage, incoming (`their`+multiword `unresistable Unique`), live "health points" heals (delivered+received mirror), hit/miss/forced/autohit, DoT chains, activations, permanent pet (`Imp:`), Lore pet (`Ravager/Defiler Essence:`), pseudopet (`Enervating Storm:`), pet incoming damage, pet incoming roll grammar, pet healing, procs (`Armageddon: Chance…`, `Panacea…`, `Reactive Interface`, `Doublehit`), mez/knock/OVERPOWER, endurance grants, `{power} missed!`, malformed lines, unknown/future lines, streakbreaker, autohit.

- **Attribution fixtures (four-mode):**
  1. Direct — `You hit … with your Fire Ball …` ⇒ `Mode=Direct`, parent=Fire Ball, no manifest needed.
  2. BuildConfirmed — `Armageddon: Chance for Fire Damage` + fixture manifest where that exact proc is slotted **once**, in Hot Feet ⇒ `Mode=BuildConfirmed`, parent=Hot Feet.
  3. Same proc in **multiple** powers in the fixture manifest ⇒ **NOT** BuildConfirmed; `Mode=Correlated` (with candidates) or `Unattributed` per evidence.
  4. **No frozen build manifest available** (null manifest reference) ⇒ `Mode=Unattributed`; proc **total** still `Available`.
  5. **Capture-time build unsynced/out-of-date** — a fixture in which an establishable indication exists that the synced build was out-of-date relative to the character at capture time ⇒ BuildConfirmed **withheld** (downgrades to `Correlated`/`Unattributed`); the frozen manifest itself is still treated as immutable and never described as "stale."
  6. **Proc cannot be matched** to the frozen manifest/catalog ⇒ `Mode=Unattributed`.
  7. Incarnate/global (`Reactive Interface`) ⇒ global-effect attribution path, excluded from per-attack proc-parent share.
  8. Unattributed parent ⇒ proc contribution % still RELIABLE.

- **Dedup fixtures (conservative allowlist):**
  1. Proven **heal mirror** (delivered/received) collapses to exactly **one** event.
  2. Proven **pet/player mirror** collapses to exactly **one** event.
  3. **Two identical real damage ticks** in the same second remain **TWO** events.
  4. **Two identical pet hits** in the same second remain **TWO** events.
  5. **Same semantic shape from a non-allowlisted family** remains **separate**.
  6. **Ambiguity favors under-merging** — an allowlisted pair that fails any Phase-B provenance condition is kept as two events and flagged `MirrorCandidatesHeld`.
  7. No-`RepeatCount`-merge invariant — assert dedup never emits a survivor with an absorbed-duplicate count.

- **Golden tests**: grammar match table, normalization, dedup phases A/B, attribution four modes incl. multi-power, no-manifest, capture-time-unsynced downgrade, and global, accumulator projections, `SegmentClock` active-vs-wallclock, `Metric<T>` availability (NotCaptured ≠ 0), unequal-duration Compare, cross-account same-name distinctness, legacy v1/v2 adapter, replay-determinism (spine→aggregates reproducibility incl. held mirror re-evaluation under a newer `DedupPolicyVersion`).
- Preserve the ~2,486 existing tests; new engine lands behind new types so current suites stay green during migration.

---

## 25. Diagnostics / failure observability

- Reuse existing JSONL diagnostics (`ApplicationDataPaths.GetLogsRoot`).
- Per-segment **CoverageDescriptor** doubles as diagnostics: matched/unmatched line counts, `IsCombatShapedUnparsed` rate, **`MirrorCollapsedCount`** and **`MirrorCandidatesHeldCount`** per family, attribution mode histogram (Direct/BuildConfirmed/Correlated/Unattributed), cardinality-overflow flags.
- **Held-mirror visibility**: because ambiguity favors under-merging, held candidates are explicitly counted so a human can tune the allowlist/band later; they are never silently merged.
- **Attribution coverage visibility**: where the engine can establish a capture-time unsynced/out-of-date build condition, that establishment is recorded as coverage on the segment so consumers understand why BuildConfirmed was withheld. Absence of such a signal is never treated as proof the build was current.
- **Unparsed capture**: combat-shaped-but-unparsed lines are counted and a bounded sanitized sample retained to drive future grammar work.
- Determinism canary: a hash of the canonical (deduped) stream per fixture, asserted in CI.

---

## 26. Performance / storage considerations

- **Hot path**: grammar matching dominates. Keep compiled `GeneratedRegex`; add the single outer pet-prefix strip to avoid doubling the table. Match order by observed frequency (damage dealt dominates).
- **Validated coalescing** of DoT/AoE caps memory and spine size; single powers emit thousands of ticks.
- **Conservative dedup is cheap**: Phase A is a family-membership check; Phase B runs only on allowlisted candidates within a narrow sequence/timestamp band — no global O(n²) semantic hashing.
- **Cardinality caps** on per-target/per-pet with `Other` overflow bucket + `CoverageLimited`.
- **Spine**: columnar + dictionary + Deflate; single-digit MB per long farm. Held mirror candidates add little because they are the minority.
- **Live memory**: bounded tail feeding the same accumulators; do not hold the whole session in RAM.
- **IO**: atomic writes, one-time per segment; annotations sidecar avoids large rewrites.

---

## 27. Risks and edge cases

- **Over-merging in dedup** — eliminated by allowlist-only eligibility + provenance-confirmed Phase B + prohibition on semantic-key-only merges. No `RepeatCount` exists to disguise a bad merge. Residual risk is **under-merging**, which is the accepted, recoverable failure mode (held candidates retained in spine, re-evaluable later).
- **Legitimate identical repeats** — structurally protected: `DamageDealt↔DamageDealt` is not allowlisted, so two identical same-second ticks/pet hits stay separate by construction (fixtures 3–4).
- **False BuildConfirmed** — prevented: requires exactly-one slotted occurrence in the *frozen* build, non-global, catalog-matched, with no establishable capture-time unsynced/out-of-date condition. Multi-power, no-manifest, unmatched, and detectable capture-time-out-of-date all downgrade. Game-wide uniqueness is never assumed.
- **Capture-time build out-of-date** — the real risk is that the user changed slotting/powers and did not resync before capture, so the synced build was already out-of-date *at capture time*. This is a property of the moment of capture, not of the frozen manifest, which never becomes stale afterward. Where the engine can establish this condition, BuildConfirmed is withheld; where it cannot, the design does not fabricate a mechanism — detection/coverage is an implementation concern and its absence is not treated as proof the build was current.
- **No frozen manifest** — proc parentage `Unattributed`, never guessed; the manifest reference may be null; proc totals stay reliable.
- **Pet instance collisions** — default `NormalizedPetName` rollup; instance split only high-confidence; `CoverageLimited`.
- **Multiword/`unresistable Unique`/future types** — tolerant `DamageType` VO; unknowns pass through.
- **Channel-set variance** — CoverageDescriptor records enabled-family evidence so `NotCaptured` is honest and mirror allowlist behavior is interpretable.
- **Clock resolution** — second-resolution game timestamps; ties broken by `ParserSequence`.
- **Spine growth on pathological sessions** — hard cap + graceful `Incomplete`.
- **Semantic drift** — every metric-affecting change bumps a version; Compare guards.

---

## 28. Existing code to retain / reuse

- `ParserWorker`, `ParserRawEvent`, `ParserEvent`, `ParserClassifier`, `LogSourceId`, `ParserSourceSegmentId`, `MonitoringSessionManager` and log-discovery stack — **retain**.
- `CombatScaledAmount` — **retain** as the amount primitive.
- `CombatAccuracyAccumulator` semantics (rolled/forced/autohit-separate) — **retain/port**.
- `RollingCombatAccumulator` concept — retain for live.
- `CharacterPerformanceObservationRepository` atomic-write/quarantine pattern — **generalize** into `SegmentStore`.
- `CharacterPerformanceCombatProjection` baseline→delta discipline — retain as a pattern for live→segment finalize.
- Identity stack: `CharacterRecord`, `CharacterRecordId`, `AccountStableId`, aliases — **retain**.
- Build stack: `CharacterBuildSnapshot`, `HomecomingBuildLayoutSnapshot`, `HomecomingBuildLayoutParser`, `IHomecomingPowerReferenceCatalog`, `BuildEnhancementIndex` — **reuse** as inputs to `FrozenBuildManifest` (including `ExactProcIdentity` and slotted-power resolution for BuildConfirmed).

---

## 29. Existing code likely needing replacement / restructuring

- `CombatEventParser` — **split** into `GrammarMatcher` + `Normalizer`; regex table extended. Old class kept until migration completes, then retired.
- `CombatEvent` / `CombatEventKind` / `CombatActorRole` — **superseded** by `CanonicalCombatEvent` / `CombatEventFamily` / `ActorRef`/`ActorType`. (`CombatActorRole.OwnPet` was never used — replaced outright.)
- `CombatAggregator` scalar-only accumulation — **replaced** by dimensioned `CombatEngine` accumulators.
- `CharacterPerformanceObservation` as the *durable analytical unit* — **replaced** by the `Segment` model (old observations kept read-only via adapter).
- `CombatAnalyticsPresentation` — **restructured**: formulas move into engine projections; presentation becomes a thin mapper.
- `GameplaySessionManager.ApplyCombatTelemetryLocked` — rewired to the new pipeline stages.

---

## 30. Dependency-ordered implementation slices

Each slice is independently shippable, green-tested, and non-regressive.

### Slice 1 — Grammar/Normalization split (no behavior change)
- **Objective**: extract `GrammarMatcher` and `Normalizer` from `CombatEventParser`, producing today's `CombatEvent` output unchanged.
- **Files**: `CombatEventParser.cs` (split), new `GrammarMatcher.cs`, `Normalizer.cs`, `CombatGrammarId` (extend later).
- **New abstractions**: `GrammarMatch`.
- **Migration impact**: none externally.
- **Tests**: all existing combat parser tests pass byte-identical; add golden grammar-match tests.
- **Acceptance**: output equals pre-split for all current fixtures.
- **Depends on**: none.
- **Must NOT change**: provenance types, aggregation, persistence, public event shape.

### Slice 2 — Canonical event model + provenance carry-through
- **Objective**: introduce `CanonicalCombatEvent`, `EventProvenance`, `ActorRef`/`ActorType`, `DamageType`, `CombatEventFamily`, `DeliveryFlags`, `MirrorClassification`; Normalizer emits it; adapter maps to legacy `CombatEvent` for existing accumulators.
- **Files**: new models; `Normalizer.cs`; temporary `CanonicalToLegacyAdapter`.
- **Migration**: internal only.
- **Tests**: normalization golden tests incl. provenance fields; adapter equivalence.
- **Acceptance**: legacy accumulators still produce identical snapshots via adapter.
- **Depends on**: 1.
- **Must NOT change**: persisted observation format.

### Slice 3 — New grammars (player) + damage-type VO
- **Objective**: add live "health points" heals, incoming `their`+multiword/`unresistable Unique`, endurance grants, mez/knock, `{power} missed!`, env scorch.
- **Files**: `GrammarMatcher.cs`, `Normalizer.cs`, fixtures.
- **Tests**: one fixture family per grammar; `IsCombatShapedUnparsed` rate drops on the sanitized real log.
- **Acceptance**: audit's failing lines now normalize with correct family/type.
- **Depends on**: 2. **Must NOT change**: dedup/aggregation yet.

### Slice 4 — Pet-prefix + pet actor model
- **Objective**: outer pet-prefix strip; pet-scoped variants; `ActorType.OwnPet/OtherPet`; `PetInstanceKey` rollup by name.
- **Files**: `GrammarMatcher.cs`, `Normalizer.cs`, `PetInstanceResolver.cs`, fixtures (Imp, Essence, Enervating Storm, pet incoming, pet roll, pet heal).
- **Tests**: pet fixtures; "you inside pet line = the pet" rule; instance rollup.
- **Acceptance**: pet damage/heal/incoming normalize with correct actor/target.
- **Depends on**: 3. **Must NOT change**: classification confidence (Unresolved for now), defeat attribution.

### Slice 5 — Conservative allowlist deduplication
- **Objective**: `Deduplicator` with **Mirror Compatibility Allowlist** (Phase A) + provenance-confirmed same-logical-event decision (Phase B); `DedupPolicyVersion` v1 minimal allowlist; **no** semantic-key-only merging; **no** `RepeatCount` merge artifact; under-merge on uncertainty with held-candidate diagnostics.
- **Files**: `Deduplicator.cs`, `MirrorCompatibilityPolicy.cs`, `MirrorClassification.cs`.
- **New abstractions**: versioned allowlist; `MirrorCandidatesHeldCount`/`MirrorCollapsedCount` diagnostics.
- **Migration**: internal only.
- **Tests (required)**:
  1. proven heal mirror collapses to one;
  2. proven pet/player mirror collapses to one;
  3. two identical real damage ticks same second remain two;
  4. two identical pet hits same second remain two;
  5. non-allowlisted family with identical shape remains separate;
  6. ambiguity (Phase B fails) keeps both + flags held;
  7. invariant: survivor never carries an absorbed-duplicate count.
- **Acceptance**: max-channel fixture collapses **only** allowlisted mirrors; all identical-repeat and non-allowlist cases remain separate; held candidates counted, not merged.
- **Depends on**: 4. **Must NOT change**: aggregation formulas; must NOT introduce any semantic-hash auto-merge; must NOT add RepeatCount to survivors.

### Slice 6 — Dimensioned accumulators + projection DTOs
- **Objective**: `CombatEngine` with per-power/type/target/pet/proc/mez cubes; emit `CombatAnalyticsProjection`; wire live path; retire `CanonicalToLegacyAdapter`.
- **Files**: `CombatEngine.cs`, accumulators, `CombatAnalyticsProjection.cs`; rewire `GameplaySessionManager`; refactor `CombatAnalyticsPresentation` to map DTO.
- **Tests**: projection goldens; cardinality caps/overflow; reuse accuracy tests; proc total vs proc-by-parent separation.
- **Acceptance**: live Combat view renders new metrics; existing scalar metrics unchanged.
- **Depends on**: 5. **Must NOT change**: on-disk formats.

### Slice 7 — Metric availability/confidence + time model
- **Objective**: `Metric<T>`, `MetricAvailability`, `SegmentClock` (active vs wallclock), rate-denominator policy at projection boundary.
- **Files**: `Metric.cs`, `SegmentClock.cs`, projection mapping.
- **Tests**: NotCaptured≠0; active-vs-wallclock rates; denominator policy stamped.
- **Acceptance**: projections expose availability; rates declare denominator.
- **Depends on**: 6.

### Slice 8 — Frozen build manifest + four-mode proc attribution
- **Objective**: build `FrozenBuildManifest` (content-addressed, with `ExactProcIdentity`, `SlottedInPowerIds`, `IsGlobalOrIncarnate`) from `CharacterBuildSnapshot`; `Enricher` sets `PowerAttribution` in **Direct / BuildConfirmed / Correlated / Unattributed**; separate global/Incarnate path; `AttributionPolicyVersion`.
- **Files**: `FrozenBuildManifest.cs`, `BuildManifestStore.cs` (`builds/manifests/{hash}.json`), `ProcAttributionEnricher.cs`.
- **New abstractions**: four-mode `PowerAttribution` with `Candidates`/`Evidence`/`Confidence`.
- **Migration impact**: new manifest store; no rewrite of existing builds.
- **Tests (required)**: Direct; BuildConfirmed (exactly-one slot ⇒ deterministic parent); multi-slot ⇒ not BuildConfirmed (Correlated/Unattributed); **no frozen manifest available (null reference) ⇒ Unattributed**; **establishable capture-time unsynced/out-of-date build ⇒ BuildConfirmed withheld**; proc cannot be matched ⇒ Unattributed; global/Incarnate ⇒ separate path, excluded from per-attack share; proc total/contribution % remain Available under Unattributed; manifest hash stability/content-addressing.
- **Acceptance**: BuildConfirmed produced **only** when the frozen build proves single-slot uniqueness **and** there is no established indication the synced build was out-of-date relative to the character at capture time; never from game-wide assumptions; the frozen manifest is never treated as becoming stale after capture; proc totals reliable regardless of parent mode.
- **Depends on**: 6 (7 recommended). **Must NOT change**: dedup policy; must NOT promote Correlated to BuildConfirmed; must NOT read the mutable latest build during historical replay; must NOT invent a capture-time out-of-date detection mechanism where none exists.

### Slice 9 — Durable Segment model + hybrid persistence
- **Objective**: `SegmentStore` (metadata/coverage/aggregates/spine/annotations), atomic writes, validated-coalesced compressed spine (retaining held mirror candidates + proc events), annotations sidecar.
- **Files**: `SegmentStore.cs`, `SegmentDocument*.cs`, spine codec.
- **Tests**: atomicity/corruption→Incomplete; annotation update doesn't rewrite payload; spine size bound on max-telemetry fixture; replay-determinism (spine→aggregates, incl. re-evaluating held mirrors under a newer `DedupPolicyVersion`).
- **Acceptance**: a captured farm persists as a bounded self-contained segment; held mirror candidates recoverable.
- **Depends on**: 6,7,8. **Must NOT change**: legacy observation files.

### Slice 10 — Historical read + legacy adapter
- **Objective**: Historical loads any segment (new + legacy v1/v2 via `LegacyObservationAdapter`), aggregate-fast + spine-recompute paths.
- **Files**: `HistoricalSegmentReadService.cs`, `LegacyObservationAdapter.cs`.
- **Tests**: legacy ⇒ NotCaptured on missing axes; recompute determinism; cross-account enumeration.
- **Acceptance**: Historical shows the same projection shape as live for one segment.
- **Depends on**: 9.

### Slice 11 — Comparison engine
- **Objective**: `ComparisonEngine` typed deltas (absolute/percent/percentage-point), incompatible/unavailable/normalized states, denominator/semantic/dedup/attribution guards.
- **Files**: `ComparisonEngine.cs`, `MetricComparison.cs`.
- **Tests**: unequal duration; zero-baseline; NotCaptured side; cross-policy refuse/normalize; cross-account same-name; build-change surfaced via manifest hash.
- **Acceptance**: any two segments compare without user math.
- **Depends on**: 10.

### Slice 12 — Diagnostics + fixture pipeline hardening
- **Objective**: CoverageDescriptor diagnostics (incl. mirror-held and attribution-mode histograms), unparsed sampling, determinism canary in CI; finalize sanitized fixture corpus from the real log.
- **Files**: diagnostics wiring, `Fixtures/Combat/*`.
- **Tests**: canary hashes; unparsed-rate thresholds; held-mirror and attribution-mode reporting.
- **Acceptance**: CI guards semantic drift and dedup/attribution regressions.
- **Depends on**: 1–11.

---

## 31. Explicit deferred work

- Final WPF Combat/Historical/Compare UI and HTML report (out of scope; engine emits DTOs only).
- Optional timing-based **Correlated** proc attribution beyond deterministic context (BuildConfirmed never depends on it).
- A concrete mechanism for detecting whether a synced build was out-of-date relative to the character at capture time (the semantic rule is defined; detection/coverage is deferred and must not be fabricated).
- Optional user-initiated segment "re-bake" to a new version.
- Crit analytics (blocked on real crit fixtures).
- Any encounter/HP/uptime/overkill reconstruction (UNSUPPORTED).
- Pet instance-split analytics beyond name rollup (ship rollup first).
- Expansion of the mirror allowlist beyond fixtures-proven pairs (additive, versioned).

---

## 32. Final recommendation

**GO** on the architecture as specified, executed strictly in the slice order of §30, with the two corrected contracts as hard requirements:

- **Proc attribution is four-mode** (`Direct`, `BuildConfirmed`, `Correlated`, `Unattributed`). `BuildConfirmed` is a deterministic, build-proven parentage — permitted only when the frozen captured build contains exactly one matching slotted occurrence of the exact proc, non-global, catalog-matched, and only when there is no established indication that the synced build was out-of-date relative to the character at capture time. The frozen manifest never becomes stale after capture; the only build-currency risk is a capture-time unsynced/out-of-date build, and where that condition can be established BuildConfirmed is withheld. Proc totals/contribution remain reliable independent of parentage.
- **Deduplication is conservative and allowlist-driven.** Only fixtures-proven mirror family pairs are eligible; provenance confirms same-logical-event; semantic similarity alone never merges; identical real repeats always stay separate; uncertainty under-merges and is flagged; no merge counter may mask uncertainty.

**MODIFY** if, during Slice 5, sanitized fixtures reveal a mirror family pair that cannot be provenance-confirmed reliably — in that case narrow the allowlist further (prefer keeping both events) rather than widening the band; revisit before Slice 9.

**NO-GO** only for anything in the UNSUPPORTED list (§22), and for any attribution that would assert `BuildConfirmed` without single-slot proof in the frozen build (or in the face of an established capture-time out-of-date build), or any dedup that would merge on semantic key alone. Those must not enter the engine.

Guiding rule throughout: **engine correctness and durable, versioned semantics first; presentation later. Under-merge over data loss; prove parentage, never assume it.**

---

## Appendix A — Revision history

**Revision 1 — Proc attribution (four-mode).**
- Replaced three-mode (`Direct/Correlated/Unattributed`) with `Direct / BuildConfirmed / Correlated / Unattributed`.
- Added **BuildConfirmed**: deterministic parent resolution when the frozen captured build has exactly one matching slotted occurrence of the exact proc; uniqueness judged against the captured build, not game-wide rules; no timing heuristic.
- Clarified proc totals and proc contribution % remain reliable even when parent is Unattributed.

**Revision 2 — Deduplication (conservative, allowlist-driven).**
- Removed semantic-key auto-merge; introduced the versioned Mirror Compatibility Allowlist (Phase A) + provenance-confirmed same-logical-event decision (Phase B).
- Guaranteed identical real repeats and non-allowlisted families stay separate; ambiguity under-merges with held-candidate diagnostics.
- Removed `RepeatCount` from dedup entirely; retained only as explicit validated DoT-coalescing.
- Replaced `DedupKey` on the event model with `MirrorClassification`.

**Revision 3 (freeze) — Frozen-manifest wording correction.**
- Removed all wording implying a frozen build manifest can become "stale" after capture. A frozen manifest is immutable and never becomes stale.
- Reframed the real build-currency risk as a **capture-time** property: the synced build may already have been unsynced/out-of-date relative to the character when the segment was captured.
- Split the former single combined manifest-availability downgrade into four distinct conditions: (1) no frozen build manifest available, (2) capture-time build unsynced/out-of-date where establishable, (3) proc evidence ambiguous, (4) proc cannot be matched to the frozen manifest/catalog.
- Stated the semantic rule that BuildConfirmed must not be asserted when a capture-time out-of-date condition can be detected/established, while explicitly deferring any detection mechanism (not to be fabricated).
- Updated §3, §11, §13, §22, §23, §24 (attribution fixtures 4–6 and golden tests), §25, §27, Slice 8 tests/acceptance/must-not-change, §31 deferred work, and §32 accordingly.

This document is the frozen architecture baseline for implementation.
