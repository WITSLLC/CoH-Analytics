using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoHAnalytics.Models;
using CoHAnalytics.Observations;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Services;

/// <summary>
/// Gated, observation-first coordinator for teaching the authoritative item catalog about one
/// selected acquisition. This is intentionally not a general catalog editor.
/// </summary>
public sealed class AcquisitionClassificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _mutationSync = new();
    private readonly IInternalFeatureGate _featureGate;
    private readonly IAcquisitionObservationService _observationService;
    private readonly IItemReferenceCatalog _catalog;
    private readonly string? _authoritativeCatalogPath;
    private readonly string _runtimeDatabasePath;

    public AcquisitionClassificationService(
        IInternalFeatureGate featureGate,
        IAcquisitionObservationService observationService,
        IItemReferenceCatalog catalog,
        AcquisitionClassificationOptions? options = null)
    {
        _featureGate = featureGate;
        _observationService = observationService;
        _catalog = catalog;
        _authoritativeCatalogPath = options?.AuthoritativeCatalogPath ?? FindAuthoritativeCatalogPath();
        _runtimeDatabasePath = options?.RuntimeDatabasePath
            ?? Path.Combine(
                AppContext.BaseDirectory,
                ItemReferenceCatalogFactory.ProductionDatabaseRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
    }

    public bool IsAuthoringAvailable =>
        _featureGate.IsDeveloperMode
        && _catalog is SqliteReferenceStore
        && !string.IsNullOrWhiteSpace(_authoritativeCatalogPath)
        && File.Exists(_authoritativeCatalogPath);

    public string AuthoringAvailabilityDetail => IsAuthoringAvailable
        ? "Authoritative catalog source available."
        : "Authoritative catalog source unavailable.";

    public AcquisitionClassificationResult MapToExistingReference(
        string normalizedObservationKey,
        string catalogItemId)
    {
        lock (_mutationSync)
        {
            var preflight = Preflight(normalizedObservationKey, out var observation, out var document);
            if (preflight is not null)
            {
                return preflight;
            }

            if (!_catalog.TryGetById(catalogItemId, out _))
            {
                return Result(
                    AcquisitionClassificationOutcome.ReferenceNotFound,
                    detail: $"Catalog item '{catalogItemId}' was not found.");
            }

            var aliases = RequireArray(document!, "aliases");
            var normalizedObservedText = ItemReferenceLookup.NormalizeLookupKey(
                observation!.Summary.ObservedText);
            var existingAlias = aliases
                .OfType<JsonObject>()
                .FirstOrDefault(alias => string.Equals(
                    ItemReferenceLookup.NormalizeLookupKey(alias["text"]?.GetValue<string>() ?? string.Empty),
                    normalizedObservedText,
                    StringComparison.Ordinal));

            if (existingAlias is not null)
            {
                var existingItemId = existingAlias["catalogItemId"]?.GetValue<string>();
                if (!string.Equals(existingItemId, catalogItemId, StringComparison.Ordinal))
                {
                    return Result(
                        AcquisitionClassificationOutcome.Conflict,
                        detail: $"The observed receipt already maps to '{existingItemId}'.");
                }

                var reconcileExisting = _observationService.ReconcileObservation(normalizedObservationKey);
                return reconcileExisting.IsSuccess
                    ? Result(AcquisitionClassificationOutcome.Success, catalogItemId)
                    : Result(
                        AcquisitionClassificationOutcome.ReconciliationFailed,
                        catalogItemId,
                        reconcileExisting.Detail ?? "Observation reconciliation failed.");
            }

            aliases.Add(CreateAlias(
                catalogItemId,
                observation.Summary.ObservedText,
                "LogReceipt",
                isPreferred: false));
            UpdateManifest(document!);

            return PublishAndReconcile(
                normalizedObservationKey,
                observation.Summary.ObservedText,
                catalogItemId,
                document!);
        }
    }

    public AcquisitionClassificationResult CreateEnhancementReference(
        string normalizedObservationKey,
        NewEnhancementReference request)
    {
        lock (_mutationSync)
        {
            var preflight = Preflight(normalizedObservationKey, out var observation, out var document);
            if (preflight is not null)
            {
                return preflight;
            }

            if (string.IsNullOrWhiteSpace(request.CurrentDisplayName)
                || string.IsNullOrWhiteSpace(request.Subtype)
                || string.IsNullOrWhiteSpace(request.Variant))
            {
                return Result(
                    AcquisitionClassificationOutcome.ValidationFailed,
                    detail: "Display name, subtype, and variant are required.");
            }

            var items = RequireArray(document!, "items");
            var aliases = RequireArray(document!, "aliases");
            var collision = FindNameCollision(
                document!,
                request.CurrentDisplayName,
                observation!.Summary.ObservedText);
            if (collision is not null)
            {
                return Result(
                    AcquisitionClassificationOutcome.Conflict,
                    detail: $"The name already belongs to '{collision}'. Map to that identity instead.");
            }

            if (!string.IsNullOrWhiteSpace(request.EnhancementSetId)
                && !ContainsEnhancementSet(document!, request.EnhancementSetId))
            {
                return Result(
                    AcquisitionClassificationOutcome.ValidationFailed,
                    detail: $"Enhancement set '{request.EnhancementSetId}' was not found.");
            }

            var catalogItemId = NextCatalogItemId(items, "ENH");
            var item = new JsonObject
            {
                ["catalogItemId"] = catalogItemId,
                ["family"] = "Enhancement",
                ["subtype"] = request.Subtype.Trim(),
                ["currentDisplayName"] = request.CurrentDisplayName.Trim(),
                ["activeStatus"] = "Active",
                ["serverAvailability"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["serverKey"] = ReferenceServerKey.Homecoming,
                        ["status"] = nameof(ReferenceServerAvailabilityStatus.Current)
                    }
                },
                ["verificationStatus"] = "VerifiedDirect",
                ["variant"] = request.Variant.Trim()
            };
            if (!string.IsNullOrWhiteSpace(request.EnhancementSetId))
            {
                item["enhancementSetId"] = request.EnhancementSetId.Trim();
            }

            items.Add(item);
            AddIdentityAliases(
                aliases,
                catalogItemId,
                request.CurrentDisplayName.Trim(),
                observation.Summary.ObservedText);
            UpdateManifest(document!);

            return PublishAndReconcile(
                normalizedObservationKey,
                observation.Summary.ObservedText,
                catalogItemId,
                document!);
        }
    }

    public AcquisitionClassificationResult CreateRecipeReference(
        string normalizedObservationKey,
        NewRecipeReference request)
    {
        lock (_mutationSync)
        {
            var preflight = Preflight(normalizedObservationKey, out var observation, out var document);
            if (preflight is not null)
            {
                return preflight;
            }

            if (string.IsNullOrWhiteSpace(request.CurrentDisplayName)
                || string.IsNullOrWhiteSpace(request.Subtype)
                || string.IsNullOrWhiteSpace(request.ProducedItemId)
                || string.IsNullOrWhiteSpace(request.Rarity))
            {
                return Result(
                    AcquisitionClassificationOutcome.ValidationFailed,
                    detail: "Display name, subtype, produced Enhancement, and rarity are required.");
            }

            if (!_catalog.TryGetById(request.ProducedItemId, out var producedItem)
                || producedItem.Family is not ReferenceItemFamily.Enhancement)
            {
                return Result(
                    AcquisitionClassificationOutcome.ValidationFailed,
                    detail: $"Produced item '{request.ProducedItemId}' is not an Enhancement identity.");
            }

            if (!string.IsNullOrWhiteSpace(request.EnhancementSetId)
                && !string.Equals(
                    request.EnhancementSetId,
                    producedItem.EnhancementSetId,
                    StringComparison.Ordinal))
            {
                return Result(
                    AcquisitionClassificationOutcome.ValidationFailed,
                    detail: "The Recipe and produced Enhancement must use the same Enhancement set identity.");
            }

            var items = RequireArray(document!, "items");
            var aliases = RequireArray(document!, "aliases");
            var collision = FindNameCollision(
                document!,
                request.CurrentDisplayName,
                observation!.Summary.ObservedText);
            if (collision is not null)
            {
                return Result(
                    AcquisitionClassificationOutcome.Conflict,
                    detail: $"The name already belongs to '{collision}'. Map to that identity instead.");
            }

            var catalogItemId = NextCatalogItemId(items, "REC");
            var item = new JsonObject
            {
                ["catalogItemId"] = catalogItemId,
                ["family"] = "Recipe",
                ["subtype"] = request.Subtype.Trim(),
                ["currentDisplayName"] = request.CurrentDisplayName.Trim(),
                ["activeStatus"] = "Active",
                ["verificationStatus"] = "VerifiedDirect",
                ["rarity"] = request.Rarity.Trim(),
                ["producedItemId"] = request.ProducedItemId.Trim()
            };
            var setId = string.IsNullOrWhiteSpace(request.EnhancementSetId)
                ? producedItem.EnhancementSetId
                : request.EnhancementSetId.Trim();
            if (!string.IsNullOrWhiteSpace(setId))
            {
                item["enhancementSetId"] = setId;
            }

            items.Add(item);
            AddIdentityAliases(
                aliases,
                catalogItemId,
                request.CurrentDisplayName.Trim(),
                observation.Summary.ObservedText);
            UpdateManifest(document!);

            return PublishAndReconcile(
                normalizedObservationKey,
                observation.Summary.ObservedText,
                catalogItemId,
                document!);
        }
    }

    private AcquisitionClassificationResult? Preflight(
        string normalizedObservationKey,
        out AcquisitionObservationDetail? observation,
        out JsonObject? document)
    {
        observation = null;
        document = null;

        if (!_featureGate.IsDeveloperMode)
        {
            return Result(AcquisitionClassificationOutcome.DeveloperModeRequired);
        }

        if (!IsAuthoringAvailable || string.IsNullOrWhiteSpace(_authoritativeCatalogPath))
        {
            return Result(
                AcquisitionClassificationOutcome.SourceUnavailable,
                detail: AuthoringAvailabilityDetail);
        }

        observation = _observationService.GetObservationDetail(normalizedObservationKey);
        if (observation is null)
        {
            return Result(AcquisitionClassificationOutcome.ObservationNotFound);
        }

        if (observation.Summary.ResolutionState is AcquisitionIdentityResolutionState.Resolved
            || observation.Summary.Disposition is ObservationDispositionStatus.Resolved
                or ObservationDispositionStatus.Ignored
                or ObservationDispositionStatus.Rejected)
        {
            observation = null;
            return Result(
                AcquisitionClassificationOutcome.ObservationNotFound,
                detail: "The observation is not in the active Needs Classification queue.");
        }

        try
        {
            document = JsonNode.Parse(File.ReadAllText(_authoritativeCatalogPath)) as JsonObject;
            if (document is null)
            {
                return Result(
                    AcquisitionClassificationOutcome.ValidationFailed,
                    detail: "The authoritative catalog document is empty.");
            }
        }
        catch (Exception exception)
        {
            return Result(
                AcquisitionClassificationOutcome.ValidationFailed,
                detail: exception.Message);
        }

        return null;
    }

    private AcquisitionClassificationResult PublishAndReconcile(
        string normalizedObservationKey,
        string observedText,
        string expectedCatalogItemId,
        JsonObject document)
    {
        var sourcePath = _authoritativeCatalogPath!;
        var sourceDirectory = Path.GetDirectoryName(sourcePath)!;
        var sourceTempPath = Path.Combine(sourceDirectory, $"item-catalog.{Guid.NewGuid():N}.tmp");
        var databaseDirectory = Path.GetDirectoryName(_runtimeDatabasePath)!;
        var databaseTempPath = Path.Combine(databaseDirectory, $"reference.{Guid.NewGuid():N}.tmp.db");
        var databaseBackupPath = Path.Combine(databaseDirectory, $"reference.{Guid.NewGuid():N}.backup.db");
        var originalSource = File.ReadAllText(sourcePath);
        var sourceUpdated = false;
        var databaseReplaced = false;
        var databaseBackedUp = false;

        try
        {
            var proposedJson = document.ToJsonString(JsonOptions) + Environment.NewLine;
            using (var validationStream = new MemoryStream(Encoding.UTF8.GetBytes(proposedJson)))
            {
                var validation = ItemReferenceCatalogLoader.Load(validationStream);
                if (!validation.Succeeded)
                {
                    return Result(
                        AcquisitionClassificationOutcome.ValidationFailed,
                        expectedCatalogItemId,
                        validation.FailureReason);
                }
            }

            File.WriteAllText(sourceTempPath, proposedJson, new UTF8Encoding(false));
            File.Move(sourceTempPath, sourcePath, overwrite: true);
            sourceUpdated = true;

            Directory.CreateDirectory(databaseDirectory);
            using (var generationStream = new MemoryStream(Encoding.UTF8.GetBytes(proposedJson)))
            {
                ItemReferenceCatalogImporter.ImportFromJsonStream(generationStream, databaseTempPath);
            }

            var candidate = ItemReferenceCatalogFactory.LoadFromDatabaseFile(databaseTempPath);
            if (!candidate.IsLoaded)
            {
                RestoreSource(sourcePath, originalSource);
                return Result(
                    AcquisitionClassificationOutcome.GenerationFailed,
                    expectedCatalogItemId,
                    candidate.LoadFailureReason);
            }

            if (!candidate.TryResolve(observedText, out var candidateResolution)
                || !string.Equals(
                    candidateResolution.Item.CatalogItemId,
                    expectedCatalogItemId,
                    StringComparison.Ordinal))
            {
                RestoreSource(sourcePath, originalSource);
                return Result(
                    AcquisitionClassificationOutcome.ValidationFailed,
                    expectedCatalogItemId,
                    "The staged catalog did not resolve the observation to the selected identity.");
            }

            if (File.Exists(_runtimeDatabasePath))
            {
                File.Copy(_runtimeDatabasePath, databaseBackupPath, overwrite: true);
                databaseBackedUp = true;
            }

            File.Move(databaseTempPath, _runtimeDatabasePath, overwrite: true);
            databaseReplaced = true;
            string? reloadFailure = null;
            if (_catalog is not SqliteReferenceStore store
                || !store.TryReloadFromDatabase(_runtimeDatabasePath, out reloadFailure))
            {
                RestoreSource(sourcePath, originalSource);
                RestoreDatabase(_runtimeDatabasePath, databaseBackupPath, databaseBackedUp);
                return Result(
                    AcquisitionClassificationOutcome.ReloadFailed,
                    expectedCatalogItemId,
                    reloadFailure);
            }

            var reconcile = _observationService.ReconcileObservation(normalizedObservationKey);
            if (!reconcile.IsSuccess)
            {
                return Result(
                    AcquisitionClassificationOutcome.ReconciliationFailed,
                    expectedCatalogItemId,
                    reconcile.Detail ?? "Observation reconciliation failed.");
            }

            return Result(AcquisitionClassificationOutcome.Success, expectedCatalogItemId);
        }
        catch (Exception exception)
        {
            if (sourceUpdated)
            {
                RestoreSource(sourcePath, originalSource);
            }

            if (databaseReplaced)
            {
                RestoreDatabase(_runtimeDatabasePath, databaseBackupPath, databaseBackedUp);
            }

            return Result(
                AcquisitionClassificationOutcome.GenerationFailed,
                expectedCatalogItemId,
                exception.Message);
        }
        finally
        {
            DeleteNoThrow(sourceTempPath);
            DeleteNoThrow(databaseTempPath);
            DeleteNoThrow(databaseBackupPath);
        }
    }

    private static void AddIdentityAliases(
        JsonArray aliases,
        string catalogItemId,
        string displayName,
        string observedText)
    {
        var sameText = string.Equals(
            ItemReferenceLookup.NormalizeLookupKey(displayName),
            ItemReferenceLookup.NormalizeLookupKey(observedText),
            StringComparison.Ordinal);
        aliases.Add(CreateAlias(
            catalogItemId,
            observedText,
            "LogReceipt",
            isPreferred: sameText));
        if (!sameText)
        {
            aliases.Add(CreateAlias(
                catalogItemId,
                displayName,
                "Display",
                isPreferred: true));
        }
    }

    private static JsonObject CreateAlias(
        string catalogItemId,
        string text,
        string nameKind,
        bool isPreferred) =>
        new()
        {
            ["catalogItemId"] = catalogItemId,
            ["locale"] = "en",
            ["text"] = text,
            ["nameKind"] = nameKind,
            ["isPreferred"] = isPreferred
        };

    private static string? FindNameCollision(
        JsonObject document,
        params string[] names)
    {
        var normalizedNames = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(ItemReferenceLookup.NormalizeLookupKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var alias in RequireArray(document, "aliases").OfType<JsonObject>())
        {
            var text = alias["text"]?.GetValue<string>();
            if (text is not null && normalizedNames.Contains(ItemReferenceLookup.NormalizeLookupKey(text)))
            {
                return alias["catalogItemId"]?.GetValue<string>();
            }
        }

        foreach (var item in RequireArray(document, "items").OfType<JsonObject>())
        {
            var displayName = item["currentDisplayName"]?.GetValue<string>();
            if (displayName is not null
                && normalizedNames.Contains(ItemReferenceLookup.NormalizeLookupKey(displayName)))
            {
                return item["catalogItemId"]?.GetValue<string>();
            }
        }

        return null;
    }

    private static bool ContainsEnhancementSet(JsonObject document, string catalogItemId) =>
        RequireArray(document, "enhancementSets")
            .OfType<JsonObject>()
            .Any(set => string.Equals(
                set["catalogItemId"]?.GetValue<string>(),
                catalogItemId,
                StringComparison.Ordinal));

    private static string NextCatalogItemId(JsonArray items, string prefix)
    {
        var maximum = items
            .OfType<JsonObject>()
            .Select(item => item["catalogItemId"]?.GetValue<string>())
            .Where(id => id is not null && id.StartsWith(prefix + "-", StringComparison.Ordinal))
            .Select(id => int.TryParse(id!.AsSpan(prefix.Length + 1), out var value) ? value : 0)
            .DefaultIfEmpty()
            .Max();
        return $"{prefix}-{maximum + 1:D5}";
    }

    private static JsonArray RequireArray(JsonObject document, string propertyName) =>
        document[propertyName] as JsonArray
        ?? throw new InvalidOperationException($"Catalog property '{propertyName}' is missing.");

    private static void UpdateManifest(JsonObject document)
    {
        var manifest = document["manifest"] as JsonObject
            ?? throw new InvalidOperationException("Catalog manifest is missing.");
        manifest["catalogVersion"] = IncrementCatalogVersion(
            manifest["catalogVersion"]?.GetValue<string>());
        manifest["sourceRevision"] = $"developer-tools-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}Z";
        var notes = manifest["sourceNotes"]?.GetValue<string>() ?? string.Empty;
        if (!notes.Contains("Developer Tools observation classification", StringComparison.Ordinal))
        {
            manifest["sourceNotes"] = string.IsNullOrWhiteSpace(notes)
                ? "Developer Tools observation classification."
                : notes.TrimEnd() + " Developer Tools observation classification.";
        }
    }

    private static string IncrementCatalogVersion(string? current)
    {
        const string prefix = "item-ref-";
        if (current is not null
            && current.StartsWith(prefix, StringComparison.Ordinal)
            && Version.TryParse(current[prefix.Length..], out var version))
        {
            return $"{prefix}{version.Major}.{version.Minor}.{version.Build + 1}";
        }

        return $"item-ref-developer-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private static string? FindAuthoritativeCatalogPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "CoHAnalytics",
                "ReferenceData",
                "item-catalog.v1.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void RestoreSource(string sourcePath, string originalSource)
    {
        var restorePath = sourcePath + ".restore.tmp";
        File.WriteAllText(restorePath, originalSource, new UTF8Encoding(false));
        File.Move(restorePath, sourcePath, overwrite: true);
    }

    private static void RestoreDatabase(string databasePath, string backupPath, bool backupExists)
    {
        if (backupExists && File.Exists(backupPath))
        {
            File.Copy(backupPath, databasePath, overwrite: true);
            return;
        }

        DeleteNoThrow(databasePath);
    }

    private static void DeleteNoThrow(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static AcquisitionClassificationResult Result(
        AcquisitionClassificationOutcome outcome,
        string? catalogItemId = null,
        string? detail = null) =>
        new()
        {
            Outcome = outcome,
            CatalogItemId = catalogItemId,
            Detail = detail
        };
}
