# CoH Analytics — Analytics Engine Architecture

Status: frozen baseline (design only) · File: `docs/Analytics-Engine-Architecture.md` · Baseline release: 0.1.3 Beta

---

## 1. Executive recommendation

**GO, with staged replacement of the combat interpretation layer and additive replacement of the persistence layer.**

The current engine is a solid *provenance-preserving log pipeline* with a *shallow single-stage combat interpreter* and a *thin aggregate-only observation store*. The provenance side (`ParserRawEvent` → `ParserEvent` with byte offsets, source-segment identity, binding generation, sequence) is genuinely good and should be **retained**. The interpretation side (`CombatEventParser`, `CombatEvent`, `CombatActorRole`) and the persistence side (`CharacterPerformanceObservation`) are **too narrow to carry** Historical/Compare and must be **replaced by new domain models rather than stretched**.

Core recommendations:

1. **Split parsing from normalization.** Grammar matching (text → grammar hit) becomes a distinct stage from normalization (grammar hit → canonical event). This is the single most important structural change; the current parser fuses both and that is why pet-prefix, "with their", multiword types, and the live "health points" heal grammar are all stuck.
2. **Introduce a new `CanonicalCombatEvent`** (superset of today's `CombatEvent`) with an explicit `Actor`/`Target` identity model, damage-type as a value object, an `EventFamily`, a mirror-classification handle, and an attached `Provenance` record. Keep `CombatScaledAmount` (hundredths) as-is.
3. **Hybrid persistence (Option D).** Immutable capture metadata + persisted aggregate cube + a **bounded, potentially down-sampled normalized event spine** (not raw chat). Aggregates power the fast view and remain authoritative where spine detail is discarded; the spine supports only explicitly declared lossless/sufficient recomputations.
4. **Freeze a compact, content-addressed build/proc manifest into each segment.** The mutable "latest build" (`builds/{recordId}.json`) is insufficient historically. Segments must carry their own frozen proc-attribution context. Because the frozen build can itself *prove* a proc's unique slotting, attribution supports a deterministic **BuildConfirmed** mode that is stronger than heuristic correlation.
5. **Conservative, allowlist-driven deduplication.** Duplicate collapse is **evidence-driven**: only channel/message-family pairs proven (by sanitized fixtures and an independent discriminator for that log form) to be alternate representations of the same logical event are eligible to merge. Semantic similarity, distinct byte ranges, and sequence proximity alone never prove sameness. The initial allowlist may be empty. When uncertain, **keep both** and flag coverage. This favors under-merging over data loss.
6. **Multi-axis versioning.** Separate `SegmentSchemaVersion`, `AnalyticsSemanticVersion`, `GrammarSetVersion`, `DedupPolicyVersion`, `AttributionPolicyVersion`. App version is provenance only, never a compatibility gate.
7. **Availability-typed metrics.** Every metric carries `Availability` (`Available | NotCaptured | Unsupported | Incomplete`) and, where relevant, `Confidence`/`Coverage`. `0` must never stand in for "not captured."
8. **One analytical engine and projection contract for live and historical.** Live feeds accumulators from the full committed stream; the 1024-event tail is bounded retention only. Historical projects from authoritative persisted aggregates and replays only fields for which the spine declares sufficient lossless evidence. Both emit the identical projection DTOs. UI never owns a formula.

MODIFY/NO-GO conditions are enumerated in §32.

---

## 2. Current architecture assessment

Grounded in the repository as it stands at 0.1.3.

**Pipeline today**
`ParserWorker` (tails claimed file, allowing shared read/write/delete) → `ParserRawEvent` → `ParserClassifier` (structural `ParserEvent`) → `GameplaySessionManager.ApplyCombatTelemetryLocked` → `CombatEventParser.TryParse` → `CombatEvent` → `CombatAggregator` (+ `Tracked`, `Rolling`, `Accuracy`) → on finalize `CharacterPerformanceCombatProjection` diffs baseline→current → `CharacterPerformanceObservation` persisted by `CharacterPerformanceObservationRepository`.

**Strong, retain:**
- `ParserEvent` / `ParserRawEvent` provenance: `ContextId`, `LogSourceId`, `ParserSourceSegmentId`, `BindingGeneration`, `Sequence`, `ObservedAt`, `SourceByteStart/End`, optional `SourceTimestamp`, `LineStatus`, `ClassificationRuleId`. This is a real, testable provenance chain. It establishes line occurrence and ordering context, but does not by itself prove that two lines are one logical event. `ParserClassifier` recognizes some channel/speaker shapes but does not retain the actual channel in `ParserEvent` today.
- `LogSourceId` — account-stable-id + normalized path + identity generation; correctly encodes that every account reuses the same daily filename, and advances a generation on file replacement.
- `CombatScaledAmount` — integer hundredths; avoids float drift. Keep as the amount primitive everywhere.
- `CharacterPerformanceObservationRepository` write discipline — atomic temp-then-move for one file, deterministic filename, `flushToDisk`, malformed-file reporting/skipping, duplicate/conflict resolution. Reuse these patterns, while adding a separate multi-file publication protocol for the new segment store.
- `CharacterPerformanceCombatProjection` — pure baseline→delta with monotonic-counter regression detection. The projection *discipline* survives; its field set does not.
- Identity: `CharacterRecord` (`RecordId` + `AccountStableId` + `NormalizedCharacterName` + `Aliases`) already models "display name is not identity" and cross-account same-name distinctness.

**Weak / blocking:**
- `CombatEventParser` fuses grammar+normalization; regexes are anchored to player-only shapes (`^You hit`, `^HIT`). Pet-prefix (`Imp:  …`) never matches. `ActorRole.OwnPet` exists but is never assigned. `CompanionMissSummary` is discarded. Heal grammar expects `hit points with` but live logs emit `with {power} for {amount} health points`. Incoming damage regex requires single-token `[A-Za-z]+` type, so `unresistable Unique` fails; `with their` is not handled.
- `CombatEvent` has no actor identity beyond a 3-value role, no provenance handle, no proc-parent, no pet identity, no channel/family tag, no mirror classification.
- `CombatAggregator` accumulates only scalar totals — no per-power, per-target, per-type, per-pet, DoT-vs-direct split. Per-event detail lives only in a 1024-entry ring (`MaxRetainedCombatEvents`) that is *not persisted*.
- `CharacterPerformanceObservation` persists only damage-dealt + accuracy + defeats + XP + inf. Healing, incoming, activations, types, pets, procs, mez — none persisted. Duration is derived; there is no active-vs-wallclock distinction, no rate-denominator contract.
- No semantic combat dedup stage at all. The max-channel audit identified candidate heal/pet mirrors, but current parser provenance does not retain actual channel identity and proximity is not mirror proof. Nothing collapses these today — but equally, nothing must over-collapse legitimately identical repeats.
- Build store is a single mutable "latest" per character (`builds/{recordId}.json`, schema v1), with no historical binding to a segment.

**Net:** the substrate (provenance, identity, amount, atomic IO) is good; the *combat semantics and durable capture* are the gap.

---

## 3. Design principles

1. **Engine owns all formulas.** No WPF/HTML types in analytical models; consumers read DTOs.
2. **Never fabricate telemetry.** Absence is typed, not zeroed. Confidence is explicit where attribution is inferred.
3. **Provenance is sacred.** Every canonical event traces to `(SourceId, SourceSegmentId, BindingGeneration, Sequence, ByteStart..ByteEnd)`.
4. **Determinism.** Parsing, dedup, attribution, and aggregation are deterministic given the same persisted input provenance and versioned policy. Independent rereads of the same log can create different `ParserSourceSegmentId` GUIDs and `ObservedAt` values; a cross-reread semantic digest must exclude or normalize runtime-generated provenance rather than promise byte-identical full events.
5. **Versioned semantics over silent drift.** Any change that would alter historical numbers bumps a semantic version and is recorded on the segment.
6. **Bounded durability.** A segment is size-bounded and renders supported views without the live log. The aggregate cube is authoritative where the bounded spine is lossy; exact replay is guaranteed only for explicitly lossless or sufficient-for-recompute fields. Any referenced immutable manifest blob must be durably available or its needed resolved facts must accompany the segment.
7. **Additive, non-destructive migration.** Old segments render at their best supported fidelity; they are never rewritten just to look new.
8. **Conservatism in attribution.** "Convenient" ≠ "proven." Attribution is a four-mode contract (`Direct`, `BuildConfirmed`, `Correlated`, `Unattributed`). `BuildConfirmed` is permitted only when the *frozen captured build itself* proves uniqueness — a deterministic fact, not a heuristic — and only when there is no established indication that the synced build was out-of-date relative to the character at capture time.
9. **Under-merge over data loss.** Deduplication collapses only independently evidenced mirror representations from an explicit allowlist. Byte offsets and sequence bands are candidate filters, not proof. When evidence is insufficient, keep both events and record coverage. A proven merge preserves all analytically valid directional facets without counting one magnitude twice; no post-merge counter may disguise an unsafe merge.

---

## 4. Canonical telemetry pipeline

```
raw file bytes
  → ParserWorker            (retain) line framing + byte offsets
  → ParserRawEvent          (retain) context-tagged raw line
  → ParserClassifier        (retain, extend) structural ParserEvent + optional actual source-channel/message discriminator where present
  → GrammarMatcher          (NEW)    ParserEvent → GrammarMatch (grammarId + captured groups), no semantics
  → Normalizer              (NEW)    GrammarMatch → CanonicalCombatEvent (actors, type VO, amount, family, optional channel, provenance)
  → Deduplicator            (NEW)    mirror-candidate classification (allowlist) → collapse only independently proven mirror pairs, preserving facets
  → Enricher                (NEW)    actor classification, proc-parent attribution (frozen manifest), DoT/AoE coalescing
  → CombatEngine accumulators (NEW; replaces CombatAggregator scope)
  → AnalyticalProjection DTOs (NEW)  consumed by Live / Historical / Compare
```

Two consumers feed the accumulators through the **same** interface:
- **Live**: `GameplaySessionManager` streams normalized events as they arrive.
- **Historical**: a reader projects from the aggregate cube and streams the persisted spine through the same engine only for fields declared losslessly replayable or sufficient-for-recompute.

Dedup boundary summary: mirror collapse is decided at the **normalized canonical** stage (post-normalization, pre-aggregation), and only for independently proven message/channel-family pairs on the versioned allowlist. Never at raw-text level, never inside aggregation, never by semantic key, byte offsets, or sequence proximity alone. An empty initial allowlist is valid.

---

## 5. Raw grammar / normalization boundary

**Separate the two stages.**

- **`GrammarMatcher`** owns the regex table and returns a `GrammarMatch { GrammarId, Captures }`. It knows nothing about actors, damage-type semantics, or dedup. It is the only place that knows text shapes. It handles the **pet prefix at this layer**: a single outer rule strips `^(?<entity>[^:]{1,40}):  ` and records `PrefixEntity`, then re-matches the inner grammar. A verified pet prefix gives inner grammars a pet-scoped variant without duplicating their rules; classifier context and fixtures must distinguish it from chat/speaker syntax. `GrammarSetVersion` versions this table.
- **`Normalizer`** turns a `GrammarMatch` + `ParserEvent` provenance into a `CanonicalCombatEvent`: resolves actor/target roles, parses damage type into a `DamageType` value object (multiword, `unresistable`, `Unique`), strips optional `their`, maps "health points"/"hit points"/"granting … endurance" into `EventFamily`, carries an actual source-channel/message discriminator only where available, and records mirror-classification inputs. It never fabricates a source channel from a semantic family. `AnalyticsSemanticVersion` versions normalization decisions.

This boundary is what unblocks: pet lines, incoming "with their", multiword types, the live heal grammar, endurance grants, mez lines, and the distinct pet incoming-roll grammar (`… HITS you! … had a X% chance to hit and rolled a Y`), all as **new grammars + normalization rules** without touching provenance or aggregation.

Routing is part of grammar coverage: `ParserClassifier` may label `X hits/heals you with their …` as `PotentialIdentityEvidence`, while today's combat candidate filter excludes that kind. Relevant lines must retain identity-evidence processing **and** reach combat normalization; do not recategorize them solely to make the combat grammar match.

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
- Explicit power lifecycle (Slice 5A): `You activated the {power} power.`; `{power} is recharged.`; `{power} is still recharging.` Capture the surfaced candidate name. Recharge-shaped lines require independent same-session activation evidence before lifecycle analytics.
- Scorch/env: `{prefix?}The {power} scorches you for {n} points of {type} damage!`
- Keep crit grammar present but **CONDITIONAL**: the repository has a synthetic incoming-critical fixture, not representative real player-crit evidence from the audit.

---

## 6. Canonical combat event model

New record `CanonicalCombatEvent` (superset; `CombatEvent` is retired from the hot path but kept until migration completes):

```
CanonicalCombatEvent
  Provenance          EventProvenance     // §8, required
  Sequence            long                // parser sequence; scoped with source/segment/binding provenance
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
  SourceChannel       string?             // actual source-channel/message discriminator, only if present in the log
  MirrorClass         MirrorClassification // §9: mirror-candidacy metadata (NOT a semantic dedup hash)
  Facets              EventFacets         // valid analytical perspectives, e.g. HealDelivered and HealReceived
  PowerStateTransition PowerStateTransition? // Activated | RechargeCompletedObserved | StillRechargingObserved; activation or unconfirmed candidate
  DuplicateOf         EventOccurrenceRef? // scoped provenance of a surviving logical event, if proven mirrored
```

`CombatEventFamily` (replaces the overloaded `CombatEventKind`): `DamageDealt, DamageReceived, HealDealt, HealReceived, EnduranceGrantDealt, EnduranceGrantReceived, AttackResolution, Activation, Defeat, Mez, Knock, CompanionMissSummary, EnvironmentDamage, Unparsed, RechargeCandidate`.

`Activation` remains the `You activate {power}.` / `You activated the {power} power.` family with `PowerStateTransition.Activated`. With actor `Self`, it independently establishes a surfaced player-power name (`IsPlayerPowerActivationEvidence`); pet activations do not establish player powers.

`RechargeCandidate` represents only the syntax `{name} is recharged.` / `{name} is still recharging.`. Its `PowerName` is unverified surfaced text, its actor is Unknown, and its target is null. `RechargeCompletedObserved` and `StillRechargingObserved` describe the wording, not confirmed power identity, readiness, or blocked-use. Candidates carry only the `RechargeCandidate` facet and `MagnitudeKind.None`; they cannot contribute to lifecycle analytics or legacy `PowerActivation` merely because they parsed. Arbitrary prose such as `The battery is recharged.` remains an unconfirmed candidate and does not fabricate a power.

Slice 5A is stateless and never upgrades candidates, even when an activation has previously been parsed. The later session-aware consumer must require a preceding independent player activation for the same surfaced name in the same gameplay session and compatible source context (ContextId, SourceId/account, source segment, binding generation). Parser sequence is meaningful only within that scope. Source scope alone is not a gameplay-session identifier: a later Welcome can start another session without changing the log source. Evidence must not cross that boundary. Preserve captured names exactly and compare them consistently using ordinal equality, as the existing canonical power comparison does; no casing, trimming, catalog, or build heuristics are introduced. Missing session identity or independent evidence means unconfirmed. Full parser provenance is retained for that future correlation; correlation, accumulators, and interval calculations are not implemented in Slice 5A.

Rationale: today's `CombatEventKind` conflates magnitude semantics (heal vs endurance) with family, and can't express mez/knock/endurance. `DeliveryFlags` collapses the scattered `IsOverTime/WasForced/IsAutohit` booleans and adds Containment/Overpower/Critical. `Magnitude` disambiguates "health points" heals from "endurance" grants that share the `You hit … granting` shape.

`MirrorClass` deliberately replaces the earlier "semantic DedupKey." It carries candidate inputs (actual channel when present, message/grammar family, actor/target identity, canonical power, amount, type, timestamp precision, scoped sequence band) but is **not** itself a merge trigger. Two representations collapse only when a fixture-backed allowlist rule has an independent discriminator for that log form **and** matching semantic/provenance constraints (§9).

`EventFacets` is a minimal logical-event projection of analytically valid perspectives. For a proven mirrored heal, one logical magnitude can support both `HealDelivered(source)` and `HealReceived(target)` facets. The accumulator counts that magnitude once as a physical heal, and applies each valid directional facet once to its corresponding metric; it never creates two independently counted magnitude events. A non-mirrored event normally has one facet. The surviving event retains both source-line provenance references for audit.

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

Pet-prefix normalization, once the prefix is verified as own-pet context: `Imp:  You hit X …` ⇒ Actor=`OwnPet(Imp)`, Target=`Enemy(X)`. `Imp:  Y hits you …` ⇒ Actor=`Enemy(Y)`, Target=`OwnPet(Imp)`. The word "you" inside that verified pet-prefixed line refers to the prefixed pet, not Self — the Normalizer encodes this rule explicitly. A colon prefix alone does not establish ownership. Name rollup ships before confident per-instance splitting.

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
  SourceChannel       string? // actual source-channel/message discriminator if supplied; never inferred as fact
```

In the **persisted event spine**, retained provenance is stored in a **compact, columnar/interned** form (source ids and grammar ids dictionary-encoded per segment) so it does not bloat storage. `ParserSequence` is never a standalone global key: it remains scoped by source, source segment, and binding/generation. Distinct `ByteStart`/`ByteEnd` and nearby sequence values distinguish line occurrences and filter candidates; they do **not** prove a true repeat versus a mirror. `SourceTimestamp` is optional, timezone-unspecified, and sometimes limited in precision; it assists comparison/order where valid but does not universally replace committed parser order or `ObservedAt`. Independent rereads may create different source-segment GUIDs and observation times.

---

## 9. Deduplication architecture

Dedup is deterministic, testable, versioned (`DedupPolicyVersion`), conservative, and occurs **once**, at the canonical stage. Its default posture is **under-merge over data loss**.

### 9.1 Core rule

> Only collapse event pairs/families that are **empirically proven** to be alternate representations of the same logical event, and only when the pair's families appear on the versioned **Mirror Compatibility Allowlist**.

Semantic similarity alone **never** makes two events duplicates. A hash of `(actor, target, power, amount, type, timestampSecond)` is explicitly **not** a merge trigger. Byte offsets and parser-sequence proximity are only candidate filters, not proof of sameness. A mirror rule needs fixture-backed independent evidence in addition to matching semantics and provenance constraints. The initial allowlist may be **empty**; this is a valid implementation state.

### 9.2 Two-phase evaluation

**Phase A — Mirror-candidate classification.**
Each canonical event carries its semantic family and any **actual** source-channel/message discriminator the log supplies. `ParserClassifier` recognizes some channel/speaker structures but currently does not retain the channel in `ParserEvent`; add an optional discriminator at that classifier/grammar boundary, without fabricating one for untagged lines. The `Deduplicator` consults the **Mirror Compatibility Policy** — a versioned allowlist of message/channel-family relationships proven by sanitized fixtures. Only events covered by a proven rule and its independent discriminator are *eligible* for collapse. Everything else passes through untouched.

Candidate relationships for future allowlist entries (each remains **disabled** until a committed sanitized fixture proves the relationship and an independent discriminator for that log form):
- `HealDealt ↔ HealReceived` where one logical heal is surfaced on delivery and receipt channels (Panacea/Transfusion pattern).
- Player/pet mirrored-message pairs where one channel demonstrably restates the other for the same logical hit.
- `EnduranceGrantDealt ↔ EnduranceGrantReceived`, if proven.

Anything not on the allowlist (including `DamageDealt ↔ DamageDealt`) is **never** eligible — so two identical damage ticks, or two identical pet hits, in the same second are structurally ineligible to merge and remain two events.

**Phase B — Same-logical-event decision (only for eligible pairs).**
For an allowlisted candidate pair, collapse occurs only when independent mirror evidence + provenance + semantics jointly establish one logical event:
- message/channel families form a **fixture-proven enabled** mirror rule with its required independent discriminator, **and**
- actor/target identities are the mirror-consistent counterparts, **and**
- canonical power + amount (hundredths) + damage type + magnitude match, **and**
- timestamps match at the precision actually available for that log form, **and**
- scoped `ParserSequence` values fall within the policy's narrow mirror band **and** `ByteStart` ranges identify distinct log lines. These last constraints filter candidates; they are not independent proof.

If any condition is unmet, **keep both** events and record a coverage note. For a proven pair, choose the earliest occurrence in scoped source order as the surviving logical event; link the other by scoped occurrence provenance. The survivor's physical magnitude is unchanged and its `EventFacets` are the union of valid directional perspectives from both representations. Thus a mirrored heal contributes once to the logical heal magnitude and once to each proven delivered/received perspective, never as two independently counted magnitudes.

### 9.3 What is explicitly forbidden

- No merging by semantic key, byte-offset distinction, or sequence proximity alone.
- No merging across non-allowlisted families.
- No `RepeatCount` on the survivor to "absorb" a suppressed event. `RepeatCount` is **removed from dedup entirely** and does not exist as a mirror-merge artifact. (It survives only as an explicit, validated **analytical coalescing** structure for DoT chains — §10 — where the coalesced rows are proven ticks of one power, never a dedup guess.)
- No collapse of true repeated identical events.

### 9.4 Versioning

`DedupPolicyVersion` governs: the allowlist membership (possibly empty at v1), required independent discriminators, mirror band width, actor/target counterpart rules, facet union, and survivor selection. Any change bumps the version, which is stamped on the segment so Compare never mixes policies silently. Families are added only as fixtures prove them.

Output: a **conservatively deduped canonical stream** plus per-family diagnostics: `MirrorCollapsedCount` (proven merges) and `MirrorCandidatesHeldCount` (eligible pairs that failed Phase B and were kept separate) — both surfaced as coverage (§13, §25).

---

## 10. Enrichment architecture

Enricher runs after dedup, before aggregation. Responsibilities:

1. **Actor classification** — resolve `ActorType` refinements and `PetClassification` using the frozen build manifest (§11) + reference catalog + heuristics; attach confidence.
2. **Proc-parent attribution** — §11 (four-mode).
3. **Validated coalescing** — mark DoT tick chains and AoE fan-outs only where event identity, ordering, and available timestamp precision support that relationship; same power and timestamp second alone do not prove one activation. Coalesced rows may carry `TickCount` and current sufficient statistics for a verified chain, but cannot imply arbitrary future per-tick or activation reconstruction. This is analytical compression, not a dedup fallback, and it never hides mirror uncertainty.
4. **Canonical power identity** — map surfaced `PowerName` to a catalog power id only where an ambiguity-aware reverse resolver can establish it, keeping the raw string too. Existing `IHomecomingPowerReferenceCatalog.TryResolve` resolves raw category/powerset/power identity, **not** arbitrary surfaced log display names. The new reverse resolver may use the frozen build/catalog context but never guesses through a display-name collision.

Enrichment is **pure over (event + frozen manifest + catalog snapshot)**; it never reads the mutable latest build at historical replay time.

---

## 11. Build / proc correlation architecture

**Attribution model (four modes):** `PowerAttribution { Mode, ParentPowerId?, Candidates?, Evidence?, Confidence, PolicyVersion }` with `Mode ∈ { Direct, BuildConfirmed, Correlated, Unattributed }`.

- **Direct** — the log telemetry itself identifies the parent power for that occurrence (normal attack: `You hit … with your Fire Ball …`). No build needed. Highest confidence.
- **BuildConfirmed** — the log identifies the **exact proc/effect identity** (e.g. `Armageddon: Chance for Fire Damage`) **and** a validated mapping in the **frozen captured build** contains **exactly one matching slot occurrence** of that exact proc, which resolves to **exactly one** parent power (e.g. `Hot Feet`). The parent is then **deterministically resolved from the captured build** — no timing heuristic, no ambiguity. This is stronger than heuristic correlation and weaker than Direct (the log did not state the parent; the frozen build proved it).
  - Uniqueness is judged over individual **slot occurrences in the captured build**, not distinct parent powers or any game-wide uniqueness rule. Two matching slots in one parent power fail BuildConfirmed.
  - **Not** BuildConfirmed if any of the downgrade conditions below hold.
- **Correlated** — more than one possible parent exists in the frozen build (or evidence is otherwise non-deterministic), but deterministic contextual evidence (activation context, timing, co-occurrence) favors one candidate. Must retain `Candidates[]` and `Evidence` and a graded `Confidence`. Correlation is never promoted to BuildConfirmed.
- **Unattributed** — no defensible unique or correlated parent. The parent is unknown; the design does not guess.

**Immutability of the frozen manifest.** A frozen build manifest **never becomes stale after capture**. It is, by definition, the immutable record of the build context bound to the segment at capture time, and it remains the historical source of truth for build-based attribution for the life of the segment. There is no notion of a frozen manifest "expiring" or "drifting" once written.

**BuildConfirmed downgrade conditions.** `BuildConfirmed` is withheld (attribution falls to `Correlated` or `Unattributed` as the evidence allows) when any of the following holds:
1. **No frozen build manifest available** — the segment has no manifest bound to it (the manifest reference is null because no build was synced/available at capture).
2. **Capture-time build was unsynced or out-of-date** — the underlying synced build was already out-of-date relative to the character at capture time (the user changed slotting/powers and did not resync before the segment was captured), **and** the engine can detect or establish that condition. This is a capture-time property, not a property of the frozen manifest degrading afterward. No detection mechanism is mandated here; if the engine cannot establish this condition, it does not fabricate one — but where it can, BuildConfirmed must not be asserted.
3. **Proc evidence is ambiguous** — the exact proc has more than one matching slot occurrence in the frozen manifest, even if both are in the same power.
4. **Proc cannot be matched** — the proc/effect identity cannot be validated against a slotted occurrence and catalog enhancement, or the raw-token/catalog mapping collides or is incomplete.

**Incarnate / global effects** (`Reactive Interface`, `Doublehit`, `Particle Burst`, and similar) use **separate attribution semantics**: a validated source-specific mapping may identify an Incarnate/global slot rather than an attack power. The current build snapshot has no reliable generic global/Incarnate effect classification, so these default to `Unattributed` on the separate `GlobalEffectSource` path until such a mapping is proven. They are **excluded** from per-attack proc-parent contribution so they never inflate a specific power's proc share.

**Proc totals remain reliable independent of parentage.** Proc damage totals and proc contribution % of overall damage are computed from the (deduped) proc events themselves and can be `Available`/RELIABLE even when every parent is `Unattributed`. Only **proc-by-parent-power** attribution depends on the attribution mode and carries its confidence.

**Historical binding.** A mutable latest build is insufficient. The current `CharacterBuildSnapshot` stores raw enhancement tokens, each slot occurrence and parent raw power, attuned flag, sync time, source file, and source write time; it does **not** directly store `ExactProcIdentity`, canonical enhancement IDs, or reliable generic global/Incarnate classification. An engine-side, collision-aware, alias-aware, coverage-aware resolver must map raw enhancement token → validated catalog variant/item → canonical enhancement identity → exact logged proc/effect identity **where known**. Unknown or ambiguous mappings remain Unattributed. Each segment then **freezes the resolved mapping facts in a compact, content-addressed build/proc manifest** — the historical source of truth for build-based attribution, without depending on a mutable future catalog:

```
FrozenBuildManifest
  ManifestHash        string   // sha256 of normalized semantic manifest content only
  BuildCatalogFingerprint string? // source/catalog identity used to resolve mapping facts
  Powers[]  { CanonicalPowerId, RawPowerToken, PowerSetToken, Category }
  ProcSlots[] { SlotOccurrenceKey, EnhancementToken, CanonicalEnhancementId?, ExactProcIdentity?, SlottedInPowerId?, IsAttuned, IsGlobalOrIncarnate?, MappingStatus, MappingEvidence }
```

`SlotOccurrenceKey` preserves source power and slot order; count matching **occurrences**, not distinct parent IDs. `ExactProcIdentity` is the log-comparable identity only when a validated mapping exists. The repository's live build fixture has `Crafted_Armageddon_F` once under `Hot_Feet`, and the item catalog maps that variant to `Armageddon: Chance for Fire Damage`: this is a supported BuildConfirmed example once log-name matching is validated. Do not generalize it to unknown tokens or global/Incarnate effects. Content-addressing dedupes identical semantic manifests in `builds/manifests/{hash}.json`; calculate the hash from normalized build/mapping **content only**, excluding `ManifestHash` itself and capture-specific source path, source write time, sync/capture time, and record ownership. Those provenance values belong in segment capture metadata, so the blob for an identical semantic build does not acquire a conflicting capture timestamp. The segment binds the resolved facts and a durable hash/blob; historical replay never consults the mutable latest build or future catalog to recreate that decision. A missing or hash-mismatched blob makes build-derived attribution unavailable/incomplete, not guessed. If no build was synced at capture, the manifest reference is null and proc parentage is `Unattributed` — never guessed, never BuildConfirmed.

`AttributionPolicyVersion` governs BuildConfirmed matching rules (including how a detectable capture-time unsynced/out-of-date condition suppresses BuildConfirmed), global/Incarnate handling, correlation thresholds, and confidence grading.

---

## 12. Analytical accumulator / domain model

**One accumulator model for live and historical.** `CombatEngine` consumes `CanonicalCombatEvent` and maintains a set of dimensioned accumulators:

- **Scalar totals** per family (damage dealt/received, healing, endurance granted, defeats), with directional heal delivered/received views derived once each from valid logical-event facets rather than independent duplicate magnitudes.
- **Per-power cube**: key `(Scope, CanonicalPowerId|RawName, AttributionMode)` → { total, hits, ticks, directVsDoT split, type breakdown, largestHit, targetsSeen }.
- **Per-type**: `DamageType → total`.
- **Per-target**: `NormalizedTargetName → damageTotal` (cardinality-bounded, §26).
- **Per-pet**: `NormalizedPetName → { damage, dps-basis, heals, damageTaken, accuracy }` (+ optional instance split).
- **Accuracy**: retains the existing `CombatAccuracyAccumulator` semantics (rolled attempts, forced, autohit-tracked-separately) — this logic is correct and is **reused**.
- **Proc**: two independent views — (a) `proc total` and `proc contribution %` for events whose proc identity is validated (parent-independent); (b) `(ParentPowerId|BuildConfirmed|Correlated|Unattributed) → procDamage` with attribution mode carried through. Proc identification from raw logged names/catalog mappings is a prerequisite for (a); parent attribution can follow in Slice 8.
- **Mez/knock counts**: per family + per power.
- **Power lifecycle observations**: `CombatEngine` confirms recharge candidates only after an independent same-session player activation of the same surfaced name (`StringComparison.Ordinal`) with compatible source context (`ContextId`, `SourceId`/account, `SourceSegmentId`, `BindingGeneration`, and an earlier scoped parser sequence). Unmatched recharge candidates contribute no power metrics. Observed activation→recharge-complete and recharge-complete→next-activation intervals are deferred. No theoretical cooldown math is inferred.
- **Rolling windows**: retain `RollingCombatAccumulator` concept for live.

`Scope ∈ { Self, OwnPetsAggregate, PerPet }` so "player vs pet" is a first-class split rather than a filter.

Each accumulator emits an **immutable projection DTO** (`CombatAnalyticsProjection`) — POCO, no WPF/HTML. Live and Historical both produce this identical DTO; Compare consumes two of them. This is the "engine owns formulas" guarantee.

The engine consumes **deduplicated logical events**. A proven mirror union keeps one physical magnitude and all analytically valid `EventFacets`; e.g. one heal can update source-delivered and target-received views once each, while a combined healing total counts it once. The same rule applies to segment aggregates and spine replay.

Bounded per-dimension cardinality with an explicit `Other`/overflow bucket and a `CoverageLimited` flag when overflow occurs.

---

## 13. Metric availability / confidence model

A metric is not a bare number. Introduce a small generic:

```
Metric<T>
  Value        T?
  Availability MetricAvailability  // Available | NotCaptured | Unsupported | Incomplete
  Confidence   MetricConfidence?   // High | Medium | Low   (only when inferred)
  Coverage     CoverageInfo?       // optional: sampled %, overflow, mirror-candidates-held, replay capability
```

- `Available` — computed from sufficient evidence.
- `NotCaptured` — this segment's schema/coverage never recorded the inputs (old segment, disabled channel). **Never rendered as 0.**
- `Unsupported` — the game/log cannot express it (overkill, buff uptime, mez duration).
- `Incomplete` / `CoverageLimited` — partial evidence (cardinality overflow, sampled spine, low-confidence attribution, mirror candidates held apart under uncertainty).

The segment `CoverageDescriptor` includes a **versioned Lossless Replay Coverage Matrix**: for each event family/metric field, declare `Lossless`, `SufficientForRecompute` (with the exact supported statistics/semantics), or `Lossy/NotRecomputable`. This is separate from value availability: an aggregate can be `Available` while its sampled spine is `NotRecomputable`. Historical readers expose incomplete evidence or `NotRecomputable` capability when a requested recalculation lacks sufficient spine evidence. They never replace an authoritative aggregate with a lossy replay result.

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
  ActiveDuration?                   // NEW: sum of engaged intervals, when captured from full stream
  RateDenominatorPolicy             // versioned per-rate semantic: WallClock | TrackedPauseAdjusted | Active | RollingWindow
  IdleThresholdSeconds              // from CombatActivityDefaults, recorded
```

- **Event timestamp**: preserve committed parser order and `ObservedAt` for arrival/session timing. Optional, timezone-unspecified `SourceTimestamp` may assist ordering or bucketing when its actual precision is sufficient; it does not universally replace committed order. Persist both when available.
- **Rate denominator**: each rate (DPS, dmg/min, heal/min, XP/hr) declares its scope and denominator semantic. Current 0.1.3 session DPS/XP/Influence use wall elapsed, tracked DPS excludes manual pauses, and rolling uses bounded buckets. Preserve those as compatibility outputs. `ActiveDuration` is a new accumulated interval metric computed from the full committed stream before spine sampling; active-denominator rates are new/versioned outputs, not reinterpretations of old 0.1.3 values. Compare refuses unlike denominators unless **both fields** have sufficient replay coverage to normalize to a common policy.
- **Rolling windows**: live-only presentation concept; not persisted as historical truth.
- Manual tracked pauses and inferred combat-idle gaps are distinct. Active intervals may be computed deterministically from the full committed event stream under a recorded idle threshold; a sampled spine does not automatically reproduce them.

This makes rate semantics explicit without silently changing today's session, tracked, or rolling values.

---

## 15. Durable segment model

A segment is a **durable analytical capture**. Hybrid (Option D) with clearly separated layers and two analytical authorities:

```
Segment (on disk = a bounded document set in one directory)
  1. CaptureMetadata          (immutable)      §16
  2. SegmentClock             (immutable)      §14
  3. CoverageDescriptor       (immutable)      captured families/dims, actual channel evidence where available, semantic versions, mirror diagnostics, versioned Lossless Replay Coverage Matrix
  4. FrozenBuildManifest ref  (immutable)      §11 (content-addressed, may be null)
  5. AggregateCube            (immutable)      §12 outputs, authoritative for persisted metrics/sufficient statistics not losslessly retained in spine
  6. NormalizedEventSpine     (immutable)      bounded canonical evidence, authoritative only for fields declared Lossless or SufficientForRecompute, §17
  7. Annotations              (MUTABLE)        §16 (DisplayName, IsBeta, IncludeInOverview)
```

Layers 1–6 are written once and never rewritten. The complete immutable file set is validated and **published as a unit** (§17); single-file atomic move is not enough. Layer 7 is a **separate small sidecar file** so renaming/annotating never rewrites the large payload.

---

## 16. Segment metadata and annotations

**Immutable capture metadata** (persisted; duration is derived from stored bounds):
`CharacterRecordId`, `AccountStableId`, `AccountDisplayNameAtCapture`, `CharacterDisplayNameAtCapture`, `LevelAtCapture`, `Archetype`, `PrimaryPowerSet`, `SecondaryPowerSet`, `CaptureStartUtc`, `CaptureEndUtc`, `AppVersion` (provenance only), `SegmentSchemaVersion`, `AnalyticsSemanticVersion`, `GrammarSetVersion`, `DedupPolicyVersion`, `AttributionPolicyVersion`, `BuildManifestHash?`, `BuildSyncedAtUtc?`, `SourceBuildFileAtCapture?`, `SourceLastWriteUtcAtCapture?`, `GameplaySessionId`, `SegmentOrdinal`. `Duration` is `CaptureEndUtc - CaptureStartUtc`, never replaced by `UserDisplayName`.

**Mutable annotations** (sidecar, immutable metadata untouched):
`UserDisplayName?`, `IsBeta` (simple boolean — no shard/environment abstraction), `IncludeInOverview`, free-text note (optional).

Identity rule enforced: `AccountStableId + CharacterRecordId` is the local capture identity; `CharacterDisplayNameAtCapture` is descriptive and frozen at capture. `adelbert/Hell's Vengence` and `RivenForest/Hell's Vengence` remain distinct by `AccountStableId`. Renaming a segment edits only the sidecar. `AccountStableId` currently hashes the normalized account folder path; it is machine/path-derived and not guaranteed portable after folder moves or across machines.

---

## 17. Persistence format recommendation

**Hybrid, bounded.** Do **not** copy raw chat logs. Do **not** persist aggregates-only.

- **AggregateCube** — JSON (camelCase), the primary read path and authority for persisted metrics/sufficient statistics not losslessly retained in the spine. Small, bounded by per-dimension caps. Powers Historical/Compare quickly.
- **NormalizedEventSpine** — a **bounded, potentially down-sampled/coalesced, columnar, compressed** canonical evidence stream. Its authority is limited by the versioned Lossless Replay Coverage Matrix in `coverage.json`:
  - Retain selected high-value families losslessly where feasible (e.g. proc events and both members of held mirror-candidate pairs if later dedup re-evaluation is promised). Whether high-volume incoming hits can be lossless is a measured decision, not an assumption.
  - For high-frequency DoT ticks / AoE fan-out, validated coalesced records may retain sufficient statistics (power, target, tickCount, sumAmount, min/max, window) for **declared current fields**. They do not preserve arbitrary per-tick timing, activation linkage, or future semantics merely because totals survive. Coalescing is analytical compression, never a dedup guess.
  - If a family is sampled, its exact future recompute is `NotRecomputable`. Keep aggregate totals authoritative. Dictionary-encode strings and compress the payload; set size limits only after representative max-telemetry measurements and declare the resulting coverage.
- **File layout** under `%LocalAppData%\CoH Analytics\Characters\Segments\{gameplaySessionId}_{ordinal:D10}\`:
  - `metadata.json`, `coverage.json`, `aggregates.json`, `spine.bin` (compressed), `annotations.json` (mutable sidecar).
  - Content-addressed manifests in shared `builds/manifests/{hash}.json`.
- **Atomic publication**: reuse the observation repository's single-file temp write, `flushToDisk`, deterministic naming, duplicate/conflict checks, and malformed reporting/skipping patterns. For a segment, first durably publish and hash-validate any external content-addressed manifest blob; write immutable segment files into a staging directory; validate required files and hashes; then atomically rename the staging directory to its final deterministic path (or use an equivalent validated commit-marker protocol). Readers ignore incomplete/unpublished staging directories. A repeated final capture key is a duplicate only when immutable content agrees; otherwise it is a conflict.
- **Corruption/recovery**: validate each file and its declared hash. A missing/hash-mismatched manifest leaves build-derived attribution unavailable/incomplete while independently valid aggregates remain readable. Bad annotations fall back to safe defaults with a reported error; never rewrite immutable capture facts. A bad spine degrades to aggregate-only with replay `NotRecomputable`. A bad aggregate file may be rebuilt **only for fields the intact spine's coverage matrix declares sufficient**; otherwise those fields are unavailable/incomplete, not guessed. Staging or partially published directories are ignored/reported.

**Intentionally NOT retained:** raw chat text, unbounded per-tick DoT rows, unbounded per-target rows beyond cap, rolling-window state, live session scaffolding, presentation state, any dedup-merge counter. This limits future recomputation by design.

---

## 18. Versioning and semantic compatibility

Compatibility is **multi-axis**; app version is never the gate.

| Axis | Governs | Effect on compat |
|---|---|---|
| `SegmentSchemaVersion` | file/field layout | reader migrates layout in memory |
| `AnalyticsSemanticVersion` | metric definitions | Compare gates on equality/known-mapping |
| `GrammarSetVersion` | grammar table | normalized spine cannot reconstruct raw text that was not retained; only declared sufficient evidence permits a supported re-projection |
| `DedupPolicyVersion` | mirror allowlist + independent discriminator + band + survivor/facet rules | Compare refuses cross-policy fields unless both have sufficient replay coverage for normalization |
| `AttributionPolicyVersion` | Direct/BuildConfirmed/Correlated/Unattributed rules, global handling, thresholds | affects proc-by-parent metrics' mode/confidence only |
| `RateDenominatorPolicy` | rate math | Compare normalizes or blocks |

Rule: a reader **declares a support matrix** mapping (semanticVersion, families) → supported metrics and consults the segment's versioned **Lossless Replay Coverage Matrix** per field/family. Opening an older segment yields `Available` only for metrics its versions and persisted evidence support; everything else is `NotCaptured`/`Unsupported`/`Incomplete`, never false zero. Newer numbers are derived from the spine **only if** its retained evidence is sufficient for that precise calculation. Retaining held mirror candidates permits later policy re-evaluation **for that pair/family only when their discriminator, facets, and other required evidence were retained losslessly**; it cannot undo discarded detail or an earlier unsafe merge. Compare cannot assume policy mismatch is universally normalizable.

---

## 19. Historical replay / recalculation strategy

- **Primary read path**: load `aggregates.json` → project to DTO. O(cube size). The cube is authoritative wherever spine evidence is lossy.
- **Recompute path**: consult the versioned Lossless Replay Coverage Matrix first. Replay `spine.bin` through the same analytical engine **only for fields/families declared Lossless or SufficientForRecompute for the requested semantic/policy change**. Full proc-event retention can permit a supported proc-total recalculation; coalesced DoT statistics may permit current totals but not arbitrary future per-tick semantics. Held mirror candidates can be reconsidered only if their actual independent discriminator and both representations survived. Parser grammar changes cannot be replayed from normalized events when necessary raw text was discarded. This is **non-destructive** — on-disk segments are not rewritten; optional user-initiated re-bake writes a new version alongside.
- **Aggregate-only fallback**: if spine is absent/corrupt/lossy for a request, serve intact authoritative aggregates; expose `NotRecomputable` for that request and `Incomplete` only where required evidence or aggregate fields are actually missing.
- Determinism invariant: the same **persisted** spine, pinned policy versions, and frozen resolved mapping facts yield the same supported semantic projections. Exact spine→cube equality is asserted **field by field only for declared lossless/sufficient fields**, not universally and not as byte-identical output across independent log rereads.

---

## 20. Cross-account / stable-character identity handling

Reuse existing identity substrate:
- `CharacterRecord` (`RecordId`, `AccountStableId`, `NormalizedCharacterName`, `CurrentDisplayName`, `Aliases`) is the canonical identity; keep it.
- Segments group by `(AccountStableId, CharacterRecordId)` and identify an individual capture by `(GameplaySessionId, SegmentOrdinal)`; display name is frozen-at-capture descriptive metadata only. Account ID is a normalized account-folder-path hash suitable for local identity, not guaranteed portable after a folder move or to another machine.
- Same display name on different accounts ⇒ different `AccountStableId` ⇒ distinct. Rename ⇒ same `RecordId`, alias appended; historical segments keep their captured name.
- Historical/Compare enumerate segments across **all** accounts/characters known on this machine by scanning the `Segments/` root plus legacy observations, grouping by captured identity, and honoring `CharacterRepository` canonical/retired record-ID resolution. Resolve current display where available while showing captured display where relevant; do not invent cross-machine reconciliation.

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
- **Raw totals vs normalized rates** are distinct (§14): totals compare directly only when durations equal; otherwise engine compares **normalized rates** and marks totals `Incompatible` unless the user explicitly wants raw. Each rate carries its actual session-wall, tracked-pause-adjusted, active, or rolling denominator semantic.
- **Zero-baseline**: `PercentDelta` from a zero left is `Unavailable` (not ∞); absolute delta still shown.
- **Denominator/semantic/dedup/attribution mismatch**: gate **per field** on semantic version, denominator, dedup policy, attribution policy, and both segments' replay coverage. Return `Incompatible` with reason, or `Normalized` only when each segment retains sufficient lossless evidence to compute that field under a common policy. No universal normalization from a sampled spine.
- `NotCaptured` on either side ⇒ `Unavailable` (never treated as 0 difference).

Compare is arbitrary-segment: same char before/after build change (frozen manifest hash differs — surfaced), two chars, cross-account same-name, cross-AT, Live-vs-Beta (`IsBeta` shown, never blocks).

---

## 22. Supported metric classification

**RELIABLE** (Direct log evidence, player-scope):
- total damage, DPS, damage/min, damage by power, per-power DPS, hits, misses, hit rate, attack roll/chance, largest hit, DoT vs direct split, damage-type mix, damage by target / top targets, activations, defeats (player `You have defeated`), defeats/min, XP/hr, Influence/hr.
- **proc damage total** and **proc contribution %** (parent-independent).
- direct player activation count. Recharge-completion counts, still-recharging observations, and observed activation/recharge intervals are Available only for confirmed lifecycle sequences with independent same-session player-power evidence. Unmatched recharge-shaped observations do not establish a power or measured availability; they do not contribute to these metrics. Confirmation and interval calculation are deferred beyond Slice 5A.

**CONDITIONALLY RELIABLE** (needs conservative dedup, new grammar, enrichment, or attribution; flagged with confidence/coverage):
- healing given/received, healing/min, healing by power (needs live "health points" grammar, conservative mirror policy, and directional logical-event facets; an empty allowlist leaves potential mirrored coverage flagged rather than merging without proof); incoming damage total/by source/by enemy power/by type, avg & largest incoming hit (needs `their`/multiword + mirror handling); average damage per activation (needs validated AoE/DoT coalescing); pet damage, pet DPS, pet contribution %, pet healing, pet damage taken, per-pet contribution, pet hit/miss/accuracy (needs pet-prefix parsing + conservative instance/name rollup); **proc damage by parent power** (Direct/BuildConfirmed ⇒ High; Correlated ⇒ Medium/Low; Unattributed ⇒ Incomplete for that bucket); mez event counts (counts only); enemy hit/miss/roll where surfaced; permanent-vs-Lore/temp classification (confidence-graded).

**UNSUPPORTED** (no log/game evidence — do not build):
- overkill, target HP reconstruction, full encounter reconstruction, exact mez duration, buff uptime, debuff uptime, server-hidden state, own-pet defeat attribution. The repository has a synthetic incoming-critical grammar/fixture, but that does not establish representative player-crit analytics; crit metrics remain **UNSUPPORTED until representative real evidence exists**.

---

## 23. Backward compatibility with existing saved observations

- `CharacterPerformanceObservation` v1/v2 files are **retained and readable**. Provide a `LegacyObservationAdapter` that maps an old observation into the new `SegmentProjection` shape: damage dealt, accuracy, defeats, XP, inf ⇒ `Available`; **everything else ⇒ `NotCaptured`** (healing, incoming, pets, procs, types). No zero-filling.
- Old observations have no spine or build manifest and no precise channel-coverage descriptor ⇒ recompute `NotRecomputable`, aggregate-only with legacy/unknown coverage; proc parentage `NotCaptured` (never Unattributed-vs-BuildConfirmed, since no manifest existed). They remain usable in Overview/Historical at real fidelity and comparable against new segments on fields with compatible legacy semantics and denominator, with `Unavailable` on missing axes.
- The existing `PerformanceObservations/` directory and repository stay; new segments live under `Segments/`. No destructive migration. Overview/Historical enumerate both stores with **one visible capture per `(GameplaySessionId, SegmentOrdinal)`**: if both stores contain the same logical capture, prefer the validated new segment; fall back to the legacy observation if the new segment is unpublished/invalid, and report conflicting immutable identity/content rather than silently adding both.

---

## 24. Test / replay-fixture architecture

- **Sanitized immutable fixtures** derived from the real max-telemetry log, checked into the test project (never referencing the user's live path). Sanitization: replace character/account/teammate names with stable pseudonyms via a deterministic map, keep power names, types, amounts, timestamps, source order, and actual channel/prefix syntax intact. Store under `CoHAnalytics.Tests/Fixtures/Combat/` and add that path to the test project's fixture-copy rules when implemented; current copy rules cover `Replay/Fixtures` and `Services/Fixtures`.
- **Fixture families** (each a focused file + expected canonical/aggregate JSON): player damage, incoming (`their`+multiword `unresistable Unique`), live "health points" heals (delivered+received mirror), hit/miss/forced/autohit, DoT chains, activations, power lifecycle (`You activated the {power} power.`, `{power} is recharged.`, `{power} is still recharging.`, multiword names, repeated recharge lines remaining distinct), permanent pet (`Imp:`), Lore pet (`Ravager/Defiler Essence:`), pseudopet (`Enervating Storm:`), pet incoming damage, pet incoming roll grammar, pet healing, procs (`Armageddon: Chance…`, `Panacea…`, `Reactive Interface`, `Doublehit`), mez/knock/OVERPOWER, endurance grants, `{power} missed!`, malformed lines, unknown/future lines, streakbreaker, autohit.

- **Attribution fixtures (four-mode):**
  1. Direct — `You hit … with your Fire Ball …` ⇒ `Mode=Direct`, parent=Fire Ball, no manifest needed.
  2. BuildConfirmed — `Armageddon: Chance for Fire Damage` + fixture manifest where that exact proc is slotted **once**, in Hot Feet ⇒ `Mode=BuildConfirmed`, parent=Hot Feet.
  3. Same proc in **multiple slot occurrences**, even twice in one power, ⇒ **NOT** BuildConfirmed; `Mode=Correlated` (with candidates) or `Unattributed` per evidence.
  4. **No frozen build manifest available** (null manifest reference) ⇒ `Mode=Unattributed`; proc **total** still `Available`.
  5. **Capture-time build unsynced/out-of-date** — a fixture in which an establishable indication exists that the synced build was out-of-date relative to the character at capture time ⇒ BuildConfirmed **withheld** (downgrades to `Correlated`/`Unattributed`); the frozen manifest itself is still treated as immutable and never described as "stale."
  6. **Proc cannot be matched** to the frozen manifest/catalog ⇒ `Mode=Unattributed`.
  7. Incarnate/global (`Reactive Interface`) ⇒ separate global-effect path, default `Unattributed` until a validated source-specific mapping exists, excluded from per-attack proc-parent share.
  8. Unattributed parent ⇒ proc contribution % still RELIABLE.
  9. Build/catalog mapping: `Crafted_Armageddon_F` under `Hot_Feet` → validated catalog variant/item → `Armageddon: Chance for Fire Damage`; attuned/superior variants, raw-token collisions/aliases, unknown token, and exact log-name mismatch all have explicit outcomes.
  10. Identical normalized manifest content captured at different times has the same hash; changing a resolved mapping fact changes it. Historical attribution survives catalog changes without re-resolution.

- **Dedup fixtures (conservative allowlist):**
  1. Empty initial allowlist retains all candidate representations and flags uncertainty where identifiable.
  2. A fixture-proven **heal mirror**, when enabled with its independent discriminator, collapses to one logical magnitude with both valid delivered/received facets; each directional view updates once.
  3. A fixture-proven **pet/player mirror**, when enabled with its independent discriminator, collapses to one logical event with all valid facets.
  4. **Two identical real damage ticks** in the same second remain **TWO** events.
  5. **Two identical pet hits** in the same second remain **TWO** events.
  6. **Same semantic shape from a non-allowlisted family** remains **separate**.
  7. **Ambiguity favors under-merging** — an allowlisted pair missing any independent proof or Phase-B constraint is kept as two events and flagged `MirrorCandidatesHeld`.
  8. No-`RepeatCount`-merge invariant — assert dedup never emits a survivor with an absorbed-duplicate count.

- **Golden tests**: grammar match table; normalization; a `PotentialIdentityEvidence` line that remains identity evidence **and** reaches combat normalization; dedup phases A/B and directional facets; attribution four modes incl. duplicate slot occurrence, no-manifest, capture-time-unsynced downgrade, and unresolved global; accumulator projections; `SegmentClock` session-wall/tracked-pause/rolling/active distinctions; `Metric<T>` availability (NotCaptured ≠ 0); unequal-duration Compare; cross-account same-name distinctness; legacy v1/v2 adapter; atomic multi-file publication and corruption fallback; field-level replay determinism **only where the lossless coverage matrix warrants it**, including held-mirror re-evaluation only when both representations and discriminator survive.
- Preserve the existing test suite; new engine lands behind new types so current suites stay green during migration. Do not use an unverified test count as acceptance.

### Required pre-Slice-1 checkpoints

Before production implementation, establish: (1) a clean baseline for parser, replay, accuracy, session, historical-observation, and build-layout tests; (2) a legacy `CombatEvent` equivalence oracle covering successful and failed parses, `IsCombatShapedUnparsed`, `CompanionMissSummary`, `PotentialIdentityEvidence` combat candidates, autohit, grammar precedence, and timestamps; (3) sanitized max-channel fixtures retaining channel/prefix syntax and source order, candidate mirrors, and legitimate identical-repeat negative cases; (4) a classifier-envelope fixture proving whether an actual channel exists for each relevant form and where it is currently lost; (5) build/catalog mapping fixtures for `Crafted_Armageddon_F`, attuned/superior variants, duplicate slot occurrences, unknown token, and unresolved Incarnate/global; (6) the versioned Lossless Replay Coverage Matrix definition; and (7) a representative segment-size measurement plan. Do not hard-code a single-digit-MB acceptance target before measurement.

---

## 25. Diagnostics / failure observability

- Reuse existing JSONL diagnostics (`ApplicationDataPaths.GetLogsRoot`).
- Per-segment **CoverageDescriptor** doubles as diagnostics: matched/unmatched line counts, `IsCombatShapedUnparsed` rate, actual channel evidence where present (not inferred), **`MirrorCollapsedCount`** and **`MirrorCandidatesHeldCount`** per family, directional-facet merge counts, attribution mode histogram (Direct/BuildConfirmed/Correlated/Unattributed), cardinality-overflow flags, and the versioned Lossless Replay Coverage Matrix.
- **Held-mirror visibility**: because ambiguity favors under-merging, held candidates are explicitly counted so a human can tune the allowlist/band later; they are never silently merged.
- **Attribution coverage visibility**: where the engine can establish a capture-time unsynced/out-of-date build condition, that establishment is recorded as coverage on the segment so consumers understand why BuildConfirmed was withheld. Absence of such a signal is never treated as proof the build was current.
- **Unparsed capture**: combat-shaped-but-unparsed lines are counted and a bounded sanitized sample retained to drive future grammar work.
- Determinism canary: a hash of semantic canonical output from the **same persisted provenance fixture**, or a documented digest excluding/normalizing runtime-generated GUIDs and observation times, asserted in CI. Essential channel/fixture/coverage diagnostics are built with their dependent Slices 2–5 and 9; Slice 12 hardens the pipeline.

---

## 26. Performance / storage considerations

- **Hot path**: grammar matching dominates. Keep compiled `GeneratedRegex`; add the single outer pet-prefix strip to avoid doubling the table. Match order by observed frequency (damage dealt dominates).
- **Validated coalescing** of DoT/AoE caps spine size, but only declared sufficient statistics remain exactly replayable; single powers can emit thousands of ticks.
- **Conservative dedup is cheap**: Phase A is a family-membership check; Phase B runs only on allowlisted candidates within a narrow sequence/timestamp band — no global O(n²) semantic hashing.
- **Cardinality caps** on per-target/per-pet with `Other` overflow bucket + `CoverageLimited`.
- **Spine**: columnar + dictionary + Deflate; measure long max-channel farms before setting a size target. Held mirror candidates and high-volume families may materially affect size; cap with explicit replay coverage loss, never silently.
- **Live memory**: full committed stream feeds bounded accumulators; the separate 1024-event tail is for retention only. Do not hold the whole session in RAM.
- **IO**: staged multi-file publication once per immutable segment, with durable manifest dependency; annotations sidecar avoids large rewrites.

---

## 27. Risks and edge cases

- **Critical — over-merging in dedup** — reduced, not eliminated, by an empty-by-default/fixtures-proven allowlist, actual independent discriminator, matching semantic/provenance constraints, and prohibition on semantic-key-only merges. Distinct offsets or nearby sequence are not proof. No `RepeatCount` disguises a bad merge. Under-merging is accepted; held candidates are re-evaluable later only where spine coverage is lossless for that evidence.
- **Critical — sampled-spine overclaim** — an aggregate can remain authoritative while its spine is `NotRecomputable`; never claim exact full-cube replay after discarded detail. Test the field-level lossless matrix.
- **High — directional facet loss** — a proven HealDealt/HealReceived mirror must preserve both valid analytical perspectives while counting one physical magnitude once.
- **Legitimate identical repeats** — structurally protected: `DamageDealt↔DamageDealt` is not allowlisted, so two identical same-second ticks/pet hits stay separate by construction (fixtures 3–4).
- **High — false BuildConfirmed** — controlled by validated exact proc→catalog variant mapping, exactly one **slot occurrence** in the frozen build, validated non-global source, and no established capture-time unsynced/out-of-date condition. Raw-token collisions, same-power duplicate slots, no-manifest, unmatched/global-unknown, and detectable capture-time-out-of-date all downgrade. Game-wide uniqueness is never assumed.
- **Capture-time build out-of-date** — the real risk is that the user changed slotting/powers and did not resync before capture, so the synced build was already out-of-date *at capture time*. This is a property of the moment of capture, not of the frozen manifest, which never becomes stale afterward. Where the engine can establish this condition, BuildConfirmed is withheld; where it cannot, the design does not fabricate a mechanism — detection/coverage is an implementation concern and its absence is not treated as proof the build was current.
- **High — multi-file or manifest failure** — stage/validate/publish the immutable directory as a unit; a missing/hash-mismatched manifest prevents build-derived parentage, not independently valid aggregate reads.
- **No frozen manifest** — proc parentage `Unattributed`, never guessed; the manifest reference may be null; validated proc totals remain reliable.
- **Pet instance collisions** — default `NormalizedPetName` rollup; instance split only high-confidence; `CoverageLimited`.
- **Multiword/`unresistable Unique`/future types** — tolerant `DamageType` VO; unknowns pass through.
- **Channel-set variance** — CoverageDescriptor records actual channel evidence where present and observed family coverage, not a fabricated enabled-channel set, so `NotCaptured` and mirror policy behavior are honest.
- **Clock resolution** — source timestamps may be absent, timezone-unspecified, or minute-resolution; use scoped parser order/`ObservedAt` as appropriate, not timestamp-second matching as universal proof.
- **Spine growth on pathological sessions** — hard cap + graceful `Incomplete`.
- **Semantic drift** — every metric-affecting change bumps a version; Compare guards.

---

## 28. Existing code to retain / reuse

- `ParserWorker`, `ParserRawEvent`, `ParserEvent`, `ParserClassifier`, `LogSourceId`, `ParserSourceSegmentId`, `MonitoringSessionManager` and log-discovery stack — **retain**.
- `CombatScaledAmount` — **retain** as the amount primitive.
- `CombatAccuracyAccumulator` semantics (rolled/forced/autohit-separate) — **retain/port**.
- `RollingCombatAccumulator` concept — retain for live.
- `CharacterPerformanceObservationRepository` single-file atomic-write and malformed-report/skip patterns — **reuse** in `SegmentStore`, adding multi-file publication.
- `CharacterPerformanceCombatProjection` baseline→delta discipline — retain as a pattern for live→segment finalize.
- Identity stack: `CharacterRecord`, `CharacterRecordId`, `AccountStableId`, aliases — **retain**.
- Build stack: `CharacterBuildSnapshot`, `HomecomingBuildLayoutSnapshot`, `HomecomingBuildLayoutParser`, `IHomecomingPowerReferenceCatalog`, `BuildEnhancementIndex` — **reuse** as inputs to a validated, collision-aware proc/enhancement resolver and `FrozenBuildManifest`. They do not already persist `ExactProcIdentity`; the power catalog resolves raw triplets, so surfaced-name reverse resolution is additional work.

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

Each slice is independently green-tested and non-regressive. Complete the pre-Slice-1 checkpoints in §24 before production changes. Slices 1–5 may add in-memory types/routing and shadow canonical behavior but make **no on-disk format change**; keep legacy observed behavior stable until the engine explicitly takes ownership. Prerequisite fixture/channel/coverage diagnostics land in the slice that needs them rather than waiting for Slice 12.

### Slice 1 — Grammar/Normalization split (no behavior change)
- **Objective**: extract `GrammarMatcher` and `Normalizer` from `CombatEventParser`, producing today's `CombatEvent` output unchanged.
- **Files**: `CombatEventParser.cs` (split), new `GrammarMatcher.cs`, `Normalizer.cs`, `CombatGrammarId` (extend later).
- **New abstractions**: `GrammarMatch`.
- **Migration impact**: none externally.
- **Tests**: all existing combat parser tests pass; add golden grammar-match tests and the §24 legacy equivalence oracle for successes, failures, `IsCombatShapedUnparsed`, `CompanionMissSummary`, precedence, autohit, and timestamps.
- **Acceptance**: observable legacy output and rejection behavior equal pre-split for all current fixtures; do not require byte identity for runtime-generated provenance across independent rereads.
- **Depends on**: none.
- **Must NOT change**: provenance types, aggregation, persistence, public event shape.

### Slice 2 — Canonical event model + provenance carry-through
- **Objective**: introduce `CanonicalCombatEvent`, `EventProvenance`, `ActorRef`/`ActorType`, `DamageType`, `CombatEventFamily`, `DeliveryFlags`, `EventFacets`, `MirrorClassification`; Normalizer emits it; adapter maps existing families to legacy `CombatEvent` for existing accumulators. Carry an optional **actual** source-channel/message discriminator where the log supplies one; never invent one.
- **Files**: new models; `Normalizer.cs`; temporary `CanonicalToLegacyAdapter`; small additive classifier/parser-envelope field where source-channel evidence exists.
- **Migration**: internal only.
- **Tests**: normalization goldens incl. full scoped provenance, optional actual channel and absent-channel case; classifier-envelope fixture; adapter equivalence including accuracy flags and timestamps.
- **Acceptance**: legacy accumulators still produce identical snapshots via adapter.
- **Depends on**: 1.
- **Must NOT change**: persisted observation format.

### Slice 3 — New grammars (player) + damage-type VO
- **Objective**: add live "health points" heals, incoming `their`+multiword/`unresistable Unique`, endurance grants, mez/knock, `{power} missed!`, env scorch. Admit relevant `PotentialIdentityEvidence`-classified lines for combat normalization **without changing their identity-evidence handling**.
- **Files**: `GrammarMatcher.cs`, `Normalizer.cs`, fixtures.
- **Tests**: one fixture family per grammar; a `PotentialIdentityEvidence` line remains usable for identity and also normalizes as combat; `IsCombatShapedUnparsed` rate drops on the sanitized real log.
- **Acceptance**: audit's failing lines now normalize with correct family/type.
- **Depends on**: 2. **Must NOT change**: dedup/aggregation yet.

### Slice 4 — Pet-prefix + pet actor model
- **Objective**: outer verified pet-prefix strip; pet-scoped variants; `ActorType.OwnPet/OtherPet`; conservative normalized-name rollup, with best-effort `PetInstanceKey` only where justified.
- **Files**: `GrammarMatcher.cs`, `Normalizer.cs`, `PetInstanceResolver.cs`, fixtures (Imp, Essence, Enervating Storm, pet incoming, pet roll, pet heal).
- **Tests**: pet and non-pet colon-prefix fixtures; "you inside verified pet line = the pet" rule; normalized-name rollup and unresolved instance collision.
- **Acceptance**: pet damage/heal/incoming normalize with correct actor/target.
- **Depends on**: 3. **Must NOT change**: classification confidence (Unresolved for now), defeat attribution.

### Slice 5 — Conservative allowlist deduplication
- **Objective**: `Deduplicator` with **Mirror Compatibility Allowlist** (Phase A) + independently evidenced same-logical-event decision (Phase B); `DedupPolicyVersion` v1 may have an **empty** allowlist. No enabled mirror pair without sanitized fixture proof and an independent discriminator; **no** semantic-key/offset/sequence-only merging; **no** `RepeatCount` merge artifact. Under-merge on uncertainty and preserve valid directional `EventFacets` on proven logical-event unions.
- **Files**: `Deduplicator.cs`, `MirrorCompatibilityPolicy.cs`, `MirrorClassification.cs`.
- **New abstractions**: versioned allowlist; `MirrorCandidatesHeldCount`/`MirrorCollapsedCount` diagnostics.
- **Migration**: internal only.
- **Tests (required)**:
  1. empty allowlist keeps candidates separate;
  2. enabled, independently proven heal mirror becomes one logical magnitude with delivered/received facets each counted once;
  3. enabled, independently proven pet/player mirror becomes one logical event;
  4. two identical real damage ticks same second remain two;
  5. two identical pet hits same second remain two;
  6. non-allowlisted family with identical shape remains separate;
  7. ambiguity (independent proof or Phase B constraints fail) keeps both + flags held;
  8. invariant: survivor never carries an absorbed-duplicate count.
- **Acceptance**: empty policy is valid; max-channel fixture collapses **only enabled, independently proven** allowlisted mirrors, preserving all valid directional facets; all identical-repeat and non-allowlist cases remain separate; held candidates counted, not merged.
- **Depends on**: 4. **Must NOT change**: aggregation formulas; must NOT introduce any semantic-hash auto-merge; must NOT add RepeatCount to survivors.

### Slice 5A — Power activation / recharge-state telemetry
- **Objective**: capture direct activation and unconfirmed recharge candidates: `You activated the {power} power.`, `{power} is recharged.`, `{power} is still recharging.`. Capture surfaced name, full provenance, and observation wording only; do not confirm recharge candidates here. No theoretical recharge, no build math, no hidden availability, no Slice 6 accumulators.
- **Files**: `PowerStateTransition.cs`, grammar/normalizer/parser candidate routing, `Fixtures/Combat/power-lifecycle-2026-09-12.tsv`.
- **Tests**: Hasten activation; Hasten recharge-complete; Fire Cages still-recharging; Long Range Teleporter multiword recharge; activation→recharge ordering/provenance; repeated recharge-complete and still-recharging remain distinct; no damage/heal/endurance/attack-resolution fabrication; not allowlisted for dedup; pet combat unchanged; recharge forms canonical-only (legacy `TryParse` stays false); `GrammarSetVersion` bumped once; `DedupPolicyVersion` unchanged.
- **Acceptance**: existing `Activation` legacy mapping unchanged; direct player activation independently establishes the surfaced name. Recharge text remains canonical-only `RechargeCandidate`, never confirmed by the stateless parser. A later session-aware consumer must independently confirm the name from a preceding same-session player activation before lifecycle analytics can use it. Still-recharging wording does not prove blocked-use.
- **Depends on**: 5. **Must NOT change**: aggregation formulas, UI, persistence, proc attribution, mirror allowlist.

### Slice 6 — Dimensioned accumulators + projection DTOs
- **Objective**: `CombatEngine` with per-power/type/target/pet/mez cubes and proc dimensions where proc identity is validated; emit `CombatAnalyticsProjection`; wire the full committed live path; retire `CanonicalToLegacyAdapter` once legacy scalar parity is proven. A small proc detector may be introduced here for parent-independent totals; parent attribution remains Slice 8.
- **Files**: `CombatEngine.cs`, `CombatAnalyticsProjection.cs`, `CombatAnalyticsScope.cs`, `AnalyticsSemanticVersion.cs`; rewire `GameplaySessionManager.ApplyCombatTelemetryLocked` to `TryParseCanonical` → `Deduplicator.LogicalEvents` → `CombatEngine`; keep `CanonicalToLegacyAdapter` + `CombatAggregator` on the original accepted occurrences as the live WPF/`CombatSnapshot` compatibility bridge; do not adapt a facet-unioned survivor to a one-family legacy event. `CombatAnalyticsPresentation` unchanged.
- **Slice 6 implementation notes**: `AnalyticsSemanticVersion` is `1`. `GrammarSetVersion` stays `4`; `DedupPolicyVersion` stays `1`. Production DedupPolicyVersion 1 remains empty and immediately passes through. SessionCombatStream retains a bounded candidate window across live calls for enabled test-only policies, closes it on a sequence gap beyond the candidate band, scope change, or session finalization, and sends only LogicalEvents to CombatEngine. At 256 pending occurrences it keeps all and disables merging for the remainder of the session, with CoverageLimited, rather than over-merging. Reprocessed sequence/byte ranges are excluded within their source scope. Owner session totals include local player plus owned pets; `CombatSnapshot` remains player-only. Recharge confirmation is session-aware inside `CombatEngine`. Observed recharge interval math is deferred. Proc totals are not implemented in this slice.
- **Dimension contracts**: power rows separate scope, normalized pet-name rollup, surfaced power name (ordinal), and outgoing/incoming direction. Session `DamageTypes` is outgoing; `IncomingDamageTypes` is separate. A null damage type produces no type row. Directional mirror facets update each applicable player/pet view once, independent of which representation survived; do not sum overlapping self/pet-aggregate/per-pet views. Direct/DoT and largest-hit values are damage-only. `TotalMagnitude`/`TotalMagnitudeKind` are null for mixed HP/endurance units; typed subtotals remain usable. `None` with zero denotes no measured magnitude.
- **Bounds and coverage**: retain up to 512 power rows plus at most one overflow row per scope/direction (six), 64 pet names plus one overflow, and 256 target names plus one overflow. Internal overflow identities cannot collide with the surfaced name `Other`. Per-power distinct-target count excludes overflow and is a lower bound when coverage is limited. Pet rows remain coverage-limited name rollups, never instance claims. Same-session activation evidence is bounded to 512 scoped names; omitted evidence leaves recharge candidates unmatched. Combat magnitude arithmetic is checked rather than silently wrapping. Unsupported EnvironmentDamage/Unparsed/CompanionMissSummary evidence contributes no fabricated combat magnitude or accuracy.
- **Tests**: projection goldens; cardinality caps/overflow; reuse accuracy tests; facet-preserving single-magnitude heal aggregation; proc total vs proc-by-parent separation where identity is validated.
- **Acceptance**: engine projection DTO emits supported new metrics; existing live scalar snapshots and visible WPF behavior remain unchanged. New WPF metrics are deferred.
- **Depends on**: 5A. **Must NOT change**: on-disk formats.

### Slice 7 — Metric availability/confidence + time model
- **Objective**: `Metric<T>`, `MetricAvailability`, `SegmentClock` with new active duration, and explicit per-rate denominator semantic at projection boundary; retain 0.1.3 session-wall, tracked-pause-adjusted, and rolling outputs unchanged.
- **Files**: `Metric.cs`, `SegmentClock.cs`, projection mapping on `CombatAnalyticsProjection` / `CombatEngine`.
- **Slice 7 implementation notes**: `AnalyticsSemanticVersion` is `2`. `MetricAvailability` defaults to `NotCaptured`; factory-only values reject contradictory availability/evidence/coverage. `MetricConfidence` is retained from §13, nullable and unset for all current projections; no proc inference is implemented. The denominator vocabulary is retained from §14; only capture-wall rates and captured tracked-pause durations are projected here. Active duration/rate are `Unsupported`: §14 permits recorded idle intervals but does not define the activity-family selection policy, so Slice 7 does not import the legacy combat-window algorithm as analytical active time. `ObservedAt` is local parser ingestion time, suitable for arrival/session timing only. `ObservedAnalyticalSpan` is an arrival diagnostic, not a source gameplay interval or a rate denominator; ingestion delays affect it. Clock endpoints retain source-scoped provenance; sequence only breaks timestamp ties within the same source scope. `CaptureEndUtc` is final-only, while `AsOfUtc` identifies an open snapshot's observation boundary. Capture-wall DPS divides captured damage by capture-wall elapsed (minimum 1 ms, tick precision, whole hundredths truncated, overflow unavailable); it makes no claim of source-time gameplay DPS. Replay is deterministic for identical committed provenance and supplied capture boundaries, not independent rereads. No timer publication is added; evidence changes and existing session/track transitions publish coherent as-of clocks. Finalization publishes the existing session-removal transition; the live session list does not retain finalized analytical snapshots. Recharge interval arithmetic stays `NotCaptured`; absent activation/recharge evidence never implies a captured zero. Theoretical recharge, perma-Hasten, overkill, mez duration, companion-miss resolution, and pet instances stay `Unsupported`. Available magnitude means the sum of captured supported observations, not proof of channel completeness: pet name rollup does not lose magnitude or imply instance precision, and absent/unsupported pet families are not inferred. Target overflow or missing target evidence gives incomplete lower-bound cardinality without affecting damage totals. Missing damage types and type overflow mark the corresponding outgoing/incoming/power breakdown incomplete. Mixed-unit combined magnitudes are `Unsupported`, with separately evidenced unit-specific metrics. WPF `CombatSnapshot` and Slice 6 raw numerical outputs remain unchanged.
- **Tests**: NotCaptured≠0; session-wall/tracked-pause/rolling parity; new active-vs-wallclock rates; denominator semantic stamped per metric; sampled spine does not fabricate active duration.
- **Acceptance**: projections expose availability and denominator; active-denominator rates are new/versioned metrics, never in-place reinterpretations of existing DPS/XP/Influence.
- **Depends on**: 6.

### Slice 8 — Frozen build manifest + four-mode proc attribution
- **Objective**: build `FrozenBuildManifest` from `CharacterBuildSnapshot` through a validated collision/alias/coverage-aware raw token → catalog variant/item → canonical enhancement → exact logged proc resolver. Count individual slot occurrences, freeze resolved mapping facts, and hash normalized semantic content only. `Enricher` sets **Direct / BuildConfirmed / Correlated / Unattributed**; separate global/Incarnate path defaults Unattributed until source-specific mapping is proven; `AttributionPolicyVersion`.
- **Files**: `FrozenBuildManifest.cs`, `FrozenBuildManifestHash.cs`, `FrozenBuildManifestFactory.cs`, `EnhancementTokenResolver.cs`, `ProcLogNameIndex.cs`, `ProcAttributionClassifier.cs`, `AttributionPolicyVersion.cs`, `CombatEngine` attach-once + projection, `GameplaySessionManager` freeze-on-Resolved. Durable `BuildManifestStore` (`builds/manifests/{hash}.json`) remains Slice 9.
- **New abstractions**: four-mode `PowerAttribution` with `Candidates`/`Evidence`/`Confidence`; `CombatBuildContextSummary`; `CombatProcAttributionSummary`.
- **Migration impact**: in-memory session freeze only; no rewrite of existing builds; no segment persistence.
- **Slice 8 implementation notes**: `AnalyticsSemanticVersion=3`, `AttributionPolicyVersion=1`; grammar 4 and dedup 1 are unchanged. Manifest identity hashes normalized build/mapping content and a digest of the actual enhancement identity/token/name/set facts used during capture. Policy version remains a separate axis and is excluded from build-content identity. Hashing uses invariant, escaped, source-order-normalized content; capture times, paths, ownership and enhancement levels/boost numbers remain excluded. Qualified category/powerset/power tokens identify parents; derived display names are presentation only. Manifest and candidate collections defensively copy their inputs. Freeze occurs once at first resolved identity, using the build available then; later edits do not rebase an existing session. Missing, corrupt or empty snapshots with no retained analytical content are NotCaptured; retained partial mappings are Incomplete and cannot BuildConfirm. Classification happens on Apply, not Project: prior events are never reclassified. Scoped pre-freeze source boundaries also exclude buffered unresolved-session events applied after manual confirmation. The frozen manifest includes validated proc-name/catalog identities and collision facts, so classification after attachment ignores the live catalog. The catalog lacks a general exact logged-proc identity field: only the real-fixture ENH-01287 / Crafted_Armageddon_F / Armageddon: Chance for Fire Damage mapping is currently validated; other enhancement display names do not imply proc identity. Direct is restricted to the player outgoing damage grammar and fixture-proven Fire Ball, Hot Feet and Fire Cages sources; absence from a proc index never establishes Direct. Unknowns remain Unattributed. The attribution summary partitions outgoing owner damage only (including ordinary Direct damage), not healing/endurance/incoming/activation. Known proc metrics are a separate subset, NotCaptured without observed proc evidence and lower-bound Incomplete when unidentified damage sources remain. BuildConfirmed requires complete capture mapping, one exact non-global resolved slot occurrence, and no proc/power-name collision; duplicate occurrences even within one power remain Unattributed. Correlated is never emitted. Architecture section 11 global examples remain separate Unattributed exclusions, not an exhaustive proc-identity classifier; without source-specific mapping they do not fabricate proc-total coverage. Pet damage never uses player parentage. Attribution counts/magnitudes accumulate once with bounded caches/parent buckets; projection does not retain or replay an unbounded event history. Percent arithmetic uses a wide intermediate. No stale-build detection, timing correlation, persistence or UI change is introduced.
- **Tests (required)**: Direct; real-fixture `Crafted_Armageddon_F` → `Armageddon: Chance for Fire Damage` → one Hot Feet slot ⇒ BuildConfirmed; attuned/superior variants; two matching occurrences even in one parent ⇒ not BuildConfirmed; raw-token collision/unknown/alias handling; **no frozen manifest ⇒ Unattributed**; **establishable capture-time unsynced/out-of-date build ⇒ BuildConfirmed withheld** (no detection mechanism exists, so this case cannot currently be established); unmatched proc ⇒ Unattributed; unresolved global/Incarnate ⇒ separate path, excluded from per-attack share; validated proc total/contribution remains Available under Unattributed parent; manifest hash stable across capture times and resolved facts survive catalog changes.
- **Acceptance**: BuildConfirmed produced **only** when the frozen build proves one validated exact-proc slot occurrence in one non-global parent **and** there is no established indication the synced build was out-of-date at capture; never from game-wide assumptions or unresolved global/Incarnate mapping. Frozen resolved facts, not a future mutable catalog, reproduce the decision; the manifest never becomes stale after capture.
- **Depends on**: 6 (7 recommended). **Must NOT change**: dedup policy; must NOT promote Correlated to BuildConfirmed; must NOT read the mutable latest build during historical replay; must NOT invent a capture-time out-of-date detection mechanism where none exists.

### Slice 9 — Durable Segment model + hybrid persistence
- **Objective**: `SegmentStore` (metadata/coverage/aggregates/spine/annotations) with **two analytical authorities**: cube for persisted metrics not losslessly in spine; spine for only matrix-declared lossless/sufficient fields. Add versioned Lossless Replay Coverage Matrix, bounded sampled/coalesced spine, annotation sidecar, durable manifest dependency, and atomic **multi-file publication**.
- **Files**: `SegmentStore.cs`, `SegmentDocument*.cs`, spine codec.
- **Tests**: staging/final publication and interrupted-write recovery; missing/hash-mismatched manifest; annotation corruption; partial aggregate/spine corruption with field-level fallback; annotation update doesn't rewrite payload; measured spine size/coverage on max-telemetry fixture; exact replay only for matrix-declared fields; held mirror re-evaluation only when both representations and independent discriminator survive.
- **Acceptance**: a captured farm persists as a bounded, durably published segment whose cube and spine authorities are explicit; no impossible universal spine→aggregate replay guarantee. Held mirror candidates are recoverable only where declared lossless.
- **Depends on**: 6,7,8. **Must NOT change**: legacy observation files.
- **Slice 9 implementation notes**: Persistence is hybrid Option D on the existing JSON file store, not SQLite (`reference.db` remains reference-data-only). Published path is `%LocalAppData%\CoH Analytics\Characters\Segments\{gameplaySessionId}_{ordinal:D10}\` with `metadata.json`, `coverage.json`, `aggregates.json`, gzip JSON `spine.bin`, and mutable `annotations.json`. Frozen manifests are content-addressed at `builds/manifests/{hash}.json`. `SegmentSchemaVersion=1`, `SpineSchemaVersion=1`; `AnalyticsSemanticVersion` stays `3`; grammar 4, dedup 1, and attribution policy 1 are unchanged. One Slice 9 Segment is persisted per finalized resolved gameplay session at ordinal 0 after `CombatEngine.Freeze()`; unresolved provisionals are skipped. Duplicate `(GameplaySessionId, SegmentOrdinal)` publications are idempotent when immutable hashes agree and conflicts otherwise. Atomic commit is staging-directory rename; failed writes roll back the staging tree and do not mark the Segment saved. The aggregate cube is the durable authority for Slice 6–8 metrics. The spine retains the first 1024 logical events in apply order (`SegmentSpineLimits.MaxRetainedLogicalEvents`, matching the live combat-tail bound, not last-N). Truncation leaves cube totals complete and marks cube-field replay `NotRecomputable` and event-timeline `PartialReplay`. Clock is cube-authoritative and marked `NotRecomputable` for spine replay: the final clock includes tracked pause-adjusted duration derived from tracked-lifecycle state that is not represented in the spine, and truncation would break first/last observation bounds. `Lossless`/`SufficientForRecompute` are scoped to the normalized post-parse, post-dedup logical spine under the pinned semantic/policy versions; they never imply raw-log, physical-occurrence, or original-text recovery. Frozen build context and policy-1 proc attribution are stored as capture-time facts and are `NotRecomputable` (no rebake). `MetricAvailability`/`Evidence`/`Confidence`/`CoverageInfo` round-trip through factory reconstruction so Available-zero, NotCaptured, Incomplete, and Unsupported stay distinct. No production historical reader, comparison, HTML, or current-policy reprocessing is introduced.

### Slice 10 — Historical read + legacy adapter
- **Objective**: Historical loads new and legacy v1/v2 segments via `LegacyObservationAdapter`, uses canonical character-ID resolution and one visible capture per `(GameplaySessionId, SegmentOrdinal)`, then selects aggregate-fast or matrix-permitted spine-recompute paths.
- **Files**: `HistoricalSegmentReadService.cs`, `LegacyObservationAdapter.cs`.
- **Tests**: legacy ⇒ NotCaptured on missing axes and unknown channel coverage; field-level recompute determinism/NotRecomputable; canonical/retired character IDs; old/new capture-key precedence; cross-account enumeration.
- **Acceptance**: Historical shows the same projection shape as live for one segment.
- **Depends on**: 9.
- **Slice 10 implementation notes**: `HistoricalSegmentReadService` enumerates published Segment headers from `metadata.json` + `coverage.json` only (no aggregate/spine/manifest decode on listing). Default order is capture-end descending, finalized-at descending, `SegmentId` ordinal tie-break. Filters use header metadata (character including canonical/retired IDs, capture/finalization overlap, SegmentId). Authoritative analytics are the persisted cube; the reader never runs `CombatEngine`, parser, dedup, attribution, or the current catalog/build store. `AnalyticsSemanticVersion` stays `3`; Segment/spine schema stay `1`. Schema ≠ 1 is `UnsupportedSchema`. Semantic ≠ 3 is `UnsupportedSemanticVersion` with no current-meaning aggregate. Spine schema/hash/decode failures leave the cube readable and mark detail `Corrupt`/`UnsupportedSchema`/`Unavailable`; truncated spines are `AvailablePartial` (`PartialReplay`) never complete. Missing/corrupt manifests degrade build context only. Corrupt annotations cannot invalidate the cube. Coverage is inspected before spine load (`IncludeSpine` defaults false). `LegacyObservationAdapter` maps observation v1/v2 damage, accuracy, and defeats to `Available` and every other combat axis to `NotCaptured` with `ForLegacyObservation()` coverage (no spine, no rebake, no current-formula DPS). One visible capture per `(GameplaySessionId, SegmentOrdinal)`: a validated durable Segment wins; unpublished/invalid durable falls back to the matching observation and records the conflict. `AnalyticalProjectionView` is the UI-agnostic live/historical shape. Slice 11 comparison is unimplemented.

### Slice 11 — Comparison engine
- **Objective**: `ComparisonEngine` typed deltas (absolute/percent/percentage-point), incompatible/unavailable/normalized states, per-field semantic-version/denominator/dedup/attribution/replay-coverage guards.
- **Files**: `ComparisonEngine.cs`, `MetricComparison.cs`.
- **Tests**: unequal duration; zero-baseline; NotCaptured side; session-wall vs tracked-pause/active; cross-policy refuse unless both fields have sufficient replay evidence; cross-account same-name; build-change surfaced via manifest hash.
- **Acceptance**: any two segments compare without user math.
- **Depends on**: 10.

### Slice 12 — Diagnostics + fixture pipeline hardening
- **Objective**: harden CoverageDescriptor diagnostics (incl. mirror-held, facet unions, replay matrix, and attribution-mode histograms), unparsed sampling, and semantic determinism canary in CI. Prerequisite sanitized mirror/channel fixtures and coverage fields already accompany Slices 2–5 and 9; this slice finalizes and hardens the corpus.
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
- Crit analytics (a synthetic incoming-critical parser fixture exists, but representative real player-crit evidence is still required).
- Any encounter/HP/uptime/overkill reconstruction (UNSUPPORTED).
- Pet instance-split analytics beyond name rollup (ship rollup first).
- Expansion of the mirror allowlist beyond fixtures-proven pairs (additive, versioned).
- Observed activation→recharge-complete and recharge-complete→next-activation interval metrics (Slice 6 confirms recharge observations only; interval pairing is deferred).
- Retirement of `CanonicalToLegacyAdapter` / `CombatAggregator` after live WPF cutover to `CombatAnalyticsProjection`.

---

## 32. Final recommendation

**GO** on the engine-first architecture in the twelve-slice order of §30, subject to the repository-grounded contracts below. These are local corrections, not a redesign:

- **Proc attribution is four-mode** (`Direct`, `BuildConfirmed`, `Correlated`, `Unattributed`). `BuildConfirmed` is deterministic build-based parentage — permitted only when validated exact logged proc/catalog mapping finds exactly one matching **slot occurrence** in one non-global parent in the frozen captured build, with no established indication the synced build was out-of-date at capture. Raw-token collisions, unresolved global/Incarnate effects, and unknown mappings downgrade. The manifest freezes resolved mapping facts and never becomes stale after capture; proc totals/contribution remain parent-independent where proc identity is validated.
- **Deduplication is conservative and allowlist-driven.** The initial allowlist may be empty. No pair is enabled without sanitized fixture evidence and an independent discriminator for that log form; semantic similarity, distinct byte offsets, and sequence proximity alone never merge. Proven unions preserve all valid analytical facets while counting one logical magnitude once. Uncertainty under-merges and is flagged; no merge counter masks it.
- **Bounded historical evidence has declared limits.** The aggregate cube is authoritative where the spine is lossy; the versioned Lossless Replay Coverage Matrix controls exact field-level replay and policy normalization. Multi-file publication and referenced manifests are validated before a segment becomes visible. Existing 0.1.3 rate denominators remain compatibility outputs.

**MODIFY** if, during Slice 5, sanitized fixtures cannot independently prove a proposed mirror family pair — keep that entry disabled (including an empty allowlist) rather than widening a sequence band; revisit before Slice 9. Likewise narrow replay promises when measured spine retention cannot preserve a field losslessly.

**NO-GO** for anything in the UNSUPPORTED list (§22), `BuildConfirmed` without validated exact mapping and single-slot proof (or in the face of an established capture-time out-of-date build), dedup on semantic/proximity keys alone, or exact historical recomputation from discarded evidence. Those must not enter the engine.

Guiding rule throughout: **engine correctness and durable, versioned semantics first; presentation later. Under-merge over data loss; prove parentage, never assume it.**

---

## Appendix A — Revision history

**Revision 1 — Proc attribution (four-mode).**
- Replaced three-mode (`Direct/Correlated/Unattributed`) with `Direct / BuildConfirmed / Correlated / Unattributed`.
- Added **BuildConfirmed**: deterministic parent resolution when the frozen captured build has exactly one matching slotted occurrence of the exact proc; uniqueness judged against the captured build, not game-wide rules; no timing heuristic.
- Clarified proc totals and proc contribution % remain reliable even when parent is Unattributed.

**Revision 2 — Deduplication (conservative, allowlist-driven).**
- Removed semantic-key auto-merge; introduced the versioned Mirror Compatibility Allowlist (Phase A) + the then-described provenance-confirmed same-logical-event decision (Phase B), tightened by Revision 4 to require independent fixture-backed evidence.
- Guaranteed identical real repeats and non-allowlisted families stay separate; ambiguity under-merges with held-candidate diagnostics.
- Removed `RepeatCount` from dedup entirely; retained only as explicit validated DoT-coalescing.
- Replaced `DedupKey` on the event model with `MirrorClassification`.

**Revision 3 (freeze) — Frozen-manifest wording correction.**
- Removed all wording implying a frozen build manifest can become "stale" after capture. A frozen manifest is immutable and never becomes stale.
- Reframed the real build-currency risk as a **capture-time** property: the synced build may already have been unsynced/out-of-date relative to the character when the segment was captured.
- Split the former single combined manifest-availability downgrade into four distinct conditions: (1) no frozen build manifest available, (2) capture-time build unsynced/out-of-date where establishable, (3) proc evidence ambiguous, (4) proc cannot be matched to the frozen manifest/catalog.
- Stated the semantic rule that BuildConfirmed must not be asserted when a capture-time out-of-date condition can be detected/established, while explicitly deferring any detection mechanism (not to be fabricated).
- Updated §3, §11, §13, §22, §23, §24 (attribution fixtures 4–6 and golden tests), §25, §27, Slice 8 tests/acceptance/must-not-change, §31 deferred work, and §32 accordingly.

**Revision 4 — Reconcile frozen baseline with Codex repository validation.**
- Corrected parser provenance and determinism claims: actual channels are optional and not currently retained; scoped sequence/offset proximity is candidate filtering, not mirror proof; independent rereads need not be byte-identical.
- Kept the conservative allowlist with a valid empty initial policy; added fixture-backed independent mirror evidence and a minimal logical-event facet union so proven mirrored heals retain delivered/received perspectives without double magnitude.
- Split segment authority between the aggregate cube and a versioned, bounded spine with a Lossless Replay Coverage Matrix; removed universal replay/recompute and unmeasured file-size promises.
- Kept four-mode proc attribution and BuildConfirmed's unique captured-slot rule while requiring validated, collision-aware exact proc mapping and frozen resolved facts; unknown global/Incarnate sources remain Unattributed; manifest hashes exclude capture-volatile metadata.
- Corrected single-file versus multi-file publication, legacy/new capture precedence, path-derived account-ID portability, `PotentialIdentityEvidence` combat routing, power-catalog lookup, current timing semantics, malformed-file handling, and deferred crit evidence.
- Preserved the twelve-slice order, strengthened pre-Slice-1 checkpoints, and aligned each slice's acceptance with engine-first, legacy-equivalent behavior.

This reconciled document remains the frozen architecture baseline for implementation; no Slice 1 work is included in this revision.

**Revision 5 — Slice 6 accumulator/projection clarification.**
- Named the live analytical types: `CombatEngine`, `CombatAnalyticsProjection`, `CombatSessionSummary`, `CombatPowerAnalysisRow`, `CombatDamageTypeTotal`, `CombatActorSummary`, `CombatTargetSummary`, `CombatAnalyticsScope`, `AnalyticsSemanticVersion` v1.
- Recorded the temporary live compatibility bridge: WPF/`CombatSnapshot` still consume `CombatAggregator` via `CanonicalToLegacyAdapter`; `GameplaySessionSnapshot.CombatAnalytics` carries engine projections without changing visible live totals.
- Deferred observed recharge interval pairing. Slice 6 confirms same-session recharge observations only.

**Revision 6 — Slice 7 availability and time model.**
- Named `Metric<T>` (struct values), `MetricRef<T>` (reference snapshots), `MetricAvailability` (`NotCaptured` default, `Available`, `Incomplete`, `Unsupported`), `MetricEvidence`, `MetricConfidence`, `RateDenominatorKind`, `CoverageInfo`, `SegmentClock`, and `CombatSessionMetricSet`.
- Capture bounds / explicit as-of time describe local session-wall timing; first/last ingestion observations retain scoped provenance. Active-time calculation is withheld until an activity-selection policy is specified. No inferred source-time precision or encounter segmentation is introduced.
- Metric invariants are enforced; coverage is metric-specific. Pet rollup does not invalidate captured magnitude; target/type limitations and mixed units are explicit.
- Confirmed recharge counts are `Available` only after Slice 6 confirmation. Observed recharge intervals remain `NotCaptured` until pairing is implemented. Pet instance split, theoretical recharge, and perma-Hasten are `Unsupported`.

**Revision 7 — Slice 8 frozen build manifest and proc attribution.**
- Named in-memory `FrozenBuildManifest`, `SlotOccurrenceKey`, `PowerAttribution`, `AttributionPolicyVersion` v1, `CombatBuildContextSummary`, and `CombatProcAttributionSummary`. `AnalyticsSemanticVersion` is `3`.
- Freeze is once per gameplay session at first resolved character identity. Content-addressed disk persistence of manifests is Slice 9. Correlated attribution is deferred with no timing heuristic. Capture-time stale-build detection remains unimplemented.

**Revision 8 — Slice 9 durable Segment hybrid persistence.**
- Named `SegmentStore`, `SegmentCaptureMetadata`, `SegmentCoverageDescriptor`, `LosslessReplayCoverageMatrix` (`Lossless` / `SufficientForRecompute` / `PartialReplay` / `NotRecomputable`), `PersistedSpineEvent`, `SegmentSchemaVersion` v1, and `SpineSchemaVersion` v1. `AnalyticsSemanticVersion` remains `3`.
- Hybrid publication writes an authoritative aggregate cube, a first-1024 logical-event spine, frozen version axes, and capture-time build/attribution facts. Truncation never silently replaces cube totals with replay. Saves are atomic and idempotent. Slice 10 historical reading remains unimplemented.

**Revision 9 — Slice 10 historical read and legacy adapter.**
- Named `IHistoricalSegmentReader`, `HistoricalSegmentReadService`, `HistoricalSegmentHeader`, `HistoricalSegment`, `HistoricalCompatibility`, `AnalyticalProjectionView`, and `LegacyObservationAdapter`. `AnalyticsSemanticVersion` remains `3`; Segment/spine schema remain `1`.
- Header listing is metadata/coverage only. Aggregate load uses the persisted cube. Spine load is coverage-first and optional. Legacy v1/v2 observations map missing axes to `NotCaptured`. Comparison, historical UI, HTML, and rebake remain unimplemented.
