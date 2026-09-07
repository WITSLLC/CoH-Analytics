namespace CoHAnalytics.Models;

/// <summary>
/// Explicit outcome of a Character Repository mutation. Expected conflicts are represented here
/// rather than as exceptions; programming-contract violations may still throw.
/// </summary>
public enum CharacterRepositoryOutcome
{
  Success,
  InvalidAccountStableId,
  InvalidDisplayName,
  InvalidIconReference,
  RecordNotFound,
  PersistenceFailed
}

/// <summary>Result of a Character Repository establish or observation operation.</summary>
public sealed record CharacterRepositoryOperationResult
{
  public required CharacterRepositoryOutcome Outcome { get; init; }

  public CharacterRecordId? RecordId { get; init; }

  public string? Detail { get; init; }

  public bool IsSuccess => Outcome == CharacterRepositoryOutcome.Success;

  public static CharacterRepositoryOperationResult Success(CharacterRecordId recordId) =>
      new() { Outcome = CharacterRepositoryOutcome.Success, RecordId = recordId };

  public static CharacterRepositoryOperationResult Failure(
      CharacterRepositoryOutcome outcome,
      string? detail = null) =>
      new() { Outcome = outcome, Detail = detail };
}
