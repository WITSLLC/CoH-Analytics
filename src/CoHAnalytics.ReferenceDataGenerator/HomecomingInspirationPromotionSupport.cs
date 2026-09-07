using System.IO;
using System.Text.Json;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingInspirationPromotionSupport
{
    internal static HomecomingInspirationPromotionArtifacts Apply(
        ItemReferenceCatalogDocument document,
        HomecomingInspirationCandidateDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!string.Equals(
                candidate.SchemaVersion,
                HomecomingInspirationCandidateGenerator.SchemaVersion,
                StringComparison.Ordinal))
        {
            throw new HomecomingInspirationPromotionException(
                $"Inspiration candidate schema '{candidate.SchemaVersion}' is not supported.");
        }

        if (candidate.Summary.Ambiguous > 0)
        {
            throw new HomecomingInspirationPromotionException(
                "Inspiration candidate contains ambiguous identity matches.");
        }

        if (candidate.Summary.CurrentCatalogNotMatched > 0)
        {
            throw new HomecomingInspirationPromotionException(
                "Inspiration candidate reports current catalog records absent from Homecoming.");
        }

        var existingInspirations = document.Items
            .Where(item =>
                string.Equals(item.Family, nameof(ReferenceItemFamily.Inspiration), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.CatalogItemId))
            .ToDictionary(item => item.CatalogItemId!, StringComparer.Ordinal);

        var promoted = new List<ItemReferenceRecordDocument>();
        foreach (var record in candidate.Records.OrderBy(
                     value => value.HomecomingSourceId,
                     StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(record.AppOwnedId))
            {
                throw new HomecomingInspirationPromotionException(
                    $"Inspiration '{record.HomecomingSourceId}' is missing a stable catalog ID.");
            }

            if (string.IsNullOrWhiteSpace(record.DisplayName))
            {
                throw new HomecomingInspirationPromotionException(
                    $"Inspiration '{record.HomecomingSourceId}' is missing a display name.");
            }

            if (string.IsNullOrWhiteSpace(record.HomecomingCategory)
                || string.IsNullOrWhiteSpace(record.InspirationForm)
                || string.IsNullOrWhiteSpace(record.IconIdentity))
            {
                throw new HomecomingInspirationPromotionException(
                    $"Inspiration '{record.HomecomingSourceId}' is missing required Homecoming metadata.");
            }

            var existing = existingInspirations.GetValueOrDefault(record.AppOwnedId);
            promoted.Add(ToItemDocument(record, existing));
        }

        if (promoted.Count != candidate.Records.Count)
        {
            throw new HomecomingInspirationPromotionException(
                "Inspiration promotion produced a different record count than the candidate package.");
        }

        var nonInspirations = document.Items
            .Where(item =>
                !string.Equals(item.Family, nameof(ReferenceItemFamily.Inspiration), StringComparison.Ordinal))
            .ToList();
        document.Items = nonInspirations
            .Concat(promoted)
            .OrderBy(item => item.CatalogItemId, StringComparer.Ordinal)
            .ToList();
        document.Aliases = BuildAliases(document.Aliases, promoted);

        var existingCount = promoted.Count(item =>
            existingInspirations.ContainsKey(item.CatalogItemId!));
        return new HomecomingInspirationPromotionArtifacts(
            promoted.Count,
            existingCount,
            promoted.Count - existingCount,
            promoted);
    }

    internal static HomecomingInspirationCandidateDocument LoadCandidate(string candidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        var fullPath = Path.GetFullPath(candidatePath);
        if (!File.Exists(fullPath))
        {
            throw new HomecomingInspirationPromotionException(
                $"Inspiration candidate path '{fullPath}' was not found.");
        }

        return JsonSerializer.Deserialize<HomecomingInspirationCandidateDocument>(
            File.ReadAllBytes(fullPath),
            HomecomingInspirationPromotionCommand.ReadOptions)
            ?? throw new HomecomingInspirationPromotionException("Inspiration candidate document is empty.");
    }

    private static ItemReferenceRecordDocument ToItemDocument(
        HomecomingInspirationCandidateRecord record,
        ItemReferenceRecordDocument? existing)
    {
        return new ItemReferenceRecordDocument
        {
            CatalogItemId = record.AppOwnedId,
            Family = nameof(ReferenceItemFamily.Inspiration),
            Subtype = existing?.Subtype ?? MapSubtype(record.InspirationForm),
            CurrentDisplayName = record.DisplayName,
            ActiveStatus = existing?.ActiveStatus ?? nameof(ReferenceActiveStatus.Active),
            VerificationStatus = existing?.VerificationStatus
                ?? nameof(ReferenceVerificationStatus.VerifiedMultiSource),
            HomecomingSourceId = record.HomecomingSourceId,
            HomecomingCategory = record.HomecomingCategory,
            InspirationStandardTier = record.StandardTier,
            InspirationForm = record.InspirationForm,
            DisplayNameMessageKey = record.DisplayNameMessageKey,
            DisplayHelpMessageKey = record.DisplayHelpMessageKey,
            DisplayHelp = record.DisplayHelp,
            ShortHelpMessageKey = record.ShortHelpMessageKey,
            ShortHelp = record.ShortHelp,
            Icon = record.IconIdentity
        };
    }

    internal static string MapSubtype(string inspirationForm)
    {
        if (string.Equals(inspirationForm, "Special/Event", StringComparison.Ordinal))
        {
            return "Special";
        }

        return inspirationForm;
    }

    private static List<ItemReferenceAliasRecordDocument> BuildAliases(
        List<ItemReferenceAliasRecordDocument> existingAliases,
        IReadOnlyList<ItemReferenceRecordDocument> inspirations)
    {
        var retained = existingAliases
            .Where(alias =>
                alias.CatalogItemId is null
                || !alias.CatalogItemId.StartsWith("INS-", StringComparison.Ordinal))
            .ToList();
        var usedReceiptTexts = new HashSet<string>(
            retained
                .Where(alias =>
                    string.Equals(alias.NameKind, "LogReceipt", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(alias.Text))
                .Select(alias => alias.Text!),
            StringComparer.Ordinal);

        foreach (var inspiration in inspirations)
        {
            if (string.IsNullOrWhiteSpace(inspiration.CurrentDisplayName))
            {
                continue;
            }

            if (!usedReceiptTexts.Add(inspiration.CurrentDisplayName))
            {
                continue;
            }

            retained.Add(new ItemReferenceAliasRecordDocument
            {
                CatalogItemId = inspiration.CatalogItemId,
                Locale = "en",
                Text = inspiration.CurrentDisplayName,
                NameKind = "LogReceipt",
                IsPreferred = true
            });
        }

        return retained
            .OrderBy(alias => alias.CatalogItemId, StringComparer.Ordinal)
            .ThenBy(alias => alias.Text, StringComparer.Ordinal)
            .ToList();
    }
}

internal sealed record HomecomingInspirationPromotionArtifacts(
    int PromotedCount,
    int ExistingCount,
    int NewCount,
    IReadOnlyList<ItemReferenceRecordDocument> PromotedItems);

internal sealed record HomecomingInspirationPromotionResult(
    string CatalogPath,
    string BuildVersion,
    string PackageRevision,
    string CatalogSha256,
    string? DeterminismSha256,
    HomecomingInspirationPromotionArtifacts Artifacts);

internal sealed class HomecomingInspirationPromotionException : Exception
{
    internal HomecomingInspirationPromotionException(string message)
        : base(message)
    {
    }
}
