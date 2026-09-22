using System.Security.Cryptography;
using System.Text.Json;

namespace CoHAnalytics.Models;

/// <summary>Build/mapping content identity; attribution policy is a separate version axis.</summary>
public static class FrozenBuildManifestHash
{
    public static FrozenBuildManifest Finalize(FrozenBuildManifest draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft with { ManifestHash = Compute(draft) };
    }

    public static string Compute(FrozenBuildManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        // JSON supplies invariant numbers and unambiguous escaped string boundaries.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            manifest.BuildCatalogFingerprint,
            Powers = manifest.Powers.OrderBy(p => p.SourceOrder).ThenBy(p => p.CanonicalPowerId, StringComparer.Ordinal)
                .Select(p => new { p.SourceOrder, p.RawCategoryToken, p.RawPowerSetToken, p.RawPowerToken, p.AcquisitionLevel }),
            Slots = manifest.ProcSlots.OrderBy(s => s.Occurrence.PowerSourceOrder)
                .ThenBy(s => s.Occurrence.RawPowerToken, StringComparer.Ordinal).ThenBy(s => s.Occurrence.SlotOrder)
                .Select(s => new { s.Occurrence, s.EnhancementToken, s.CanonicalEnhancementId, s.ExactProcIdentity,
                    s.EnhancementSetId, s.SlottedInPowerId, s.IsAttuned, s.IsEmpty, s.IsGlobalOrIncarnate,
                    s.MappingStatus, s.MappingEvidence }),
            Identities = manifest.ProcIdentities.OrderBy(i => i.LogName, StringComparer.Ordinal)
                .ThenBy(i => i.CatalogItemId, StringComparer.Ordinal)
        });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
