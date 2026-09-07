namespace CoHAnalytics.Services;

/// <summary>
/// Code-defined options for <see cref="CharacterRepository"/>.
/// </summary>
public sealed class CharacterRepositoryOptions
{
  public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

  /// <summary>
  /// Directory containing <c>characters.json</c>. When null, the default application data
  /// directory is used.
  /// </summary>
  public string? DataDirectory { get; init; }

  public int MaxRecentDiagnosticsEntries { get; init; } = 50;

  public int MaximumAliasesPerCharacter { get; init; } = 32;

  /// <summary>
  /// When true, the next persistence attempt fails with a simulated I/O error. For tests only.
  /// </summary>
  public bool SimulatePersistenceFailure { get; init; }
}
