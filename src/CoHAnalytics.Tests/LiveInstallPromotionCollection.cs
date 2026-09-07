using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveInstallPromotionCollection : ICollectionFixture<LiveInstallPromotionFixture>
{
    public const string Name = "Live-install promotion";
}

public sealed class LiveInstallPromotionFixture
{
    private readonly Lazy<HomecomingEnhancementSourceSnapshot> _sourceSnapshot;
    private int _sourceLoadCount;

    public LiveInstallPromotionFixture()
    {
        _sourceSnapshot = new Lazy<HomecomingEnhancementSourceSnapshot>(
            LoadSourceSnapshot,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal int SourceLoadCount => Volatile.Read(ref _sourceLoadCount);

    internal HomecomingEnhancementSourceSnapshotDiagnostics Diagnostics =>
        _sourceSnapshot.Value.Diagnostics;

    internal HomecomingEnhancementPromotionResult Promote(string catalogPath) =>
        HomecomingEnhancementPromotionCommand.Promote(_sourceSnapshot.Value, catalogPath);

    private HomecomingEnhancementSourceSnapshot LoadSourceSnapshot()
    {
        Interlocked.Increment(ref _sourceLoadCount);
        return HomecomingEnhancementSourceSnapshot.Load(LiveInstallTestEnvironment.InstallRoot);
    }
}
