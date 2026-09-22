# Analytics Engine pre-Slice-1 checkpoint

This is a test-planning contract for the frozen architecture at `6895e86546ba77cfcd476143e102535ba93043c0`. It is not a persisted format, an implementation of Slice 1, or a claim that the current observation store contains a replay spine.

## Replay coverage matrix, version 0 (planning baseline)

`AGGREGATE_ONLY` is the conservative initial promise wherever the proposed bounded spine has not been measured and proved sufficient. The AggregateCube remains authoritative. A later Slice 9 matrix may upgrade individual fields to `LOSSLESS` or `SUFFICIENT_STATISTICS` only after exact retention/recomputation tests; an upgrade is field-specific and versioned. `NOT_CAPTURED` means the planned durable segment omits the state. No row below promises universal event-level replay.

These checkpoint labels map to the architecture's replay-capability language as follows: `LOSSLESS` → `Lossless`; `SUFFICIENT_STATISTICS` → `SufficientForRecompute` for named fields only; `AGGREGATE_ONLY` → spine `Lossy/NotRecomputable` with an authoritative cube value; `NOT_CAPTURED` → no persisted replay evidence.

| Family / field | Initial promise | Conditional Slice 9 evidence to upgrade |
| --- | --- | --- |
| Player damage total, power breakdown | AGGREGATE_ONLY | Exact normalized events and dedup facets for the requested field |
| Incoming damage total, source breakdown | AGGREGATE_ONLY | Exact normalized events and mirror evidence |
| Hit/miss rolls and accuracy | AGGREGATE_ONLY | Every rolled/forced/autohit record and current denominator semantics |
| Activations | AGGREGATE_ONLY | Every activation and its committed order |
| Defeats | AGGREGATE_ONLY | Every supported defeat event and actor scope |
| Healing delivered/received | AGGREGATE_ONLY | Both representations, directional facets, and independent mirror discriminator for any merged pair |
| Pet damage | AGGREGATE_ONLY | Every relevant pet event and stable actor/name resolution inputs |
| Pet incoming damage | AGGREGATE_ONLY | Every relevant event and its source/target roles |
| Pet healing | AGGREGATE_ONLY | Every relevant event and directional facets |
| Validated proc total/contribution | AGGREGATE_ONLY | `LOSSLESS` target only if every proc event, exact identity, provenance, and required mapping facts survive |
| Mez/knock event counts | AGGREGATE_ONLY | Every counted event and family distinction |
| Per-target totals | AGGREGATE_ONLY | Exact target evidence including overflow status |
| Per-damage-type totals | AGGREGATE_ONLY | Exact type evidence including unknown/multiword values |
| Exact DoT tick detail | NOT_CAPTURED | Unbounded per-tick detail is intentionally omitted; `SUFFICIENT_STATISTICS` may instead cover *declared current totals only* if coalesced count/sum/min/max and dimensions are validated |
| Exact AoE fanout detail | NOT_CAPTURED | Arbitrary per-hit detail is not promised; `SUFFICIENT_STATISTICS` may instead cover *declared current totals only* if fanout/target dimensions and sums are validated |
| Rolling windows/state | NOT_CAPTURED | Live-only presentation state; no durable replay promise |

The planned `CoverageDescriptor` must name its matrix version and classify each requested field as `LOSSLESS`, `SUFFICIENT_STATISTICS`, `AGGREGATE_ONLY`, or `NOT_CAPTURED` for that segment. If the spine is sampled, coalesced without the required statistic, absent, corrupt, or missing a held mirror's two representations/discriminator, do not claim exact recompute. Preserve intact authoritative aggregate values. `Available` aggregate value and `NotRecomputable` replay capability are compatible states. Cross-policy Compare normalization requires sufficient evidence on **both** sides for that exact field.

## Segment-size measurement plan for Slice 9

Measure four representative input classes: (1) a short ordinary session, (2) a 40–60 minute farm, (3) a pet-heavy session, and (4) a max-channel session. Each sample must have immutable source identity, duration, channel evidence actually present, and an explicit privacy-safe fixture or opt-in local measurement path. Do not copy raw private chat into segments or repository artifacts.

For each class record raw line count, parsed candidate count, canonical event count, mirror-candidate count (held/collapsed separately), per-family counts, unique actor/power/target/type cardinalities and overflow, estimated serialized AggregateCube size, measured lossless-spine size, measured validated-coalesced-spine size, compressed/uncompressed ratio, peak capture memory, and durable write duration. Record retained fields and the matrix classification beside each size so smaller output cannot conceal lost replay capability. Include p50/p95 and worst observed size/write/memory across repeated samples, plus fixture hash and codec/policy versions for reproducibility. Set size and memory budgets only after these measurements; no single-digit-MB gate is assumed.

## Sanitized source evidence and dedup boundary

`src/CoHAnalytics.Tests/Fixtures/Combat/max-channel-2026-09-12.tsv` contains 72 selected rows from one local Homecoming log exhibiting the audited max-telemetry forms. The first TSV field is the one-based original source-line ordinal; the second is the complete game-log line after deterministic substitution of four player/character names with `Hero_A` and `Ally_A`–`Ally_C`. No account name, game-log path, or raw private chat corpus is copied into the fixture. An offline source-to-fixture check compared all 72 rows after only those substitutions, with zero mismatches. Timestamps, source order, actual `[NPC]`/`[Team]` channel syntax where present, pet/Lore/pseudopet prefixes, powers/procs, numbers, types, casing, punctuation, and selected adjacency are retained. The fixture-copy rule makes the test independent of the live log at runtime; no complete enabled-channel set is inferred from the text.

The Panacea rows 6–7 and Transfusion rows 792–793 are **mirror candidates with insufficient independent proof**: actor/power/amount and near timing agree, but the combat lines have no explicit channel or shared occurrence ID. No pair in this corpus is asserted `ProvenMirror`; the initial dedup allowlist may stay empty. Negative guards include identical player DoT rows 330/341, adjacent identical pet DoT rows 2348/2349, adjacent identical non-mirror-family damage rows 2298/2299, and near-identical Hot Feet rows 2296/2297 with different containment suffixes. These are distinct source rows and must not be collapsed by semantic equality or proximity alone. An individual game-log line does not establish whether visually identical ticks came from separate underlying activations; the safe invariant is to retain both absent independent mirror evidence.

Classifier evidence: `[NPC]` and `[Team]` lines produce `system_channel` and `channel_chat` rules, respectively. Bare combat lines produce `system_prefix`, with no explicit channel discriminator. `ParserEvent.RawLine` retains full text, but no typed channel/speaker field survives classification. A real `Ally_B hits you with their Particle Burst ...` row remains `PotentialIdentityEvidence`; it carries an attributed actor name, but current identity resolution does not adopt that actor as the local character, and the current `CombatEventParser` excludes it as a combat candidate. Slice 3 must preserve that structural evidence while admitting the line for combat normalization.

The logged Armageddon proc row in the sanitized corpus is checked against catalog item `ENH-01287` and the one `Crafted_Armageddon_F` slot under `Hot_Feet` in the existing build-layout fixture. This establishes a candidate **BuildConfirmed evidence chain** for Slice 8, not a current runtime attribution mode. Duplicate same-power occurrences fail the single-slot precondition. The catalog lists Crafted and Superior_Attuned source variants for this item, but no ordinary `Attuned_Armageddon_F` source variant; unknown tokens and `Reactive Interface` remain unresolved for parent attribution in current repository structures.
