namespace CoHAnalytics.Models;

/// <summary>
/// Raised when the character repository published a semantically different snapshot.
/// </summary>
public sealed class CharacterRepositoryChangedEventArgs : EventArgs
{
  public CharacterRepositoryChangedEventArgs(CharacterRepositorySnapshot snapshot)
  {
    Snapshot = snapshot;
  }

  public CharacterRepositorySnapshot Snapshot { get; }
}
