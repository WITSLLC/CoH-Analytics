using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Persistent account-scoped store of trusted character records (Revision 9 §3.6.22.5).
/// </summary>
/// <remarks>
/// <para>
/// This repository is a domain service, not an Application Orchestrator contributor. It stores
/// trusted character identity only; gameplay sessions, parser events, and analytics are owned
/// by later slices.
/// </para>
/// <para>
/// Trusted records are created only from Welcome evidence or explicit manual confirmation.
/// Imported Homecoming or Mids builds never establish ownership through this repository.
/// </para>
/// </remarks>
public interface ICharacterRepository
{
  CharacterRepositorySnapshot Current { get; }

  event EventHandler<CharacterRepositoryChangedEventArgs>? StateChanged;

  /// <summary>
  /// Creates or updates a trusted character record from Welcome evidence. Never stores the
  /// raw Welcome line.
  /// </summary>
  CharacterRepositoryOperationResult EstablishTrustedFromWelcome(
      string accountStableId,
      string displayName,
      DateTimeOffset? observedAt = null);

  /// <summary>
  /// Creates or updates a trusted character record from explicit manual user confirmation.
  /// </summary>
  CharacterRepositoryOperationResult EstablishTrustedFromManualConfirmation(
      string accountStableId,
      string displayName);

  /// <summary>
  /// Records an inferred observation against an existing trusted record without changing trust
  /// state or creating ownership.
  /// </summary>
  CharacterRepositoryOperationResult RecordInferredObservation(
      CharacterRecordId recordId,
      DateTimeOffset? observedAt = null);

  /// <summary>
  /// Records reliable character-attributed activity against an existing trusted record.
  /// </summary>
  CharacterRepositoryOperationResult RecordTrustedActivity(
      CharacterRecordId recordId,
      DateTimeOffset observedAt);

  /// <summary>
  /// Records a live character level observation from gameplay telemetry for an existing trusted record.
  /// </summary>
  CharacterRepositoryOperationResult RecordObservedLevel(
      CharacterRecordId recordId,
      int level,
      DateTimeOffset observedAt);

  /// <summary>
  /// Updates the trusted display name for an existing record. When the name changes, the prior
  /// display name is retained as an alias on the same <see cref="CharacterRecordId"/>.
  /// </summary>
  CharacterRepositoryOperationResult RecordTrustedObservedDisplayName(
      CharacterRecordId recordId,
      string displayName,
      CharacterTrustState? trustState = null);

  CharacterRecord? TryFindTrustedByDisplayName(string accountStableId, string displayName);

  CharacterRecord? TryGetRecord(CharacterRecordId recordId);

  /// <summary>
  /// Resolves a retired character-record identity to the surviving canonical identity.
  /// Implementations without consolidation metadata may return the supplied identity unchanged.
  /// </summary>
  CharacterRecordId ResolveCanonicalRecordId(CharacterRecordId recordId) => recordId;

  /// <summary>
  /// Returns the canonical identity and every retired identity that resolves to it. A query made
  /// with a retired identity returns the same complete identity set as a query made with the
  /// canonical survivor.
  /// </summary>
  IReadOnlyList<CharacterRecordId> GetRecordIdsResolvingTo(CharacterRecordId recordId) =>
      [ResolveCanonicalRecordId(recordId)];

  CharacterRecord? TryGetRecordByShortId(string accountStableId, string characterShortId);

  /// <summary>
  /// Persists imported build metadata for an existing trusted record.
  /// </summary>
  CharacterRepositoryOperationResult ImportBuildMetadata(
      CharacterRecordId recordId,
      string? primaryPowerSet,
      string? secondaryPowerSet,
      string archetype,
      int? currentBuildNumber,
      DateTimeOffset observedAt);

  /// <summary>
  /// Assigns an app-owned icon reference to the existing stable character identity.
  /// Passing <see langword="null"/> clears the assignment.
  /// </summary>
  CharacterRepositoryOperationResult SetIconReference(
      CharacterRecordId recordId,
      CharacterIconReference? iconReference);

  CharacterRepositoryDiagnostics GetDiagnostics();
}
