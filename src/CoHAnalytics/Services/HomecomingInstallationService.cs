using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed class HomecomingInstallationService
{
    private readonly SettingsService _settingsService;

    public HomecomingInstallationService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public HomecomingInstallation? CurrentInstallation { get; private set; }

    public HomecomingInstallDiscoveryDiagnostics LastDiscovery { get; private set; } = new();

    public HomecomingInstallation? DiscoverAndPersist()
    {
        var diagnostics = new HomecomingInstallDiscoveryDiagnostics();
        var settings = _settingsService.Load();

        if (TrySelectValidatedCandidate(
                settings.HomecomingInstallPath,
                HomecomingDiscoverySource.Persisted,
                diagnostics,
                out var persistedInstallation))
        {
            return FinalizeDiscovery(diagnostics, settings, persistedInstallation);
        }

        var candidates = new List<DiscoveryCandidate>();
        var shortcutOrder = 0;

        TryAddCandidate(
            candidates,
            HomecomingPathRules.OfficialDefaultInstallRoot,
            HomecomingDiscoverySource.OfficialDefault,
            order: 0,
            diagnostics);

        foreach (var shortcut in ShortcutHelper.EnumerateDesktopShortcuts())
        {
            AddShortcutCandidates(candidates, shortcut, HomecomingDiscoverySource.DesktopShortcut, ref shortcutOrder, diagnostics);
        }

        foreach (var shortcut in ShortcutHelper.EnumerateStartMenuShortcuts())
        {
            AddShortcutCandidates(candidates, shortcut, HomecomingDiscoverySource.StartMenuShortcut, ref shortcutOrder, diagnostics);
        }

        var runningClientOrder = 0;
        foreach (var executablePath in ProcessInstallHintHelper.EnumerateRunningClientExecutablePaths())
        {
            if (HomecomingPathRules.TryDeriveInstallRootFromClientExecutable(executablePath, out var installRoot))
            {
                TryAddCandidate(
                    candidates,
                    installRoot,
                    HomecomingDiscoverySource.RunningClient,
                    runningClientOrder++,
                    diagnostics,
                    executablePath);
            }
            else
            {
                diagnostics.RecordAttempt(
                    HomecomingDiscoverySource.RunningClient,
                    executablePath,
                    isValid: false,
                    "Executable path does not match Homecoming client layout.");
            }
        }

        var runningLauncherOrder = 0;
        foreach (var executablePath in ProcessInstallHintHelper.EnumerateRunningLauncherExecutablePaths())
        {
            if (HomecomingPathRules.TryDeriveInstallRootFromLauncherExecutable(executablePath, out var installRoot))
            {
                TryAddCandidate(
                    candidates,
                    installRoot,
                    HomecomingDiscoverySource.RunningLauncher,
                    runningLauncherOrder++,
                    diagnostics,
                    executablePath);
            }
            else
            {
                diagnostics.RecordAttempt(
                    HomecomingDiscoverySource.RunningLauncher,
                    executablePath,
                    isValid: false,
                    "Executable path does not match Homecoming launcher layout.");
            }
        }

        var registryOrder = 0;
        foreach (var registryHint in HomecomingRegistryHintHelper.EnumerateInstallHints())
        {
            TryAddCandidate(
                candidates,
                registryHint,
                HomecomingDiscoverySource.Registry,
                registryOrder++,
                diagnostics);
        }

        var commonPathOrder = 0;
        foreach (var commonPath in HomecomingPathRules.CommonInstallationCandidates)
        {
            TryAddCandidate(
                candidates,
                commonPath,
                HomecomingDiscoverySource.CommonPath,
                commonPathOrder++,
                diagnostics);
        }

        if (!TrySelectBestCandidate(candidates, diagnostics, out var selectedInstallation))
        {
            CurrentInstallation = null;
            LastDiscovery = diagnostics;
            return null;
        }

        return FinalizeDiscovery(diagnostics, settings, selectedInstallation);
    }

    /// <summary>
    /// Validates and persists a user-selected install root using the same rules as startup discovery.
    /// Updates <see cref="CurrentInstallation"/> on success and leaves the prior installation unchanged on failure.
    /// </summary>
    public bool TryConfigureInstallRoot(string? candidatePath, out string? failureReason)
    {
        var diagnostics = new HomecomingInstallDiscoveryDiagnostics();

        if (!TrySelectValidatedCandidate(
                candidatePath,
                HomecomingDiscoverySource.Persisted,
                diagnostics,
                out var selected))
        {
            var normalizedCandidate = candidatePath?.Trim();
            var attempts = diagnostics.Attempts;
            failureReason = attempts
                .LastOrDefault(attempt =>
                    string.Equals(attempt.CandidatePath, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                .FailureReason
                ?? attempts.LastOrDefault().FailureReason
                ?? "The selected folder is not a valid Homecoming installation.";
            return false;
        }

        var settings = _settingsService.Load();
        FinalizeDiscovery(diagnostics, settings, selected);
        failureReason = null;
        return true;
    }

    private HomecomingInstallation FinalizeDiscovery(
        HomecomingInstallDiscoveryDiagnostics diagnostics,
        AppSettings settings,
        DiscoveryCandidate selected)
    {
        var launcherPath = HomecomingPathRules.ResolveLauncherPath(
            selected.InstallRoot,
            settings.HomecomingLauncherPath);
        var installation = new HomecomingInstallation
        {
            InstallRoot = selected.InstallRoot,
            LauncherPath = launcherPath
        };

        CurrentInstallation = installation;
        diagnostics.RecordSelection(installation.InstallRoot, selected.Source, installation.LauncherPath);
        LastDiscovery = diagnostics;
        PersistInstallation(settings, installation);
        return installation;
    }

    private static bool TrySelectValidatedCandidate(
        string? candidatePath,
        HomecomingDiscoverySource source,
        HomecomingInstallDiscoveryDiagnostics diagnostics,
        out DiscoveryCandidate installation)
    {
        installation = default;

        if (!HomecomingPathRules.TryValidateInstallRoot(candidatePath, out var normalizedRoot, out var failureReason))
        {
            diagnostics.RecordAttempt(source, candidatePath, isValid: false, failureReason);
            return false;
        }

        diagnostics.RecordAttempt(source, normalizedRoot, isValid: true, failureReason: null);
        installation = new DiscoveryCandidate(normalizedRoot, source, 0);
        return true;
    }

    private static void AddShortcutCandidates(
        List<DiscoveryCandidate> candidates,
        ShortcutDetails shortcut,
        HomecomingDiscoverySource source,
        ref int shortcutOrder,
        HomecomingInstallDiscoveryDiagnostics diagnostics)
    {
        var derivedRoots = ShortcutHelper.DeriveInstallRootCandidates(shortcut).ToList();
        if (derivedRoots.Count == 0)
        {
            diagnostics.RecordAttempt(
                source,
                shortcut.ShortcutPath,
                isValid: false,
                "Shortcut did not yield an install-root candidate.");
            return;
        }

        foreach (var derivedRoot in derivedRoots)
        {
            TryAddCandidate(candidates, derivedRoot, source, shortcutOrder, diagnostics, shortcut.ShortcutPath);
        }

        shortcutOrder++;
    }

    private static void TryAddCandidate(
        List<DiscoveryCandidate> candidates,
        string? candidatePath,
        HomecomingDiscoverySource source,
        int order,
        HomecomingInstallDiscoveryDiagnostics diagnostics,
        string? attemptPath = null)
    {
        if (!HomecomingPathRules.TryValidateInstallRoot(candidatePath, out var normalizedRoot, out var failureReason))
        {
            diagnostics.RecordAttempt(source, attemptPath ?? candidatePath, isValid: false, failureReason);
            return;
        }

        diagnostics.RecordAttempt(source, normalizedRoot, isValid: true, failureReason: null);

        var existingIndex = -1;
        for (var index = 0; index < candidates.Count; index++)
        {
            if (string.Equals(candidates[index].InstallRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                existingIndex = index;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            var existing = candidates[existingIndex];
            if (GetSourcePriority(source) < GetSourcePriority(existing.Source))
            {
                candidates[existingIndex] = new DiscoveryCandidate(
                    normalizedRoot,
                    source,
                    Math.Min(existing.Order, order));
            }

            return;
        }

        candidates.Add(new DiscoveryCandidate(normalizedRoot, source, order));
    }

    private static int GetSourcePriority(HomecomingDiscoverySource source) =>
        source switch
        {
            HomecomingDiscoverySource.RunningClient => 0,
            HomecomingDiscoverySource.OfficialDefault => 1,
            HomecomingDiscoverySource.DesktopShortcut => 2,
            HomecomingDiscoverySource.StartMenuShortcut => 3,
            HomecomingDiscoverySource.RunningLauncher => 4,
            HomecomingDiscoverySource.Registry => 5,
            HomecomingDiscoverySource.CommonPath => 6,
            _ => int.MaxValue
        };

    private static bool TrySelectBestCandidate(
        IReadOnlyList<DiscoveryCandidate> candidates,
        HomecomingInstallDiscoveryDiagnostics diagnostics,
        out DiscoveryCandidate selected)
    {
        selected = default;

        if (candidates.Count == 0)
        {
            return false;
        }

        var runningClientCandidates = candidates
            .Where(candidate => candidate.Source == HomecomingDiscoverySource.RunningClient)
            .ToList();
        if (runningClientCandidates.Count > 0)
        {
            return TrySelectSingleRoot(runningClientCandidates, diagnostics, out selected);
        }

        var officialDefault = candidates.FirstOrDefault(candidate =>
            candidate.Source == HomecomingDiscoverySource.OfficialDefault);
        if (officialDefault.InstallRoot is not null)
        {
            selected = officialDefault;
            return true;
        }

        var shortcutCandidate = candidates
            .Where(candidate => candidate.Source is HomecomingDiscoverySource.DesktopShortcut
                or HomecomingDiscoverySource.StartMenuShortcut)
            .OrderBy(candidate => candidate.Order)
            .FirstOrDefault();
        if (shortcutCandidate.InstallRoot is not null)
        {
            selected = shortcutCandidate;
            return true;
        }

        var runningLauncherCandidates = candidates
            .Where(candidate => candidate.Source == HomecomingDiscoverySource.RunningLauncher)
            .ToList();
        if (runningLauncherCandidates.Count > 0)
        {
            return TrySelectSingleRoot(runningLauncherCandidates, diagnostics, out selected);
        }

        var registryCandidate = candidates
            .Where(candidate => candidate.Source == HomecomingDiscoverySource.Registry)
            .OrderBy(candidate => candidate.Order)
            .FirstOrDefault();
        if (registryCandidate.InstallRoot is not null)
        {
            selected = registryCandidate;
            return true;
        }

        var commonPathCandidate = candidates
            .Where(candidate => candidate.Source == HomecomingDiscoverySource.CommonPath)
            .OrderBy(candidate => candidate.Order)
            .FirstOrDefault();
        if (commonPathCandidate.InstallRoot is not null)
        {
            selected = commonPathCandidate;
            return true;
        }

        return false;
    }

    private static bool TrySelectSingleRoot(
        IReadOnlyList<DiscoveryCandidate> candidates,
        HomecomingInstallDiscoveryDiagnostics diagnostics,
        out DiscoveryCandidate selected)
    {
        selected = default;

        var distinctRoots = candidates
            .Select(candidate => candidate.InstallRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctRoots.Count == 1)
        {
            selected = candidates
                .OrderBy(candidate => candidate.Order)
                .First(candidate => string.Equals(candidate.InstallRoot, distinctRoots[0], StringComparison.OrdinalIgnoreCase));
            return true;
        }

        diagnostics.RecordAmbiguity(distinctRoots);
        return false;
    }

    private void PersistInstallation(AppSettings settings, HomecomingInstallation installation)
    {
        if (string.Equals(settings.HomecomingInstallPath, installation.InstallRoot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(settings.HomecomingLauncherPath, installation.LauncherPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        settings.HomecomingInstallPath = installation.InstallRoot;
        settings.HomecomingLauncherPath = installation.LauncherPath;
        _settingsService.Save(settings);
    }

    private readonly record struct DiscoveryCandidate(
        string InstallRoot,
        HomecomingDiscoverySource Source,
        int Order);
}
