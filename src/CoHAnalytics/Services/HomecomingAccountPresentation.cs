using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

internal static class HomecomingAccountPresentation
{
    public const int RecentAccountLimit = 3;

    public static IReadOnlyList<HomecomingAccount> OrderForAccountsWorkspace(IEnumerable<HomecomingAccount> accounts) =>
        accounts
            .OrderByDescending(account => account.HasHistoricalLogs)
            .ThenByDescending(account => account.NewestLogTimestamp ?? DateTimeOffset.MinValue)
            .ThenBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<HomecomingAccount> SelectRecentAccounts(IEnumerable<HomecomingAccount> accounts, int maxCount = RecentAccountLimit)
    {
        var ordered = OrderForAccountsWorkspace(accounts);
        var withHistoricalLogs = ordered
            .Where(account => account.HasHistoricalLogs)
            .Take(maxCount)
            .ToList();

        if (withHistoricalLogs.Count > 0)
        {
            return withHistoricalLogs;
        }

        return ordered
            .Take(maxCount)
            .ToList();
    }
}
