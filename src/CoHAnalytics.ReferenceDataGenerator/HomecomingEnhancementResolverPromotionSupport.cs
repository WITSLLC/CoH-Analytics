using System.Text.RegularExpressions;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingEnhancementResolverPromotionSupport
{
    private static readonly Regex ScaleTokenRegex = new(
        @"\{\s*Boost\.Attrib\.([A-Za-z]+)\.Scale\s*\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static EnhancementSourceVariantReferenceRecordDocument CreateVariantDocument(
        EnhancementSourceVariantReferenceRecordDocument baseVariant,
        HomecomingBoostResolverFactsDiscovery facts)
    {
        return new EnhancementSourceVariantReferenceRecordDocument
        {
            HomecomingSourceId = baseVariant.HomecomingSourceId,
            SourceForm = baseVariant.SourceForm,
            Icon = baseVariant.Icon,
            DisplayHelp = baseVariant.DisplayHelp,
            ShortHelp = baseVariant.ShortHelp,
            BoostUsePlayerLevel = ResolveBoostUsePlayerLevel(
                baseVariant.HomecomingSourceId,
                facts.LevelFlags.BoostUsePlayerLevel),
            MaxBoostLevel = facts.LevelFlags.MaxBoostLevel,
            BoostBoostable = facts.LevelFlags.BoostBoostable,
            Effects = facts.Effects
                .Select(effect => new EnhancementSourceVariantEffectReferenceRecordDocument
                {
                    Tag = effect.Tag,
                    Table = effect.Table,
                    Scale = effect.Scale,
                    AttribIds = effect.AttribIds.ToList()
                })
                .ToList()
        };
    }

    internal static EnhancementSetBonusPowerReferenceRecordDocument CreateBonusPowerDocument(
        EnhancementSetBonusPowerReferenceRecordDocument basePower,
        HomecomingBoostResolverFactsDiscovery facts)
    {
        return new EnhancementSetBonusPowerReferenceRecordDocument
        {
            HomecomingSourceId = basePower.HomecomingSourceId,
            DisplayName = basePower.DisplayName,
            DisplayHelp = basePower.DisplayHelp,
            BoostUsePlayerLevel = ResolveBoostUsePlayerLevel(
                basePower.HomecomingSourceId,
                facts.LevelFlags.BoostUsePlayerLevel),
            MaxBoostLevel = facts.LevelFlags.MaxBoostLevel,
            BoostBoostable = facts.LevelFlags.BoostBoostable,
            Effects = facts.Effects
                .Select(effect => new EnhancementSourceVariantEffectReferenceRecordDocument
                {
                    Tag = effect.Tag,
                    Table = effect.Table,
                    Scale = effect.Scale,
                    AttribIds = effect.AttribIds.ToList()
                })
                .ToList()
        };
    }

    internal static EnhancementSourceVariantReferenceRecordDocument CreateEmptyResolverVariantDocument(
        EnhancementSourceVariantReferenceRecordDocument baseVariant) =>
        new()
        {
            HomecomingSourceId = baseVariant.HomecomingSourceId,
            SourceForm = baseVariant.SourceForm,
            Icon = baseVariant.Icon,
            DisplayHelp = baseVariant.DisplayHelp,
            ShortHelp = baseVariant.ShortHelp,
            BoostUsePlayerLevel = false,
            MaxBoostLevel = 0,
            BoostBoostable = false,
            Effects = []
        };

    internal static IReadOnlyDictionary<string, IReadOnlyList<float>> PromoteNamedTables(
        byte[] classesBytes,
        HomecomingClassModTableDiscoveryResult classTables,
        IReadOnlyCollection<string> referencedTableNames)
    {
        ArgumentNullException.ThrowIfNull(classesBytes);
        var canonical = classTables.RequireClass("Class_Blaster");
        var promoted = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal);

        foreach (var tableName in referencedTableNames.OrderBy(value => value, StringComparer.Ordinal))
        {
            IReadOnlyList<float> canonicalValues;
            if (!TryResolveCanonicalTable(canonical, tableName, out canonicalValues))
            {
                canonicalValues = HomecomingClassModTableDiscoveryReader.RequireNamedTable(
                    classesBytes,
                    "Class_Blaster",
                    tableName);
            }

            if (HomecomingClassModTableDiscoveryReader.IdenticalAcrossClassesTableNames.Contains(tableName)
                || HomecomingClassModTableDiscoveryReader.IdenticalAcrossClassesTableNames.Any(
                    value => string.Equals(value, tableName, StringComparison.OrdinalIgnoreCase)))
            {
                var resolvedName = ResolveTableName(canonical, tableName) ?? tableName;
                foreach (var (className, tables) in classTables.TablesByClassName)
                {
                    if (!TryResolveCanonicalTable(tables, resolvedName, out var classValues)
                        && !TryResolveCanonicalTable(tables, tableName, out classValues))
                    {
                        throw new HomecomingEnhancementPromotionException(
                            $"NamedTable '{tableName}' is missing from class '{className}'.");
                    }

                    if (classValues.Count != canonicalValues.Count
                        || !ValuesEqual(classValues, canonicalValues))
                    {
                        throw new HomecomingEnhancementPromotionException(
                            $"NamedTable '{tableName}' disagrees across player classes.");
                    }
                }
            }

            promoted[tableName] = canonicalValues.ToArray();
        }

        return promoted;
    }

    internal static void ValidateTokenCoverage(
        ItemReferenceCatalogDocument document,
        IReadOnlySet<string> liveEnhancementIds)
    {
        var tableNames = new HashSet<string>(StringComparer.Ordinal);
        if (document.EnhancementResolverNamedTables is not null)
        {
            foreach (var table in document.EnhancementResolverNamedTables)
            {
                if (!string.IsNullOrWhiteSpace(table.Name))
                {
                    tableNames.Add(table.Name);
                }
            }
        }

        var unresolved = 0;
        var tokenBearing = 0;
        var covered = 0;
        var unresolvedDetails = new List<string>();

        foreach (var item in document.Items)
        {
            if (!string.Equals(item.Family, nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(item.CatalogItemId)
                || !liveEnhancementIds.Contains(item.CatalogItemId))
            {
                continue;
            }

            foreach (var variant in item.SourceVariants ?? [])
            {
                if (string.IsNullOrWhiteSpace(variant.DisplayHelp))
                {
                    continue;
                }

                foreach (Match match in ScaleTokenRegex.Matches(variant.DisplayHelp))
                {
                    tokenBearing++;
                    var tokenTag = match.Groups[1].Value;
                    var effect = variant.Effects?
                        .FirstOrDefault(value =>
                            string.Equals(value.Tag, tokenTag, StringComparison.OrdinalIgnoreCase));
                    if (effect is null)
                    {
                        unresolved++;
                        if (unresolvedDetails.Count < 32)
                        {
                            unresolvedDetails.Add($"{variant.HomecomingSourceId}:{tokenTag}");
                        }

                        continue;
                    }

                    covered++;
                    if (string.IsNullOrWhiteSpace(effect.Table)
                        || !tableNames.Any(name =>
                            string.Equals(name, effect.Table, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new HomecomingEnhancementPromotionException(
                            $"Variant '{variant.HomecomingSourceId}' effect '{effect.Tag}' references unresolved table '{effect.Table}'.");
                    }
                }
            }
        }

        if (unresolved > 0)
        {
            throw new HomecomingEnhancementPromotionException(
                $"Enhancement resolver promotion left {unresolved} unresolved Scale token bindings: "
                + string.Join(", ", unresolvedDetails));
        }
    }

    internal static IEnumerable<string> ExtractScaleTokens(string? helpText)
    {
        if (string.IsNullOrWhiteSpace(helpText))
        {
            yield break;
        }

        foreach (Match match in ScaleTokenRegex.Matches(NormalizeHelpTokenWhitespace(helpText)))
        {
            yield return match.Groups[1].Value;
        }
    }

    internal static string NormalizeHelpTokenWhitespace(string helpText) =>
        helpText.Replace("{ Boost.", "{Boost.", StringComparison.Ordinal)
            .Replace(".Scale}", ".Scale}", StringComparison.Ordinal);

    internal static bool ResolveBoostUsePlayerLevel(string? homecomingSourceId, bool parsedValue)
    {
        if (parsedValue)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(homecomingSourceId))
        {
            return false;
        }

        return homecomingSourceId.Contains(".Attuned_", StringComparison.Ordinal)
            || homecomingSourceId.Contains(".Superior_Attuned_", StringComparison.Ordinal);
    }

    private static bool TryResolveCanonicalTable(
        IReadOnlyDictionary<string, IReadOnlyList<float>> tables,
        string tableName,
        out IReadOnlyList<float> values)
    {
        if (tables.TryGetValue(tableName, out values!))
        {
            return true;
        }

        foreach (var (name, tableValues) in tables)
        {
            if (string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase))
            {
                values = tableValues;
                return true;
            }
        }

        values = Array.Empty<float>();
        return false;
    }

    private static string? ResolveTableName(
        IReadOnlyDictionary<string, IReadOnlyList<float>> tables,
        string tableName)
    {
        if (tables.ContainsKey(tableName))
        {
            return tableName;
        }

        return tables.Keys.FirstOrDefault(
            value => string.Equals(value, tableName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValuesEqual(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (Math.Abs(left[index] - right[index]) > 1e-6f)
            {
                return false;
            }
        }

        return true;
    }
}
