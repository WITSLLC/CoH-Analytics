using CoHAnalytics.Homecoming;
using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Observations;
using CoHAnalytics.Services.Diagnostics;
using CoHAnalytics.Updates;

namespace CoHAnalytics.Services;

public sealed class AppServices : IDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);

    private readonly HomecomingRuntimeContributor _runtimeContributor;
    private readonly LogActivityContributor _logActivityContributor;
    private readonly MonitoringSessionManagerContributor _monitoringSessionManagerContributor;
    private readonly ParserHealthContributor _parserHealthContributor;
    private readonly GameplaySessionContributor _gameplaySessionContributor;
    private readonly AcquisitionObservationContributor _acquisitionObservationContributor;
    private readonly LiveRuntimeGenerationService _liveRuntimeGenerationService;
    private bool _disposed;
    private bool _orchestratorStarted;

    private AppServices(
        SettingsService settingsService,
        HomecomingInstallationService homecomingInstallationService,
        HomecomingAccountDiscoveryService accountDiscoveryService,
        MidsInstallationService midsInstallationService,
        HomecomingLauncherService launcherService,
        HomecomingRuntimeService runtimeService,
        LogActivityService logActivityService,
        MonitoringSessionManager monitoringSessionManager,
        CharacterRepository characterRepository,
        GameplaySessionManager gameplaySessionManager,
        GameplaySessionIdentityReadService gameplaySessionIdentityReadService,
        ViewedContextService viewedContextService,
        GameplaySessionContextResolver gameplaySessionContextResolver,
        ApplicationActivityLogService activityLogService,
        IDiagnosticLog diagnosticLog,
        ParserClassifier parserClassifier,
        ParserManager parserManager,
        ApplicationOrchestrator orchestrator,
        HomecomingRuntimeContributor runtimeContributor,
        LogActivityContributor logActivityContributor,
        MonitoringSessionManagerContributor monitoringSessionManagerContributor,
        ParserHealthContributor parserHealthContributor,
        GameplaySessionContributor gameplaySessionContributor,
        AcquisitionObservationContributor acquisitionObservationContributor,
        IItemReferenceCatalog itemReferenceCatalog,
        IAcquisitionObservationService acquisitionObservationService,
        AcquisitionClassificationService acquisitionClassificationService,
        IInternalFeatureGate internalFeatureGate,
        AccountAnonymityService accountAnonymityService,
        IInstalledGameAssetProvider installedGameAssetProvider,
        IEnhancementIconCompositor enhancementIconCompositor,
        IHomecomingBoostMetadataProvider boostMetadataProvider,
        ISessionStore sessionStore,
        LiveRuntimeGenerationService liveRuntimeGenerationService,
        CharacterBadgeAcquisitionRepository characterBadgeAcquisitionRepository,
        CharacterPerformanceObservationRepository characterPerformanceObservationRepository,
        CharacterHistoricalPerformanceReadService characterHistoricalPerformanceReadService,
        CharacterBuildImportService characterBuildImportService,
        BuiltInCharacterIconService builtInCharacterIconService,
        CustomCharacterIconService customCharacterIconService,
        IDiagnosticsReportService diagnosticsReportService,
        IGitHubReleaseClient gitHubReleaseClient,
        IUpdateCheckService updateCheckService)
    {
        SettingsService = settingsService;
        HomecomingInstallationService = homecomingInstallationService;
        AccountDiscoveryService = accountDiscoveryService;
        MidsInstallationService = midsInstallationService;
        LauncherService = launcherService;
        RuntimeService = runtimeService;
        LogActivityService = logActivityService;
        MonitoringSessionManager = monitoringSessionManager;
        CharacterRepository = characterRepository;
        GameplaySessionManager = gameplaySessionManager;
        GameplaySessionIdentityReadService = gameplaySessionIdentityReadService;
        ViewedContextService = viewedContextService;
        GameplaySessionContextResolver = gameplaySessionContextResolver;
        ActivityLogService = activityLogService;
        DiagnosticLog = diagnosticLog;
        ParserClassifier = parserClassifier;
        ParserManager = parserManager;
        Orchestrator = orchestrator;
        _runtimeContributor = runtimeContributor;
        _logActivityContributor = logActivityContributor;
        _monitoringSessionManagerContributor = monitoringSessionManagerContributor;
        _parserHealthContributor = parserHealthContributor;
        _gameplaySessionContributor = gameplaySessionContributor;
        _acquisitionObservationContributor = acquisitionObservationContributor;
        ItemReferenceCatalog = itemReferenceCatalog;
        AcquisitionObservationService = acquisitionObservationService;
        AcquisitionClassificationService = acquisitionClassificationService;
        InternalFeatureGate = internalFeatureGate;
        AccountAnonymityService = accountAnonymityService;
        SessionStore = sessionStore;
        InstalledGameAssetProvider = installedGameAssetProvider;
        EnhancementIconCompositor = enhancementIconCompositor;
        BoostMetadataProvider = boostMetadataProvider;
        CharacterBadgeAcquisitionRepository = characterBadgeAcquisitionRepository;
        CharacterPerformanceObservationRepository = characterPerformanceObservationRepository;
        CharacterHistoricalPerformanceReadService = characterHistoricalPerformanceReadService;
        CharacterBuildImportService = characterBuildImportService;
        BuiltInCharacterIconService = builtInCharacterIconService;
        CustomCharacterIconService = customCharacterIconService;
        DiagnosticsReportService = diagnosticsReportService;
        GitHubReleaseClient = gitHubReleaseClient;
        UpdateCheckService = updateCheckService;
        _liveRuntimeGenerationService = liveRuntimeGenerationService;
    }

    public SettingsService SettingsService { get; }

    public HomecomingInstallationService HomecomingInstallationService { get; }

    public HomecomingAccountDiscoveryService AccountDiscoveryService { get; }

    public MidsInstallationService MidsInstallationService { get; }

    public HomecomingLauncherService LauncherService { get; }

    public HomecomingRuntimeService RuntimeService { get; }

    /// <summary>
    /// Observed chat-log activity. Exposed for the future monitoring session manager, which
    /// consumes the domain snapshot directly rather than through the orchestrator.
    /// </summary>
    public LogActivityService LogActivityService { get; }

    /// <summary>
    /// Owns monitoring contexts and their runtime-aware suspension/resumption. Exposed for later
    /// Slice 5 and Live Session work, which will consume the domain manager directly rather than
    /// through the orchestrator.
    /// </summary>
    public MonitoringSessionManager MonitoringSessionManager { get; }

    /// <summary>
    /// Persistent account-scoped trusted character records. Exposed for Slice 7 gameplay-session
    /// and identity work, which consume the repository directly rather than through the
    /// orchestrator.
    /// </summary>
    public CharacterRepository CharacterRepository { get; }

    /// <summary>
    /// Owns per-context gameplay sessions and runtime character identity. Lifecycle is owned by
    /// <see cref="GameplaySessionContributor"/>; the composition root constructs and disposes the
    /// manager.
    /// </summary>
    public GameplaySessionManager GameplaySessionManager { get; }

    /// <summary>
    /// UI-facing runtime identity read model for Live Session and Accounts reflection.
    /// </summary>
    public GameplaySessionIdentityReadService GameplaySessionIdentityReadService { get; }

    /// <summary>
    /// Shell-level viewed navigation context for character-aware workspaces.
    /// </summary>
    public ViewedContextService ViewedContextService { get; }

    /// <summary>
    /// Authoritative gameplay-session context selection for session-scoped workspaces.
    /// </summary>
    public GameplaySessionContextResolver GameplaySessionContextResolver { get; }

    /// <summary>
    /// Persistent, curated application lifecycle history shown by the Dashboard.
    /// </summary>
    public ApplicationActivityLogService ActivityLogService { get; }

    /// <summary>Persistent structured diagnostics for application lifecycle and supportability.</summary>
    public IDiagnosticLog DiagnosticLog { get; }

    /// <summary>Creates sanitized user-saved diagnostics ZIP reports for beta support.</summary>
    public IDiagnosticsReportService DiagnosticsReportService { get; }

    /// <summary>
    /// Owns per-context parser workers and structural classifications. The parser contributor
    /// owns its start/stop lifecycle; downstream Slice 7 consumers use this domain service.
    /// </summary>
    public IParserManager ParserManager { get; }

    public IParserClassifier ParserClassifier { get; }

    public ApplicationOrchestrator Orchestrator { get; }

    /// <summary>
    /// App-owned item reference catalog loaded from the generated production SQLite package at startup.
    /// Future Inventory, telemetry, and analytics features resolve observed item text here.
    /// The local Mids taxonomy remains a secondary fallback for identities outside this catalog.
    /// </summary>
    public IItemReferenceCatalog ItemReferenceCatalog { get; }

    /// <summary>Unresolved acquisition observation capture for developer diagnostics.</summary>
    public IAcquisitionObservationService AcquisitionObservationService { get; }

    /// <summary>Gated, observation-first reference classification commands for Developer Tools.</summary>
    public AcquisitionClassificationService AcquisitionClassificationService { get; }

    /// <summary>Hidden internal feature gate; sole interpreter of developer-mode activation.</summary>
    public IInternalFeatureGate InternalFeatureGate { get; }

    /// <summary>Transient presentation-only account-name masking; never persisted.</summary>
    public AccountAnonymityService AccountAnonymityService { get; }

    /// <summary>
    /// Persistent Live, Tracked, and Saved session library for Analytics and Live Session promotion.
    /// </summary>
    public ISessionStore SessionStore { get; }

    public IInstalledGameAssetProvider InstalledGameAssetProvider { get; }

    public IEnhancementIconCompositor EnhancementIconCompositor { get; }

    public IHomecomingBoostMetadataProvider BoostMetadataProvider { get; }

    /// <summary>
    /// Persistent per-character badge acquisition completions resolved against the reference catalog.
    /// </summary>
    public CharacterBadgeAcquisitionRepository CharacterBadgeAcquisitionRepository { get; }

    /// <summary>Immutable historical character-performance observation storage.</summary>
    public CharacterPerformanceObservationRepository CharacterPerformanceObservationRepository { get; }

    /// <summary>Lifetime character-performance query and read-model service.</summary>
    public CharacterHistoricalPerformanceReadService CharacterHistoricalPerformanceReadService { get; }

    public CharacterBuildImportService CharacterBuildImportService { get; }

    public BuiltInCharacterIconService BuiltInCharacterIconService { get; }

    public CustomCharacterIconService CustomCharacterIconService { get; }

    public IGitHubReleaseClient GitHubReleaseClient { get; }

    public IUpdateCheckService UpdateCheckService { get; }

    public static AppServices Create()
    {
        var applicationDataRoot = ApplicationDataPaths.GetApplicationRoot();
        var diagnosticLog = new DiagnosticLogService(applicationDataRoot);
        diagnosticLog.Write(new ApplicationRunStartedDiagnosticEvent
        {
            ApplicationVersion = ApplicationMetadata.Version,
            ProcessId = Environment.ProcessId
        });

        var settingsService = new SettingsService();
        IInternalFeatureGate internalFeatureGate = new InternalFeatureGate(settingsService);
        var accountAnonymityService = new AccountAnonymityService(internalFeatureGate);
        var homecomingInstallationService = new HomecomingInstallationService(settingsService);
        var accountDiscoveryService = new HomecomingAccountDiscoveryService(homecomingInstallationService);
        var midsInstallationService = new MidsInstallationService(settingsService);
        var launcherService = new HomecomingLauncherService(settingsService, homecomingInstallationService);
        var logActivityService = new LogActivityService(
            accountDiscoveryService,
            diagnosticLog: diagnosticLog);
        var runtimeService = new HomecomingRuntimeService(
            homecomingInstallationService,
            launcherService,
            diagnosticLog,
            () => logActivityService.Current);
        var monitoringSessionManager = new MonitoringSessionManager(
            runtimeService,
            logActivityService,
            diagnosticLog: diagnosticLog);
        var characterRepository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = applicationDataRoot
        });
        var characterBadgeAcquisitionRepository = new CharacterBadgeAcquisitionRepository(
            new CharacterBadgeAcquisitionRepositoryOptions
            {
                DataDirectory = applicationDataRoot
            });
        var characterPerformanceObservationRepository =
            new CharacterPerformanceObservationRepository(applicationDataRoot);
        var characterHistoricalPerformanceReadService =
            new CharacterHistoricalPerformanceReadService(
                characterPerformanceObservationRepository,
                characterRepository);
        var parserClassifier = new ParserClassifier();
        var parserManager = new ParserManager(
            monitoringSessionManager,
            classifier: parserClassifier,
            diagnosticLog: diagnosticLog,
            runningClientsProvider: () => runtimeService.RunningClients,
            logActivitySnapshotProvider: () => logActivityService.Current);

        homecomingInstallationService.DiscoverAndPersist();
        accountDiscoveryService.Discover();
        midsInstallationService.DiscoverAndPersist();

        var archetypePowerCatalog = new HomecomingArchetypePowerReferenceCatalog(homecomingInstallationService);
        var characterBuildImportService = new CharacterBuildImportService(
            characterRepository,
            archetypePowerCatalog);

        var itemReferenceCatalog = ItemReferenceCatalogFactory.LoadProductionDatabase();
        var activityLogService = new ApplicationActivityLogService();

        var acquisitionObservationService = new AcquisitionObservationService(
            itemReferenceCatalog,
            activityLogService: activityLogService);
        var acquisitionClassificationService = new AcquisitionClassificationService(
            internalFeatureGate,
            acquisitionObservationService,
            itemReferenceCatalog);

        var midsTaxonomyCatalog = MidsHomecomingReceivedItemTaxonomyCatalog.TryLoadFromHomecomingDatabasePath(
            midsInstallationService.CurrentInstallation?.HomecomingDatabasePath);
        var receivedItemTaxonomyCatalog = new ChainedReceivedItemTaxonomyCatalog([
            new ItemReferenceReceivedItemTaxonomyCatalog(itemReferenceCatalog),
            midsTaxonomyCatalog
        ]);
        var receivedItemClassifier = new ObservingReceivedItemClassifier(
            new GameplayReceivedItemClassifier(receivedItemTaxonomyCatalog, itemReferenceCatalog),
            acquisitionObservationService,
            () => itemReferenceCatalog.Manifest?.CatalogVersion);

        var badgeAcquisitionResolver = new BadgeAcquisitionResolver(itemReferenceCatalog);

        var gameplaySessionManager = new GameplaySessionManager(
            monitoringSessionManager,
            parserManager,
            characterRepository,
            receivedItemClassifier: receivedItemClassifier,
            badgeAcquisitionResolver: badgeAcquisitionResolver,
            badgeAcquisitionRepository: characterBadgeAcquisitionRepository,
            historicalObservationRepository: characterPerformanceObservationRepository,
            diagnosticLog: diagnosticLog);
        var gameplaySessionIdentityReadService = new GameplaySessionIdentityReadService(
            gameplaySessionManager,
            monitoringSessionManager,
            characterRepository);
        var viewedContextService = new ViewedContextService(
            gameplaySessionIdentityReadService,
            characterRepository);
        var gameplaySessionContextResolver = new GameplaySessionContextResolver(
            gameplaySessionIdentityReadService,
            viewedContextService);
        var liveRuntimeGenerationService = new LiveRuntimeGenerationService(
            runtimeService,
            monitoringSessionManager,
            gameplaySessionManager,
            viewedContextService);
        activityLogService.Record("application.started", "Application started");
        var sessionStore = new SessionStore();
        var installedGameAssetProvider = new InstalledGameAssetProvider(homecomingInstallationService);
        var boostMetadataProvider = new HomecomingBoostMetadataProvider(homecomingInstallationService);
        var enhancementIconCompositor = new EnhancementIconCompositor(installedGameAssetProvider);
        var builtInCharacterIconService = new BuiltInCharacterIconService();
        var customCharacterIconService = new CustomCharacterIconService(settingsService);

        runtimeService.Start();

        var orchestrator = new ApplicationOrchestrator();
        var installationContributor = new HomecomingInstallationContributor(homecomingInstallationService);
        var runtimeContributor = new HomecomingRuntimeContributor(runtimeService);
        var accountsContributor = new HomecomingAccountsContributor(accountDiscoveryService);
        var midsContributor = new MidsInstallationContributor(midsInstallationService);

        // The contributor lifecycle owns starting and stopping the log activity service and the
        // monitoring session manager, so neither is started here.
        var logActivityContributor = new LogActivityContributor(logActivityService);
        var monitoringSessionManagerContributor = new MonitoringSessionManagerContributor(monitoringSessionManager);
        var parserHealthContributor = new ParserHealthContributor(parserManager);
        var gameplaySessionContributor = new GameplaySessionContributor(gameplaySessionManager);
        var acquisitionObservationContributor = new AcquisitionObservationContributor(acquisitionObservationService);

        orchestrator.Register(installationContributor);
        orchestrator.Register(runtimeContributor);
        orchestrator.Register(accountsContributor);
        orchestrator.Register(midsContributor);
        orchestrator.Register(logActivityContributor);
        orchestrator.Register(monitoringSessionManagerContributor);
        orchestrator.Register(parserHealthContributor);
        orchestrator.Register(gameplaySessionContributor);
        orchestrator.Register(acquisitionObservationContributor);

        IDiagnosticsReportService diagnosticsReportService = new DiagnosticsReportService(
            applicationDataRoot,
            diagnosticLog,
            applicationVersionProvider: () => ApplicationMetadata.Version,
            runtimeClientCountProvider: () => runtimeService.RunningClients.Count,
            monitoringContextCountProvider: () => monitoringSessionManager.Current.Contexts.Count,
            parserSnapshotProvider: () =>
            {
                var diagnostics = parserManager.GetDiagnostics();
                var stateCounts = diagnostics.Workers
                    .GroupBy(worker => worker.State.ToString(), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
                return new DiagnosticsReportService.ParserReportSnapshot(
                    diagnostics.Workers.Count,
                    diagnostics.IsRunning,
                    stateCounts);
            },
            catalogVersionProvider: () => itemReferenceCatalog.Manifest?.CatalogVersion);

        var currentReleaseVersion = ApplicationMetadata.CurrentReleaseVersion
            ?? throw new InvalidOperationException("The application informational version is not a supported release version.");
        IGitHubReleaseClient gitHubReleaseClient = new GitHubReleaseClient(currentReleaseVersion.CanonicalText);
        IUpdateCheckService updateCheckService = new UpdateCheckService(
            gitHubReleaseClient,
            currentReleaseVersion,
            ApplicationMetadata.DeploymentType);

        return new AppServices(
            settingsService,
            homecomingInstallationService,
            accountDiscoveryService,
            midsInstallationService,
            launcherService,
            runtimeService,
            logActivityService,
            monitoringSessionManager,
            characterRepository,
            gameplaySessionManager,
            gameplaySessionIdentityReadService,
            viewedContextService,
            gameplaySessionContextResolver,
            activityLogService,
            diagnosticLog,
            parserClassifier,
            parserManager,
            orchestrator,
            runtimeContributor,
            logActivityContributor,
            monitoringSessionManagerContributor,
            parserHealthContributor,
            gameplaySessionContributor,
            acquisitionObservationContributor,
            itemReferenceCatalog,
            acquisitionObservationService,
            acquisitionClassificationService,
            internalFeatureGate,
            accountAnonymityService,
            installedGameAssetProvider,
            enhancementIconCompositor,
            boostMetadataProvider,
            sessionStore,
            liveRuntimeGenerationService,
            characterBadgeAcquisitionRepository,
            characterPerformanceObservationRepository,
            characterHistoricalPerformanceReadService,
            characterBuildImportService,
            builtInCharacterIconService,
            customCharacterIconService,
            diagnosticsReportService,
            gitHubReleaseClient,
            updateCheckService);
    }

    public async Task InitializeOrchestratorAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_orchestratorStarted)
        {
            return;
        }

        await Orchestrator.StartAsync(cancellationToken).ConfigureAwait(false);
        _orchestratorStarted = true;
    }

    public async Task ShutdownOrchestratorAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_orchestratorStarted)
        {
            return;
        }

        await Orchestrator.StopAsync(cancellationToken).ConfigureAwait(false);
        _orchestratorStarted = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop runtime polling before orchestrator shutdown so in-flight detection work
        // cannot marshal back to the UI thread while OnExit is blocked on teardown.
        RuntimeService.Dispose();

        try
        {
            using var shutdownCts = new CancellationTokenSource(ShutdownTimeout);
            ShutdownOrchestratorAsync(shutdownCts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
        }

        _runtimeContributor.Dispose();
        _logActivityContributor.Dispose();
        _monitoringSessionManagerContributor.Dispose();
        _parserHealthContributor.Dispose();
        _gameplaySessionContributor.Dispose();
        _acquisitionObservationContributor.Dispose();

        if (AcquisitionObservationService is IDisposable disposableObservationService)
        {
            disposableObservationService.Dispose();
        }

        try
        {
            Orchestrator.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
        }

        if (ParserManager is IDisposable parserManager)
        {
            parserManager.Dispose();
        }

        LogActivityService.Dispose();
        MonitoringSessionManager.Dispose();
        GameplaySessionManager.Dispose();
        GameplaySessionIdentityReadService.Dispose();
        _liveRuntimeGenerationService.Dispose();

        DiagnosticLog.Write(new ApplicationShutdownCompletedDiagnosticEvent());
        if (DiagnosticLog is IDisposable disposableDiagnosticLog)
        {
            disposableDiagnosticLog.Dispose();
        }

        if (GitHubReleaseClient is IDisposable disposableGitHubReleaseClient)
        {
            disposableGitHubReleaseClient.Dispose();
        }
    }
}
