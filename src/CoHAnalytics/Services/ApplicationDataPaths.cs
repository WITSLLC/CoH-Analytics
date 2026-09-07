namespace CoHAnalytics.Services;

/// <summary>Canonical application-managed storage locations under %LocalAppData%\CoH Analytics.</summary>
public static class ApplicationDataPaths
{
    public const string ApplicationFolderName = "CoH Analytics";

    public static string GetApplicationRoot(string? dataDirectory = null) =>
        dataDirectory
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationFolderName);

    public static string GetSessionsRoot(string? dataDirectory = null) =>
        Path.Combine(GetApplicationRoot(dataDirectory), "Sessions");

    public static string GetLiveSessionsDirectory(string? dataDirectory = null) =>
        Path.Combine(GetSessionsRoot(dataDirectory), "Live");

    public static string GetTrackedSessionsDirectory(string? dataDirectory = null) =>
        Path.Combine(GetSessionsRoot(dataDirectory), "Tracked");

    public static string GetSavedSessionsDirectory(string? dataDirectory = null) =>
        Path.Combine(GetSessionsRoot(dataDirectory), "Saved");

    public static string GetLegacyTrackedBenchmarkDirectory(string? dataDirectory = null) =>
        Path.Combine(GetApplicationRoot(dataDirectory), "Documents", "Saved Sessions");

    public static string GetObservationsRoot(string? dataDirectory = null) =>
        Path.Combine(GetApplicationRoot(dataDirectory), "Observations");

    public static string GetLogsRoot(string? dataDirectory = null) =>
        Path.Combine(GetApplicationRoot(dataDirectory), "Logs");

    public static string GetStandardDiagnosticLogPath(string? dataDirectory = null) =>
        Path.Combine(GetLogsRoot(dataDirectory), "coh-analytics.jsonl");

    public static string GetCharactersRoot(string? dataDirectory = null) =>
        Path.Combine(GetApplicationRoot(dataDirectory), "Characters");

    public static string GetUserCharacterIconsDirectory(string? dataDirectory = null) =>
        Path.Combine(GetApplicationRoot(dataDirectory), "User Icons");

    public static string GetBadgeAcquisitionsPath(string? dataDirectory = null) =>
        Path.Combine(GetCharactersRoot(dataDirectory), "badge-acquisitions.json");

    public static string GetCharacterPerformanceObservationsDirectory(string? dataDirectory = null) =>
        Path.Combine(GetCharactersRoot(dataDirectory), "PerformanceObservations");
}
