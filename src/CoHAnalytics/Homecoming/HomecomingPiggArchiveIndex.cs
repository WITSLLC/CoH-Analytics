namespace CoHAnalytics.Homecoming;

internal sealed class HomecomingPiggArchiveIndex
{
    private readonly string _archivePath;

    private HomecomingPiggArchiveIndex(string archivePath, HomecomingPiggArchiveDirectory directory)
    {
        _archivePath = archivePath;
        Directory = directory;
    }

    internal HomecomingPiggArchiveDirectory Directory { get; }

    internal string ArchivePath => _archivePath;

    internal static HomecomingPiggArchiveIndex Open(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var directory = HomecomingPiggMemberReader.ReadDirectory(stream);
        return new HomecomingPiggArchiveIndex(archivePath, directory);
    }

    internal static HomecomingPiggArchiveIndex? TryOpen(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            return null;
        }

        try
        {
            return Open(archivePath);
        }
        catch (HomecomingPiggException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal bool TryResolveMemberPath(string memberPath, out string resolvedMemberPath) =>
        Directory.TryResolveMemberPath(memberPath, out resolvedMemberPath);

    internal byte[] ReadMember(string resolvedMemberPath) =>
        HomecomingPiggMemberReader.ReadMember(_archivePath, Directory, resolvedMemberPath);
}
