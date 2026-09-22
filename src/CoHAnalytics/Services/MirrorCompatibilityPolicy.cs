using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Versioned Mirror Compatibility Allowlist. Production v1 is empty: no pair is enabled
/// without sanitized fixture proof and an independent discriminator for that log form.
/// </summary>
public sealed class MirrorCompatibilityPolicy
{
    private readonly Dictionary<(CombatEventFamily, CombatEventFamily), MirrorCompatibilityRule> _rulesByFamilyPair;

    private MirrorCompatibilityPolicy(int version, IReadOnlyList<MirrorCompatibilityRule> rules)
    {
        Version = version;
        EnabledRules = Array.AsReadOnly(rules.ToArray());
        _rulesByFamilyPair = new Dictionary<(CombatEventFamily, CombatEventFamily), MirrorCompatibilityRule>();
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in EnabledRules)
        {
            ValidateRule(rule);
            if (!ruleIds.Add(rule.RuleId)
                || _rulesByFamilyPair.ContainsKey((rule.LeftFamily, rule.RightFamily)))
            {
                throw new ArgumentException("Duplicate rule ids or family pairs are ambiguous.", nameof(rules));
            }
            _rulesByFamilyPair[(rule.LeftFamily, rule.RightFamily)] = rule;
            _rulesByFamilyPair[(rule.RightFamily, rule.LeftFamily)] = rule;
        }
    }

    public int Version { get; }

    public IReadOnlyList<MirrorCompatibilityRule> EnabledRules { get; }

    /// <summary>Production policy. DedupPolicyVersion 1 has no enabled mirror pairs.</summary>
    public static MirrorCompatibilityPolicy Version1 { get; } =
        new(DedupPolicyVersion.Current, []);

    /// <summary>
    /// Candidate relationships that remain disabled until a committed fixture proves the pair
    /// and supplies an independent discriminator. Not allowlist entries. Same-family damage
    /// is never a candidate.
    /// </summary>
    public static IReadOnlyList<DeferredMirrorRelationship> DeferredRelationships { get; } =
    [
        new(
            CombatEventFamily.HealDealt,
            CombatEventFamily.HealReceived,
            "Panacea/Transfusion delivered/received lines share actor, power, and amount but have no actual combat channel or occurrence id."),
        new(
            CombatEventFamily.EnduranceGrantDealt,
            CombatEventFamily.EnduranceGrantReceived,
            "Panacea endurance grant halves share semantics and adjacency without an independent discriminator."),
        new(
            null,
            null,
            "Player/pet mirrored-message pairs: no fixture shows an independent channel restating one logical hit.")
    ];

    /// <summary>Test-only policy. Must not be used as production Version1.</summary>
    internal static MirrorCompatibilityPolicy ForTests(params MirrorCompatibilityRule[] rules) =>
        new(DedupPolicyVersion.Current, rules);

    public bool IsPhaseAEligible(CombatEventFamily family)
    {
        foreach (var rule in EnabledRules)
        {
            if (rule.LeftFamily == family || rule.RightFamily == family)
            {
                return true;
            }
        }

        return false;
    }

    public MirrorCompatibilityRule? TryGetRule(CombatEventFamily first, CombatEventFamily second)
    {
        if (first == second)
        {
            return null;
        }

        return _rulesByFamilyPair.GetValueOrDefault((first, second));
    }

    private static void ValidateRule(MirrorCompatibilityRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(rule.RuleId))
        {
            throw new ArgumentException("Mirror rule id is required.", nameof(rule));
        }

        if (rule.LeftFamily == rule.RightFamily)
        {
            throw new ArgumentException(
                "Same-family pairs are structurally ineligible for the allowlist.",
                nameof(rule));
        }

        if (string.IsNullOrWhiteSpace(rule.LeftChannel)
            || string.IsNullOrWhiteSpace(rule.RightChannel)
            || string.Equals(rule.LeftChannel, rule.RightChannel, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An enabled rule must name two distinct actual source channels.",
                nameof(rule));
        }

        if (rule.RequiredDiscriminator != MirrorDiscriminatorKind.ActualSourceChannel)
        {
            throw new ArgumentException("An actual source-channel discriminator is required.", nameof(rule));
        }

        if (rule.MaxSequenceDistance < 1 || rule.MaxSequenceDistance > Deduplicator.CandidateSequenceLookahead)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "Sequence band must fit the supported candidate window.");
        }
    }
}

public sealed record DeferredMirrorRelationship(
    CombatEventFamily? LeftFamily,
    CombatEventFamily? RightFamily,
    string Reason);
