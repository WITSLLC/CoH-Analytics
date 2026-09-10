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
            var earned = set.Bonuses
                .Select((bonus, index) => (Bonus: bonus, Tier: tiers[index]))
                .Where(entry => IsEarned(entry.Bonus, pieces))
                .Select(entry => new CharacterBuildEarnedSetBonus
                {
                    ThresholdLabel = entry.Bonus.MinimumBoosts == 1
                        ? "1-piece"
                        : $"{entry.Bonus.MinimumBoosts}-piece",
                    ConditionLabel = entry.Tier.ConditionLabel,
                    HelpLines = entry.Tier.AutoPowerHelps
                })
                .ToArray();

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
            Sets = sets
                .OrderBy(set => set.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

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
    public required IReadOnlyList<CharacterBuildSetAnalysisEntry> Sets { get; init; }

    public bool HasSets => Sets.Count > 0;

    public bool HasNoSets => !HasSets;
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
