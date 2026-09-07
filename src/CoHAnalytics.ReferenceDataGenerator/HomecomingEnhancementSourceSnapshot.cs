using System.Collections.ObjectModel;
using System.IO;

namespace CoHAnalytics.ReferenceDataGenerator;

/// <summary>
/// Immutable, source-fingerprinted Homecoming inputs shared by enhancement-promotion passes.
/// Mutable catalog documents and promoted outputs deliberately remain outside this snapshot.
/// </summary>
internal sealed class HomecomingEnhancementSourceSnapshot
{
    private const string ClassesMemberName = "bin/classes.bin";
    private const string MessageMemberName = "bin/clientmessages-en.bin";
    private readonly byte[] _classesBytes;

    private HomecomingEnhancementSourceSnapshot(
        HomecomingEnhancementSourceFingerprint fingerprint,
        HomecomingStaticDataSource source,
        HomecomingMessageStore messages,
        IReadOnlyList<HomecomingConcreteBoostRecord> concreteBoosts,
        IReadOnlyList<HomecomingBoostSetRecord> boostSets,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> powerDiscoveryBySourceId,
        IReadOnlyDictionary<string, HomecomingBoostSetDiscoveryRecord> discoverySets,
        IReadOnlyDictionary<string, HomecomingBoostResolverFactsDiscovery> resolverFactsBySourceId,
        byte[] classesBytes,
        HomecomingClassModTableDiscoveryResult classModTables)
    {
        Fingerprint = fingerprint;
        Source = source;
        Messages = messages;
        ConcreteBoosts = concreteBoosts;
        BoostSets = boostSets;
        DiscoveryBoosts = discoveryBoosts;
        PowerDiscoveryBySourceId = powerDiscoveryBySourceId;
        DiscoverySets = discoverySets;
        ResolverFactsBySourceId = resolverFactsBySourceId;
        _classesBytes = classesBytes;
        ClassModTables = classModTables;
    }

    internal HomecomingEnhancementSourceFingerprint Fingerprint { get; }

    internal HomecomingStaticDataSource Source { get; }

    internal HomecomingMessageStore Messages { get; }

    internal IReadOnlyList<HomecomingConcreteBoostRecord> ConcreteBoosts { get; }

    internal IReadOnlyList<HomecomingBoostSetRecord> BoostSets { get; }

    internal IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> DiscoveryBoosts { get; }

    internal IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> PowerDiscoveryBySourceId { get; }

    internal IReadOnlyDictionary<string, HomecomingBoostSetDiscoveryRecord> DiscoverySets { get; }

    internal IReadOnlyDictionary<string, HomecomingBoostResolverFactsDiscovery> ResolverFactsBySourceId { get; }

    internal HomecomingClassModTableDiscoveryResult ClassModTables { get; }

    internal HomecomingEnhancementSourceSnapshotDiagnostics Diagnostics { get; } = new(
        SourceParseCount: 1,
        PiggDirectoryParseCount: 2);

