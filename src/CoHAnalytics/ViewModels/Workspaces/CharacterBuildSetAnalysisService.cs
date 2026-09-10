using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ViewModels.Workspaces;

public interface ICharacterBuildSetAnalysisService
{
    CharacterBuildSetAnalysis Analyze(HomecomingBuildLayoutSnapshot snapshot);
}

public sealed class CharacterBuildSetAnalysisService : ICharacterBuildSetAnalysisService
{
    private readonly IItemReferenceCatalog _catalog;
    private readonly IEnhancementHelpResolver? _helpResolver;

    public CharacterBuildSetAnalysisService(
        IItemReferenceCatalog catalog,
        IEnhancementHelpResolver? helpResolver = null)
    {
        _catalog = catalog;
        _helpResolver = helpResolver;
    }

    public CharacterBuildSetAnalysis Analyze(HomecomingBuildLayoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var enhancementIndex = AccountsBuildPresentationSupport.BuildEnhancementIndex(_catalog);
        var piecesBySet = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var slot in snapshot.Powers.SelectMany(power => power.Slots))
        {
            if (slot.IsEmpty
                || string.IsNullOrWhiteSpace(slot.RawEnhancementToken)
                || !enhancementIndex.TryGetValue(slot.RawEnhancementToken, out var match)
                || string.IsNullOrWhiteSpace(match.Item.EnhancementSetId))
            {
                continue;
            }

            if (!piecesBySet.TryGetValue(match.Item.EnhancementSetId, out var pieces))
            {
                pieces = new HashSet<string>(StringComparer.Ordinal);
                piecesBySet.Add(match.Item.EnhancementSetId, pieces);
            }

            pieces.Add(match.Item.CatalogItemId);
        }

        var sets = new List<CharacterBuildSetAnalysisEntry>();
        var summaryContributions = new List<CharacterBuildSummaryBonusContribution>();
        foreach (var (setId, pieces) in piecesBySet)
        {
            if (!_catalog.TryGetEnhancementSetById(setId, out var set))
            {
                continue;
            }

            var tiers = ReferenceEnhancementBrowseSupport.BuildSetBonusTiers(
                _catalog,
                set,
                _helpResolver,
                snapshot.CharacterLevel ?? 50);
            var earnedEntries = set.Bonuses
                .Select((bonus, index) => (Bonus: bonus, Tier: tiers[index]))
                .Where(entry => IsEarned(entry.Bonus, pieces))
                .ToArray();
            var earned = earnedEntries
                .Select(entry => new CharacterBuildEarnedSetBonus
                {
                    ThresholdLabel = entry.Bonus.MinimumBoosts == 1
                        ? "1-piece"
                        : $"{entry.Bonus.MinimumBoosts}-piece",
                    ConditionLabel = entry.Tier.ConditionLabel,
                    HelpLines = entry.Tier.AutoPowerHelps
                })
                .ToArray();

            foreach (var entry in earnedEntries)
            {
                var canonicalPowers = entry.Bonus.AutoPowers
                    .Where(power => !string.IsNullOrWhiteSpace(power.DisplayHelp))
                    .ToArray();
                for (var index = 0;
                     index < canonicalPowers.Length && index < entry.Tier.AutoPowerHelps.Count;
                     index++)
                {
                    summaryContributions.Add(new CharacterBuildSummaryBonusContribution(
                        canonicalPowers[index].HomecomingSourceId,
                        entry.Tier.AutoPowerHelps[index],
                        entry.Tier.ConditionLabel));
                }
            }

            sets.Add(new CharacterBuildSetAnalysisEntry
            {
                EnhancementSetId = set.CatalogItemId,
                DisplayName = set.CurrentDisplayName,
                PieceCount = pieces.Count,
                PieceCountLabel = pieces.Count == 1 ? "1 piece" : $"{pieces.Count} pieces",
                EarnedBonuses = earned
            });
        }

        return new CharacterBuildSetAnalysis
        {
            SummaryBonuses = AggregateSummaryBonuses(
                summaryContributions.Where(contribution => !IsGlobalBonus(contribution.CanonicalIdentity))),
            GlobalBonuses = AggregateSummaryBonuses(
                summaryContributions.Where(contribution => IsGlobalBonus(contribution.CanonicalIdentity))),
            Sets = sets
                .OrderBy(set => set.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static IReadOnlyList<CharacterBuildSummaryBonus> AggregateSummaryBonuses(
        IEnumerable<CharacterBuildSummaryBonusContribution> contributions) =>
        contributions
            .GroupBy(
                contribution => contribution.CanonicalIdentity,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new CharacterBuildSummaryBonus
            {
                CanonicalIdentity = group.Key,
                DisplayText = group.First().DisplayText,
                ConditionLabel = group.First().ConditionLabel,
                Count = group.Count(),
                CountLabel = $"{group.Count()}×"
            })
            .OrderByDescending(bonus => bonus.Count)
            .ThenBy(bonus => bonus.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsGlobalBonus(string canonicalIdentity) =>
        canonicalIdentity.StartsWith(
            "Set_Bonus.Global_Bonus.",
            StringComparison.OrdinalIgnoreCase);

    private sealed record CharacterBuildSummaryBonusContribution(
        string CanonicalIdentity,
        string DisplayText,
        string? ConditionLabel);

    private static bool IsEarned(
        EnhancementSetBonusReferenceRecord bonus,
        IReadOnlySet<string> pieces)
    {
        if (pieces.Count < bonus.MinimumBoosts)
        {
            return false;
        }

        return bonus.RequiresPattern != ReferenceEnhancementSetBonusRequiresPattern.PieceGate
            || (bonus.RequiredEnhancementIds.Count > 0
                && bonus.RequiredEnhancementIds.All(pieces.Contains));
    }
}

public sealed class CharacterBuildSetAnalysis
{
    public required IReadOnlyList<CharacterBuildSummaryBonus> SummaryBonuses { get; init; }

    public required IReadOnlyList<CharacterBuildSummaryBonus> GlobalBonuses { get; init; }

    public required IReadOnlyList<CharacterBuildSetAnalysisEntry> Sets { get; init; }

    public bool HasSummaryBonuses => SummaryBonuses.Count > 0;

    public bool HasGlobalBonuses => GlobalBonuses.Count > 0;

    public bool HasNoSummaryBonuses => !HasSummaryBonuses && !HasGlobalBonuses;

    public bool HasSets => Sets.Count > 0;

    public bool HasNoSets => !HasSets;
}

public sealed class CharacterBuildSummaryBonus
{
    /// <summary>Canonical Homecoming auto-power identity used only for aggregation.</summary>
    public required string CanonicalIdentity { get; init; }

    /// <summary>Canonical, resolved Homecoming help shown to the user.</summary>
    public required string DisplayText { get; init; }

    public string? ConditionLabel { get; init; }

    public required int Count { get; init; }

    public required string CountLabel { get; init; }
}

public sealed class CharacterBuildSetAnalysisEntry
{
    public required string EnhancementSetId { get; init; }

    public required string DisplayName { get; init; }

    public required int PieceCount { get; init; }

    public required string PieceCountLabel { get; init; }

    public required IReadOnlyList<CharacterBuildEarnedSetBonus> EarnedBonuses { get; init; }
}

public sealed class CharacterBuildEarnedSetBonus
{
    public required string ThresholdLabel { get; init; }

    public string? ConditionLabel { get; init; }

    public required IReadOnlyList<string> HelpLines { get; init; }
}
