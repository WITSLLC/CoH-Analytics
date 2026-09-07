using System.Security.Cryptography;
using System.Text;

namespace CoHAnalytics.Models;

/// <summary>
/// Stable runtime identity for one candidate chat-log source.
/// </summary>
/// <remarks>
/// Identity is deterministic from the owning account's stable id, the normalized absolute
/// file path, and an identity generation. The account is part of the identity because every
/// account uses the same daily file name, so a path or file name alone cannot distinguish
/// two accounts' logs. The generation advances only when replacement of the underlying file
/// has been reliably detected, so a replacement file never silently reuses a prior identity.
/// </remarks>
public sealed record LogSourceId
{
    private LogSourceId(
        string value,
        string accountStableId,
        string accountDisplayName,
        string filePath,
        string fileName,
        DateOnly logDate,
        int identityGeneration)
    {
        Value = value;
        AccountStableId = accountStableId;
        AccountDisplayName = accountDisplayName;
        FilePath = filePath;
        FileName = fileName;
        LogDate = logDate;
        IdentityGeneration = identityGeneration;
    }

    /// <summary>Opaque, deterministic identity value.</summary>
    public string Value { get; }

    public string AccountStableId { get; }

    public string AccountDisplayName { get; }

    /// <summary>Normalized absolute path of the candidate file.</summary>
    public string FilePath { get; }

    public string FileName { get; }

    public DateOnly LogDate { get; }

    /// <summary>Incremented when the file at <see cref="FilePath"/> is detected as replaced.</summary>
    public int IdentityGeneration { get; }

    public static LogSourceId Create(
        string accountStableId,
        string accountDisplayName,
        string filePath,
        DateOnly logDate,
        int identityGeneration = 0)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var value = ComputeValue(accountStableId, normalizedPath, identityGeneration);

        return new LogSourceId(
            value,
            accountStableId,
            accountDisplayName,
            normalizedPath,
            Path.GetFileName(normalizedPath),
            logDate,
            identityGeneration);
    }

    /// <summary>Produces the next identity for the same path after detected replacement.</summary>
    public LogSourceId NextGeneration() =>
        Create(AccountStableId, AccountDisplayName, FilePath, LogDate, IdentityGeneration + 1);

    public override string ToString() => Value;

    private static string ComputeValue(string accountStableId, string normalizedPath, int identityGeneration)
    {
        var material = $"{accountStableId}|{normalizedPath.ToLowerInvariant()}|{identityGeneration}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
