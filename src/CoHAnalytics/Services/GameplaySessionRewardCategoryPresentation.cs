using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

internal static class GameplaySessionRewardCategoryPresentation
{
    public static string GetCategoryLabel(GameplaySessionRewardCategory category) =>
        category switch
        {
            GameplaySessionRewardCategory.RewardCurrency => "CURRENCY",
            GameplaySessionRewardCategory.Salvage => "SALVAGE",
            GameplaySessionRewardCategory.Recipe => "RECIPE",
            GameplaySessionRewardCategory.Enhancement => "ENHANCEMENT",
            GameplaySessionRewardCategory.Inspiration => "INSPIRATION",
            _ => string.Empty
        };

    public static bool CountsTowardCategoryDropTotals(GameplaySessionRewardCategory category) =>
        category is GameplaySessionRewardCategory.Salvage
            or GameplaySessionRewardCategory.Recipe
            or GameplaySessionRewardCategory.Enhancement
            or GameplaySessionRewardCategory.Inspiration;
}
