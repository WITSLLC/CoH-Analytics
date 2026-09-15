using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Builds an in-memory frozen analytical manifest. Does not persist.</summary>
public static class FrozenBuildManifestFactory
{
    public static FrozenBuildManifest Create(
        CharacterBuildSnapshot snapshot,
        EnhancementTokenResolver? resolver,
        string? catalogFingerprint)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Layout);

        var powers = new List<FrozenBuildPower>(snapshot.Layout.Powers.Count);
        var slots = new List<FrozenProcSlot>();
        foreach (var power in snapshot.Layout.Powers)
        {
            powers.Add(new FrozenBuildPower
            {
                RawPowerToken = power.RawPowerToken,
                RawPowerSetToken = power.RawPowerSetToken,
                RawCategoryToken = power.RawCategoryToken,
                AcquisitionLevel = power.AcquisitionLevel,
                SourceOrder = power.SourceOrder
            });

            foreach (var slot in power.Slots)
            {
                slots.Add(MapSlot(power, slot, resolver));
            }
        }

        return FrozenBuildManifestHash.Finalize(new FrozenBuildManifest
        {
            ManifestHash = string.Empty,
            BuildCatalogFingerprint = resolver?.CatalogFingerprint ?? catalogFingerprint,
            ProcIdentities = resolver?.ProcLogNames.Identities ?? [],
            AttributionPolicyVersion = AttributionPolicyVersion.Current,
            Powers = Array.AsReadOnly(powers.ToArray()),
            ProcSlots = Array.AsReadOnly(slots.ToArray())
        });
    }

    private static FrozenProcSlot MapSlot(
        HomecomingBuildPowerSnapshot power,
        HomecomingBuildSlotSnapshot slot,
        EnhancementTokenResolver? resolver)
    {
        var occurrence = new SlotOccurrenceKey
        {
            RawPowerToken = power.RawPowerToken,
            PowerSourceOrder = power.SourceOrder,
            SlotOrder = slot.SlotOrder
        };

        if (slot.IsEmpty)
        {
            return new FrozenProcSlot
            {
                Occurrence = occurrence,
                EnhancementToken = slot.RawEnhancementToken,
                SlottedInPowerId = $"{power.RawCategoryToken}.{power.RawPowerSetToken}.{power.RawPowerToken}",
                IsAttuned = slot.IsAttuned,
                IsEmpty = true,
                MappingStatus = ProcMappingStatus.Unknown,
                MappingEvidence = MetricEvidence.None
            };
        }

        if (resolver is null)
        {
            return new FrozenProcSlot
            {
                Occurrence = occurrence,
                EnhancementToken = slot.RawEnhancementToken,
                SlottedInPowerId = $"{power.RawCategoryToken}.{power.RawPowerSetToken}.{power.RawPowerToken}",
                IsAttuned = slot.IsAttuned,
                IsEmpty = false,
                MappingStatus = ProcMappingStatus.Incomplete,
                MappingEvidence = MetricEvidence.CoverageLimited
            };
        }

        var status = resolver.TryResolve(slot.RawEnhancementToken, out var item);
        if (status != ProcMappingStatus.Resolved || item is null)
        {
            return new FrozenProcSlot
            {
                Occurrence = occurrence,
                EnhancementToken = slot.RawEnhancementToken,
                SlottedInPowerId = $"{power.RawCategoryToken}.{power.RawPowerSetToken}.{power.RawPowerToken}",
                IsAttuned = slot.IsAttuned,
                IsEmpty = false,
                MappingStatus = status,
                MappingEvidence = MetricEvidence.CoverageLimited
            };
        }

        return new FrozenProcSlot
        {
            Occurrence = occurrence,
            EnhancementToken = slot.RawEnhancementToken,
            CanonicalEnhancementId = item.CatalogItemId,
            ExactProcIdentity = resolver.ProcLogNames.IsExactProcIdentity(item.CurrentDisplayName)
                && !resolver.ProcLogNames.IsAmbiguous(item.CurrentDisplayName) ? item.CurrentDisplayName : null,
            EnhancementSetId = item.EnhancementSetId,
            SlottedInPowerId = $"{power.RawCategoryToken}.{power.RawPowerSetToken}.{power.RawPowerToken}",
            IsAttuned = slot.IsAttuned,
            IsEmpty = false,
            MappingStatus = ProcMappingStatus.Resolved,
            MappingEvidence = MetricEvidence.DerivedFromObserved
        };
    }
}
