using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Owns persisted Live, Tracked, and Saved session documents. Analytics and other consumers
/// must use this boundary instead of reading session directories directly.
/// </summary>
public interface ISessionStore
{
    string SessionRootDirectory { get; }

    IReadOnlyList<string> LegacyBenchmarkFilePaths { get; }

    IReadOnlyList<string> MalformedFileReports { get; }

    IReadOnlyList<PersistedSessionDocument> GetRecentLiveSessions();

    IReadOnlyList<PersistedSessionDocument> GetRecentTrackedSessions();

    IReadOnlyList<PersistedSessionDocument> GetSavedSessions();

    PersistedSessionDocument? GetSession(Guid sessionId);

    string PersistCompletedLiveSession(PersistedSessionDocument document);

    string PersistCompletedTrackedSession(PersistedSessionDocument document);

  /// <summary>
  /// Promotes a session snapshot into permanent Saved storage. Duplicate unchanged saves for
  /// the same <see cref="PersistedSessionDocument.SessionId"/> are ignored.
  /// </summary>
    SessionSaveResult SaveSession(PersistedSessionDocument document);

    void DeleteSavedSession(Guid sessionId);
}

public enum SessionSaveOutcome
{
    Saved,
    DuplicateSkipped
}

public sealed record SessionSaveResult(SessionSaveOutcome Outcome, string? FilePath);