    internal static HomecomingEnhancementSourceSnapshot Load(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        var source = HomecomingStaticDataSourceDiscovery.Discover(installRoot);
        var beforeFingerprint = HomecomingEnhancementSourceFingerprint.Capture(source);
        var binIndex = HomecomingPiggArchiveIndex.Open(source.BinPiggPath);
        var powersIndex = HomecomingPiggArchiveIndex.Open(source.BinPowersPiggPath);

        var messages = HomecomingMessageStoreReader.Read(binIndex.ReadMember(MessageMemberName));
        var powersBytes = powersIndex.ReadMember(HomecomingEnhancementCandidateGenerator.PowersMember);
        var boostSetBytes = binIndex.ReadMember(HomecomingEnhancementCandidateGenerator.BoostSetsMember);
        var classesBytes = binIndex.ReadMember(ClassesMemberName);

        var concreteBoosts = HomecomingPowersReader.ReadBoosts(powersBytes).ToArray();
        var boostSets = HomecomingBoostSetsReader.Read(boostSetBytes).ToArray();
        var allPowerDiscovery = HomecomingPowersBoostDiscoveryReader.ReadBoosts(powersBytes).ToArray();
        var powerDiscoveryBySourceId = new ReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord>(
            allPowerDiscovery.ToDictionary(value => value.SourceId, StringComparer.Ordinal));
        var discoveryBoosts = new ReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord>(
            allPowerDiscovery
                .Where(value => value.SourceId.StartsWith("Boosts.", StringComparison.Ordinal))
                .ToDictionary(value => value.SourceId, StringComparer.Ordinal));
        var discoverySets = new ReadOnlyDictionary<string, HomecomingBoostSetDiscoveryRecord>(
            HomecomingBoostSetsDiscoveryReader.Read(boostSetBytes)
                .ToDictionary(value => value.HomecomingSetId, StringComparer.Ordinal));
        var resolverSourceIds = allPowerDiscovery
            .Where(value =>
                value.SourceId.StartsWith("Boosts.", StringComparison.Ordinal)
                || value.SourceId.StartsWith("Set_Bonus.", StringComparison.Ordinal))
            .Select(value => value.SourceId)
            .ToArray();
        var resolverFactsBySourceId = HomecomingBoostResolverFactsDiscoveryReader.ReadForSourceIds(
            powersBytes,
            resolverSourceIds);
        var classModTables = HomecomingClassModTableDiscoveryReader.Read(classesBytes);

        var afterFingerprint = HomecomingEnhancementSourceFingerprint.Capture(
            HomecomingStaticDataSourceDiscovery.Discover(source.InstallRoot));
        if (beforeFingerprint != afterFingerprint)
        {
            throw new HomecomingEnhancementPromotionException(
                "Homecoming installation changed while the enhancement source snapshot was loading.");
        }

        return new HomecomingEnhancementSourceSnapshot(
            beforeFingerprint,
            source,
            messages,
            Array.AsReadOnly(concreteBoosts),
            Array.AsReadOnly(boostSets),
            discoveryBoosts,
            powerDiscoveryBySourceId,
            discoverySets,
            resolverFactsBySourceId,
            classesBytes,
            classModTables);
    }

    internal void EnsureSourceUnchanged()
    {
        var currentSource = HomecomingStaticDataSourceDiscovery.Discover(Source.InstallRoot);
        var currentFingerprint = HomecomingEnhancementSourceFingerprint.Capture(currentSource);
        if (Fingerprint != currentFingerprint)
        {
            throw new HomecomingEnhancementPromotionException(
                "Homecoming installation changed after the enhancement source snapshot was created.");
        }
    }

    internal IReadOnlyDictionary<string, IReadOnlyList<float>> PromoteNamedTables(
        IReadOnlyCollection<string> referencedTableNames) =>
        HomecomingEnhancementResolverPromotionSupport.PromoteNamedTables(
            _classesBytes,
            ClassModTables,
            referencedTableNames);
}

internal sealed record HomecomingEnhancementSourceSnapshotDiagnostics(
    int SourceParseCount,
    int PiggDirectoryParseCount);

internal sealed record HomecomingEnhancementSourceFingerprint(
    string InstallRoot,
    string BuildVersion,
    string PackageRevision,
    HomecomingEnhancementSourceFileFingerprint BinPigg,
    HomecomingEnhancementSourceFileFingerprint BinPowersPigg,
    HomecomingEnhancementSourceFileFingerprint LivePackageMetadata,
    HomecomingEnhancementSourceFileFingerprint LiveDataPackageMetadata)
{
    internal static HomecomingEnhancementSourceFingerprint Capture(HomecomingStaticDataSource source) =>
        new(
            source.InstallRoot,
            source.BuildVersion,
            source.PackageRevision,
            HomecomingEnhancementSourceFileFingerprint.Capture(source.BinPiggPath),
            HomecomingEnhancementSourceFileFingerprint.Capture(source.BinPowersPiggPath),
            HomecomingEnhancementSourceFileFingerprint.Capture(Path.Combine(
                source.InstallRoot,
                HomecomingStaticDataSourceDiscovery.LivePackageMetadataRelativePath)),
            HomecomingEnhancementSourceFileFingerprint.Capture(Path.Combine(
                source.InstallRoot,
                HomecomingStaticDataSourceDiscovery.LiveDataPackageMetadataRelativePath)));
}

internal sealed record HomecomingEnhancementSourceFileFingerprint(
    string CanonicalPath,
    long Length,
    long LastWriteTimeUtcTicks)
{
    internal static HomecomingEnhancementSourceFileFingerprint Capture(string path)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists)
        {
            throw new HomecomingEnhancementPromotionException(
                $"Homecoming source file '{file.FullName}' was not found while fingerprinting.");
        }

        return new HomecomingEnhancementSourceFileFingerprint(
            file.FullName,
            file.Length,
            file.LastWriteTimeUtc.Ticks);
    }
}
