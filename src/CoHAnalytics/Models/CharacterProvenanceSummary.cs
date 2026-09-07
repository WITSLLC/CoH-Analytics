namespace CoHAnalytics.Models;

/// <summary>
/// Bounded provenance for one character record. Identifies how trust was established and how
/// often inferred observations occurred, without raw chat lines or parser payloads.
/// </summary>
public sealed record CharacterProvenanceSummary
{
  public required CharacterTrustState TrustState { get; init; }

  public required DateTimeOffset EstablishedAt { get; init; }

  /// <summary>Number of inferred observations recorded against this trusted record.</summary>
  public int InferredObservationCount { get; init; }

  public DateTimeOffset? LastInferredObservationAt { get; init; }
}
