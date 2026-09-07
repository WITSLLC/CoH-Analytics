using System.Text.RegularExpressions;

namespace CoHAnalytics.ReferenceData;

/// <summary>
/// Pure static resolver for supported Homecoming Enhancement Scale help tokens.
/// </summary>
public sealed class EnhancementHelpResolver : IEnhancementHelpResolver
{
    private static readonly Regex ScaleTokenRegex = new(
        @"\{\s*Boost\.Attrib\.([A-Za-z]+)\.Scale\s*\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<string, IReadOnlyList<float>> _namedTables;

    public EnhancementHelpResolver(IReadOnlyDictionary<string, IReadOnlyList<float>> namedTables)
    {
        ArgumentNullException.ThrowIfNull(namedTables);
        _namedTables = namedTables;
    }

    public EnhancementHelpResolutionResult Resolve(
        string template,
        EnhancementSourceVariantReferenceRecord variant,
        EnhancementHelpPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(context);

        if (template is null)
        {
            return Invalid("Template is required.");
        }

        if (context.PresentationLevel < 1)
        {
            return Invalid($"PresentationLevel must be at least 1; received {context.PresentationLevel}.");
        }

        var matches = ScaleTokenRegex.Matches(template);
        if (matches.Count == 0)
        {
            return new EnhancementHelpResolutionResult
            {
                Status = EnhancementHelpResolutionStatus.Unchanged,
                ResolvedText = template,
            };
        }

        var unresolvedTokens = new List<string>();
        var diagnostics = new List<string>();
        var resolvedCount = 0;
        var builder = new System.Text.StringBuilder(template);

        for (var index = matches.Count - 1; index >= 0; index--)
        {
            var match = matches[index];
            var tokenText = match.Value;
            var tag = match.Groups[1].Value;

            if (!TryBindEffect(variant.Effects, tag, out var effect, out var bindDiagnostic))
            {
                if (bindDiagnostic is not null)
                {
                    diagnostics.Add(bindDiagnostic);
                }

                unresolvedTokens.Add(tokenText);
                continue;
            }

            if (!TryResolveNumericValue(
                    effect,
                    variant,
                    context.PresentationLevel,
                    out var formattedValue,
                    out var valueDiagnostic))
            {
                if (valueDiagnostic is not null)
                {
                    diagnostics.Add(valueDiagnostic);
                }

                unresolvedTokens.Add(tokenText);
                continue;
            }

            builder.Remove(match.Index, match.Length);
            builder.Insert(match.Index, formattedValue);
            resolvedCount++;
        }

        unresolvedTokens.Reverse();
        diagnostics.Reverse();

        var status = resolvedCount switch
        {
            0 when diagnostics.Count > 0 || unresolvedTokens.Count > 0 =>
                EnhancementHelpResolutionStatus.PartiallyResolved,
            0 => EnhancementHelpResolutionStatus.Unchanged,
            _ when unresolvedTokens.Count > 0 || diagnostics.Count > 0 =>
                EnhancementHelpResolutionStatus.PartiallyResolved,
            _ => EnhancementHelpResolutionStatus.FullyResolved,
        };

        return new EnhancementHelpResolutionResult
        {
            Status = status,
            ResolvedText = builder.ToString(),
            UnresolvedTokens = unresolvedTokens,
            Diagnostics = diagnostics,
        };
    }

    private static EnhancementHelpResolutionResult Invalid(string diagnostic) =>
        new()
        {
            Status = EnhancementHelpResolutionStatus.InvalidInput,
            ResolvedText = string.Empty,
            Diagnostics = [diagnostic],
        };

    private static bool TryBindEffect(
        IReadOnlyList<EnhancementSourceVariantEffectReferenceRecord> effects,
        string tag,
        out EnhancementSourceVariantEffectReferenceRecord effect,
        out string? diagnostic)
    {
        var matches = effects
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Tag)
                && string.Equals(candidate.Tag, tag, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            effect = null!;
            diagnostic = null;
            return false;
        }

        var signatures = matches
            .Select(candidate => (candidate.Table, candidate.Scale))
            .Distinct()
            .ToArray();

        if (signatures.Length > 1)
        {
            effect = null!;
            diagnostic =
                $"Conflicting Scale evidence for tag '{tag}' (distinct table/scale bindings).";
            return false;
        }

        effect = matches[0];
        diagnostic = null;
        return true;
    }

    private bool TryResolveNumericValue(
        EnhancementSourceVariantEffectReferenceRecord effect,
        EnhancementSourceVariantReferenceRecord variant,
        int presentationLevel,
        out string formattedValue,
        out string? diagnostic)
    {
        if (!TryGetNamedTable(effect.Table, out var table))
        {
            formattedValue = string.Empty;
            diagnostic = $"NamedTable '{effect.Table}' is missing from production Reference data.";
            return false;
        }

        if (table.Count == 0)
        {
            formattedValue = string.Empty;
            diagnostic = $"NamedTable '{effect.Table}' has no values.";
            return false;
        }

        if (EnhancementHelpResolverBoosterMultipliers.TryGetBoosterCount(presentationLevel, out var boosterCount))
        {
            if (!EnhancementHelpResolverBoosterMultipliers.SupportsBoostedPresentationLevels(variant))
            {
                formattedValue = string.Empty;
                diagnostic =
                    $"Presentation level {presentationLevel} requires a boostable crafted source variant.";
                return false;
            }

            var nativeCapTableIndex = Math.Clamp(
                EnhancementHelpResolverBoosterMultipliers.NativeCapPresentationLevel - 1,
                0,
                table.Count - 1);
            var baseRawPercentage = table[nativeCapTableIndex] * effect.Scale * 100f;
            var boostedRawPercentage = baseRawPercentage
                * EnhancementHelpResolverBoosterMultipliers.Plus0ThroughPlus5[boosterCount];
            formattedValue = EnhancementHelpScaleFormatter.FormatDisplayPercentage(boostedRawPercentage);
            diagnostic = null;
            return true;
        }

        var effectiveLevel = variant.BoostUsePlayerLevel
            ? Math.Min(presentationLevel, variant.MaxBoostLevel)
            : presentationLevel;

        var tableIndex = Math.Clamp(effectiveLevel - 1, 0, table.Count - 1);
        var rawFraction = table[tableIndex] * effect.Scale;
        var rawPercentage = rawFraction * 100f;
        formattedValue = EnhancementHelpScaleFormatter.FormatDisplayPercentage(rawPercentage);
        diagnostic = null;
        return true;
    }

    private bool TryGetNamedTable(string tableName, out IReadOnlyList<float> values)
    {
        if (_namedTables.TryGetValue(tableName, out values!))
        {
            return true;
        }

        foreach (var (name, tableValues) in _namedTables)
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
}
