namespace CoHAnalytics.Homecoming;

internal static class HomecomingTextureArtworkArchives
{
    internal static readonly string[] RelativeArchivePaths =
    [
        Path.Combine("assets", "live", "texture_gui.pigg"),
        Path.Combine("assets", "issue24", "stage2.pigg"),
        Path.Combine("assets", "live", "texture_library.pigg")
    ];

    internal static string ResolveArchivePath(string installRoot, string relativeArchivePath) =>
        Path.Combine(installRoot, relativeArchivePath);
}
