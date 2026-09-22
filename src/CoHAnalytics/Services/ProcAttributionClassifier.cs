using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Conservative four-mode classifier. Correlated is never emitted: no fixture-proven
/// deterministic correlation rule exists, and timing proximity is forbidden.
/// </summary>
public static class ProcAttributionClassifier
{
    public static PowerAttribution Classify(
        string powerName,
        bool isOwnedPet,
        FrozenBuildManifest? manifest,
        IProcLogNameIndex logNames, bool directDamageForm = false)
    {
        ArgumentNullException.ThrowIfNull(logNames);
        if (manifest is not null) logNames = ProcLogNameIndex.FromFrozen(manifest.ProcIdentities);
        if (string.IsNullOrWhiteSpace(powerName))
        {
            return PowerAttribution.Unattributed with { ManifestHash = manifest?.ManifestHash };
        }

        if (isOwnedPet)
        {
            return new PowerAttribution
            {
                Mode = ProcAttributionMode.Unattributed,
                Path = ProcAttributionPath.OwnedPet,
                ExactProcIdentity = IsProcIdentity(powerName, manifest, logNames) && !logNames.IsAmbiguous(powerName) ? powerName : null,
                Evidence = MetricEvidence.None,
                PolicyVersion = AttributionPolicyVersion.Current,
                ManifestHash = manifest?.ManifestHash
            };
        }

        if (logNames.IsKnownGlobalOrIncarnate(powerName))
        {
            return new PowerAttribution
            {
                Mode = ProcAttributionMode.Unattributed,
                Path = ProcAttributionPath.GlobalEffect,
                Evidence = MetricEvidence.None,
                PolicyVersion = AttributionPolicyVersion.Current,
                ManifestHash = manifest?.ManifestHash
            };
        }

        if (logNames.IsAmbiguous(powerName)
            || (logNames.IsExactProcIdentity(powerName)
                && manifest?.Powers.Any(power => power.SurfacedPowerName == powerName) == true))
            return PowerAttribution.Unattributed with { ManifestHash = manifest?.ManifestHash };
        if (IsProcIdentity(powerName, manifest, logNames))
        {
            return ClassifyProcParent(powerName, manifest, logNames);
        }

        // Only explicit damage fixtures prove Direct. Catalog absence is never positive evidence.
        if (!directDamageForm || powerName is not ("Fire Ball" or "Hot Feet" or "Fire Cages"))
            return PowerAttribution.Unattributed with { ManifestHash = manifest?.ManifestHash };
        return new PowerAttribution
        {
            Mode = ProcAttributionMode.Direct,
            Path = ProcAttributionPath.AttackPower,
            ParentPowerId = powerName,
            ParentPowerName = powerName,
            Evidence = MetricEvidence.DirectObserved,
            Confidence = MetricConfidence.High,
            PolicyVersion = AttributionPolicyVersion.Current,
            ManifestHash = manifest?.ManifestHash
        };
    }

    public static bool IsProcIdentity(
        string powerName,
        FrozenBuildManifest? manifest,
        IProcLogNameIndex logNames)
    {
        return logNames.IsExactProcIdentity(powerName);
    }

    private static PowerAttribution ClassifyProcParent(string exactProcIdentity, FrozenBuildManifest? manifest, IProcLogNameIndex logNames)
    {
        if (manifest is null)
        {
            return new PowerAttribution
            {
                Mode = ProcAttributionMode.Unattributed,
                Path = ProcAttributionPath.ProcParent,
                ExactProcIdentity = exactProcIdentity,
                Evidence = MetricEvidence.None,
                PolicyVersion = AttributionPolicyVersion.Current
            };
        }

        var matching = new List<FrozenProcSlot>();
        foreach (var slot in manifest.ProcSlots)
        {
            if (slot.IsEmpty
                || slot.IsGlobalOrIncarnate
                || slot.MappingStatus != ProcMappingStatus.Resolved
                || string.IsNullOrWhiteSpace(slot.ExactProcIdentity)
                || !string.Equals(slot.ExactProcIdentity, exactProcIdentity, StringComparison.Ordinal))
            {
                continue;
            }

            matching.Add(slot);
        }

        var collision = logNames.IsAmbiguous(exactProcIdentity)
            || manifest.Powers.Any(power => power.SurfacedPowerName == exactProcIdentity);
        if (matching.Count == 1 && manifest.HasCompleteMapping && !collision
            && logNames.Identities.Any(identity => identity.LogName == exactProcIdentity
                && identity.CatalogItemId == matching[0].CanonicalEnhancementId))
        {
            var slot = matching[0];
            var parentId = slot.SlottedInPowerId ?? slot.Occurrence.RawPowerToken;
            var parentName = manifest.Powers.Single(power => power.CanonicalPowerId == parentId).SurfacedPowerName;
            return new PowerAttribution
            {
                Mode = ProcAttributionMode.BuildConfirmed,
                Path = ProcAttributionPath.ProcParent,
                ParentPowerId = parentId,
                ParentPowerName = parentName,
                ExactProcIdentity = exactProcIdentity,
                Evidence = MetricEvidence.DerivedFromObserved,
                Confidence = MetricConfidence.High,
                PolicyVersion = AttributionPolicyVersion.Current,
                ManifestHash = manifest.ManifestHash
            };
        }

        var candidates = matching.Count == 0
            ? Array.Empty<ProcAttributionCandidate>()
            : matching.Select(slot =>
            {
                var parentId = slot.SlottedInPowerId ?? slot.Occurrence.RawPowerToken;
                return new ProcAttributionCandidate
                {
                    ParentPowerId = parentId,
                    ParentPowerName = manifest.Powers.FirstOrDefault(power => power.CanonicalPowerId == parentId)?.SurfacedPowerName ?? parentId,
                    Occurrence = slot.Occurrence
                };
            }).ToArray();

        return new PowerAttribution
        {
            Mode = ProcAttributionMode.Unattributed,
            Path = ProcAttributionPath.ProcParent,
            ExactProcIdentity = exactProcIdentity,
            Candidates = candidates,
            Evidence = MetricEvidence.None,
            PolicyVersion = AttributionPolicyVersion.Current,
            ManifestHash = manifest.ManifestHash
        };
    }
}
