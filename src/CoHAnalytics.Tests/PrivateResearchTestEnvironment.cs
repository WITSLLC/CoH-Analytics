namespace CoHAnalytics.Tests;

/// <summary>
/// Opt-in access to a private external badge research package used by promotion/reconciliation tests.
/// Default CI and contributor runs skip when the package is absent.
/// </summary>
internal static class PrivateResearchTestEnvironment
{
    internal const string Category = "PrivateResearch";

    internal static string RequireResearchRoot()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "Documents", "Reference Data"));

        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException(
                "Private research package is not available. "
                + "These tests are Category=PrivateResearch and are excluded from the default suite "
                + $"({Category}). Provide the optional local research root to run them explicitly.");
        }

        return root;
    }
}
