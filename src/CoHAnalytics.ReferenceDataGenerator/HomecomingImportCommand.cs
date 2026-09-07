using System.IO;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingImportCommand
{
    internal static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryParseOptions(args, out var options, out var failureReason))
        {
            error.WriteLine(failureReason);
            WriteUsage(error);
            return 2;
        }

        try
        {
            var source = HomecomingStaticDataSourceDiscovery.Discover(options.InstallRoot);

            output.WriteLine($"Homecoming install: {source.InstallRoot}");
            output.WriteLine($"Build: {source.BuildVersion}");
            output.WriteLine($"Package revision: {source.PackageRevision}");
            output.WriteLine($"bin.pigg: found ({source.BinPiggPath})");
            output.WriteLine($"bin_powers.pigg: found ({source.BinPowersPiggPath})");

            const string messageMemberName = "bin/clientmessages-en.bin";
            var messageMember = HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, messageMemberName);
            var messages = HomecomingMessageStoreReader.Read(messageMember);

            const string salvageMemberName = "bin/salvage.bin";
            var salvageMember = HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, salvageMemberName);
            var salvageRecords = HomecomingSalvageReader.Read(salvageMember);
            var currentSalvage = HomecomingSalvageCandidateGenerator.LoadEmbeddedCurrentCatalog();
            var candidate = HomecomingSalvageCandidateGenerator.Create(
                salvageRecords,
                messages,
                currentSalvage,
                source.BuildVersion,
                source.PackageRevision);

            const string powersMemberName = "bin/powers.bin";
            var powersMember = HomecomingPiggMemberReader.ReadMember(
                source.BinPowersPiggPath,
                powersMemberName);
            var powerIdentities = HomecomingPowersReader.ReadIdentities(powersMember);

            const string boostSetsMemberName = "bin/boostsets.bin";
            var boostSetsMember = HomecomingPiggMemberReader.ReadMember(
                source.BinPiggPath,
                boostSetsMemberName);
            var boostSets = HomecomingBoostSetsReader.Read(boostSetsMember);
            var discoveryBoosts = HomecomingPowersBoostDiscoveryReader.ReadBoosts(powersMember)
                .Where(value => value.SourceId.StartsWith("Boosts.", StringComparison.Ordinal))
                .ToDictionary(value => value.SourceId, StringComparer.Ordinal);
            var currentEnhancements = HomecomingEnhancementCandidateGenerator
                .LoadEmbeddedCurrentCatalog();
            var enhancementCandidate = HomecomingEnhancementCandidateGenerator.Create(
                powerIdentities.Boosts,
                boostSets,
                messages,
                discoveryBoosts,
                currentEnhancements.Enhancements,
                currentEnhancements.EnhancementSets,
                source.BuildVersion,
                source.PackageRevision);
            var enhancementOutputPath = HomecomingEnhancementCandidateWriter.GetOutputPath(
                options.OutputPath);
            var currentInspirations = HomecomingInspirationCandidateGenerator
                .LoadEmbeddedCurrentCatalog();
            var inspirationCandidate = HomecomingInspirationCandidateGenerator.Create(
                powerIdentities.Inspirations,
                messages,
                currentInspirations,
                source.BuildVersion,
                source.PackageRevision);
            var inspirationOutputPath = HomecomingInspirationCandidateWriter.GetOutputPath(
                options.OutputPath);
            const string baseRecipesMemberName = "bin/baserecipes.bin";
            var baseRecipesMember = HomecomingPiggMemberReader.ReadMember(
                source.BinPiggPath,
                baseRecipesMemberName);
            var baseRecipes = HomecomingBaseRecipesReader.Read(baseRecipesMember);
            var currentRecipes = HomecomingRecipeCandidateGenerator.LoadEmbeddedCurrentCatalog();
            var recipeCandidate = HomecomingRecipeCandidateGenerator.Create(
                baseRecipes,
                messages,
                candidate,
                enhancementCandidate,
                currentRecipes,
                source.BuildVersion,
                source.PackageRevision);
            var recipeOutputPath = HomecomingRecipeCandidateWriter.GetOutputPath(
                options.OutputPath);
            const string badgesMemberName = "bin/badges.bin";
            var badgesMember = HomecomingPiggMemberReader.ReadMember(
                source.BinPiggPath,
                badgesMemberName);
            var badgeRecords = HomecomingBadgesReader.Read(badgesMember);
            var currentBadges = HomecomingBadgeCandidateGenerator.LoadEmbeddedCurrentCatalog();
            var badgeCandidate = HomecomingBadgeCandidateGenerator.Create(
                badgeRecords,
                messages,
                currentBadges,
                source.BuildVersion,
                source.PackageRevision);
            var badgeOutputPath = HomecomingBadgeCandidateWriter.GetOutputPath(
                options.OutputPath);

            HomecomingSalvageCandidateWriter.Write(options.OutputPath, candidate);
            HomecomingEnhancementCandidateWriter.Write(
                enhancementOutputPath,
                enhancementCandidate);
            HomecomingInspirationCandidateWriter.Write(
                inspirationOutputPath,
                inspirationCandidate);
            HomecomingRecipeCandidateWriter.Write(
                recipeOutputPath,
                recipeCandidate);
            HomecomingBadgeCandidateWriter.Write(
                badgeOutputPath,
                badgeCandidate);

            output.WriteLine($"{messageMemberName}: found ({messageMember.Length} bytes)");
            output.WriteLine($"Message count: {messages.Count}");
            output.WriteLine("Message-store validation: PASS");
            output.WriteLine($"{salvageMemberName}: found ({salvageMember.Length} bytes)");
            output.WriteLine($"Salvage records: {candidate.Summary.SalvageRecords}");
            output.WriteLine($"Resolved names: {candidate.Summary.ResolvedNames}");
            output.WriteLine($"Matched existing: {candidate.Summary.MatchedExisting}");
            output.WriteLine($"New from Homecoming: {candidate.Summary.NewFromHomecoming}");
            output.WriteLine($"Current catalog not matched: {candidate.Summary.CurrentCatalogNotMatched}");
            output.WriteLine($"Ambiguous: {candidate.Summary.Ambiguous}");
            output.WriteLine($"Candidate output: {options.OutputPath}");
            output.WriteLine("Salvage candidate generation: PASS");
            output.WriteLine($"{powersMemberName}: found ({powersMember.Length} bytes)");
            output.WriteLine($"Concrete Boost records: {enhancementCandidate.EnhancementSummary.ConcreteBoostRecords}");
            output.WriteLine($"Concrete Boost names resolved: {enhancementCandidate.EnhancementSummary.ResolvedConcreteBoostNames}");
            output.WriteLine($"Logical Enhancements: {enhancementCandidate.EnhancementSummary.LogicalEnhancements}");
            output.WriteLine($"Multi-variant Enhancements: {enhancementCandidate.EnhancementSummary.MultiVariantLogicalEnhancements}");
            output.WriteLine($"Enhancements matched existing: {enhancementCandidate.EnhancementSummary.MatchedExisting}");
            output.WriteLine($"Enhancements new from Homecoming: {enhancementCandidate.EnhancementSummary.NewFromHomecoming}");
            output.WriteLine($"Current Enhancements not matched: {enhancementCandidate.EnhancementSummary.CurrentCatalogNotMatched}");
            output.WriteLine($"Enhancements ambiguous: {enhancementCandidate.EnhancementSummary.Ambiguous}");
            output.WriteLine($"{boostSetsMemberName}: found ({boostSetsMember.Length} bytes)");
            output.WriteLine($"Enhancement Sets: {enhancementCandidate.EnhancementSetSummary.HomecomingSets}");
            output.WriteLine($"Enhancement Set names resolved: {enhancementCandidate.EnhancementSetSummary.ResolvedNames}");
            output.WriteLine($"Enhancement Sets matched existing: {enhancementCandidate.EnhancementSetSummary.MatchedExisting}");
            output.WriteLine($"Enhancement Sets new from Homecoming: {enhancementCandidate.EnhancementSetSummary.NewFromHomecoming}");
            output.WriteLine($"Current Enhancement Sets not matched: {enhancementCandidate.EnhancementSetSummary.CurrentCatalogNotMatched}");
            output.WriteLine($"Enhancement Sets ambiguous: {enhancementCandidate.EnhancementSetSummary.Ambiguous}");
            output.WriteLine($"Enhancement Set rarity codes: {FormatCodeCounts(enhancementCandidate.EnhancementSetSummary.RarityCodeCounts)}");
            output.WriteLine($"Enhancement Set category codes: {FormatCodeCounts(enhancementCandidate.EnhancementSetSummary.CategoryCodeCounts)}");
            output.WriteLine($"Enhancement candidate output: {enhancementOutputPath}");
            output.WriteLine("Enhancement candidate generation: PASS");
            output.WriteLine($"Homecoming Inspirations: {inspirationCandidate.Summary.HomecomingInspirations}");
            output.WriteLine($"Inspiration names resolved: {inspirationCandidate.Summary.ResolvedNames}");
            output.WriteLine($"Inspirations matched existing: {inspirationCandidate.Summary.MatchedExisting}");
            output.WriteLine($"Inspirations new from Homecoming: {inspirationCandidate.Summary.NewFromHomecoming}");
            output.WriteLine($"Current Inspirations not matched: {inspirationCandidate.Summary.CurrentCatalogNotMatched}");
            output.WriteLine($"Inspirations ambiguous: {inspirationCandidate.Summary.Ambiguous}");
            output.WriteLine($"Inspiration candidate output: {inspirationOutputPath}");
            output.WriteLine("Inspiration candidate generation: PASS");
            output.WriteLine($"{baseRecipesMemberName}: found ({baseRecipesMember.Length} bytes)");
            output.WriteLine($"Base Recipe records: {recipeCandidate.Summary.TotalBaseRecipeRecords}");
            output.WriteLine($"Crafting Recipes: {recipeCandidate.Summary.CraftingRecipes}");
            output.WriteLine($"Recipe names resolved: {recipeCandidate.Summary.ResolvedNames}");
            output.WriteLine($"Recipe product joins: {recipeCandidate.Summary.CompletedProductJoins}");
            output.WriteLine($"Case-insensitive product joins: {recipeCandidate.Summary.CaseInsensitiveProductJoinsUsed}");
            output.WriteLine($"Assigned Enhancement IDs: {recipeCandidate.Summary.NewlyAssignedEnhancementIds}");
            output.WriteLine($"Salvage requirement rows: {recipeCandidate.Summary.SalvageRequirementRows}");
            output.WriteLine($"Recipe candidates: {recipeCandidate.Summary.RecipeCandidates}");
            output.WriteLine($"Recipe candidate output: {recipeOutputPath}");
            output.WriteLine("Recipe candidate generation: PASS");
            output.WriteLine($"{badgesMemberName}: found ({badgesMember.Length} bytes)");
            output.WriteLine($"Live Badge records: {badgeCandidate.Summary.TotalLiveBadgeRecords}");
            output.WriteLine($"Player-facing Badges: {badgeCandidate.Summary.PlayerFacingBadgeRecords}");
            output.WriteLine($"Badge names resolved: {badgeCandidate.Summary.NamesResolved}");
            output.WriteLine($"Badges with descriptions: {badgeCandidate.Summary.RecordsWithResolvedDescriptions}");
            output.WriteLine($"Badge categories: {badgeCandidate.Summary.CategoryCount}");
            output.WriteLine($"Badge category distribution: {FormatBadgeCategoryCounts(badgeCandidate.Summary.CategoryDistribution)}");
            output.WriteLine($"Badges with icons: {badgeCandidate.Summary.RecordsWithIcons}");
            output.WriteLine($"Badges matched existing: {badgeCandidate.Summary.MatchedExisting}");
            output.WriteLine($"Badges new from Homecoming: {badgeCandidate.Summary.NewFromHomecoming}");
            output.WriteLine($"Current Badges not matched: {badgeCandidate.Summary.CurrentCatalogNotMatched}");
            output.WriteLine($"Badges ambiguous: {badgeCandidate.Summary.Ambiguous}");
            output.WriteLine($"Badge candidate output: {badgeOutputPath}");
            output.WriteLine("Badge candidate generation: PASS");
            output.WriteLine("Importer source validation: PASS");
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error.WriteLine("Importer source validation: FAIL");
            error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string FormatCodeCounts(IReadOnlyList<HomecomingEnhancementCodeCount> counts) =>
        string.Join(
            ", ",
            counts.Select(value => $"{value.Code ?? "<missing>"}={value.Count}"));

    private static string FormatBadgeCategoryCounts(
        IReadOnlyList<HomecomingBadgeCategoryCount> counts) =>
        string.Join(", ", counts.Select(value => $"{value.Category}={value.Count}"));

    internal static void WriteUsage(TextWriter writer) =>
        writer.WriteLine("Usage: import-homecoming --install <HomecomingRoot> --output <candidatePath>");

    private static bool TryParseOptions(
        IReadOnlyList<string> args,
        out ImportOptions options,
        out string failureReason)
    {
        options = null!;
        failureReason = string.Empty;
        string? installRoot = null;
        string? outputPath = null;

        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (option is not ("--install" or "--output"))
            {
                failureReason = $"Unknown import-homecoming option '{option}'.";
                return false;
            }

            if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
            {
                failureReason = $"Option '{option}' requires a value.";
                return false;
            }

            if (option == "--install")
            {
                if (installRoot is not null)
                {
                    failureReason = "Option '--install' may be specified only once.";
                    return false;
                }

                installRoot = args[index];
            }
            else
            {
                if (outputPath is not null)
                {
                    failureReason = "Option '--output' may be specified only once.";
                    return false;
                }

                outputPath = args[index];
            }
        }

        if (installRoot is null)
        {
            failureReason = "Option '--install' is required.";
            return false;
        }

        if (outputPath is null)
        {
            failureReason = "Option '--output' is required.";
            return false;
        }

        try
        {
            outputPath = Path.GetFullPath(outputPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            failureReason = $"Candidate output path is invalid: {exception.Message}";
            return false;
        }

        options = new ImportOptions(installRoot, outputPath);
        return true;
    }

    private sealed record ImportOptions(string InstallRoot, string OutputPath);
}
