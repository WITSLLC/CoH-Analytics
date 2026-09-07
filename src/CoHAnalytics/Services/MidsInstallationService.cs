using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed class MidsInstallationService
{
    private readonly SettingsService _settingsService;

    public MidsInstallationService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public MidsInstallation? CurrentInstallation { get; private set; }

    public MidsInstallDiscoveryDiagnostics LastDiscovery { get; private set; } = new();

    public MidsInstallationStatus GetStatus() =>
        MidsInstallationStatus.FromInstallation(
            CurrentInstallation,
            LastDiscovery.MultipleInstallAmbiguity);

    public MidsInstallation? DiscoverAndPersist()
    {
        var diagnostics = new MidsInstallDiscoveryDiagnostics();
        var settings = _settingsService.Load();

        if (TrySelectValidatedCandidate(
                settings.MidsInstallPath,
                MidsDiscoverySource.Persisted,
                diagnostics,
                out var persistedInstallation))
        {
            return FinalizeDiscovery(diagnostics, settings, persistedInstallation);
        }

        var candidates = new List<DiscoveryCandidate>();
        var shortcutOrder = 0;

        TryAddCandidate(
            candidates,
            MidsPathRules.OfficialDefaultInstallRoot,
            MidsDiscoverySource.OfficialDefault,
            order: 0,
            diagnostics);

        foreach (var shortcut in ShortcutHelper.EnumerateDesktopShortcuts())
        {
            AddShortcutCandidates(candidates, shortcut, MidsDiscoverySource.DesktopShortcut, ref shortcutOrder, diagnostics);
        }

        foreach (var shortcut in ShortcutHelper.EnumerateStartMenuShortcuts())
        {
            AddShortcutCandidates(candidates, shortcut, MidsDiscoverySource.StartMenuShortcut, ref shortcutOrder, diagnostics);
        }

        var runningProcessOrder = 0;
        foreach (var executablePath in ProcessInstallHintHelper.EnumerateRunningMidsExecutablePaths())
        {
            if (MidsPathRules.TryDeriveInstallRootFromExecutable(executablePath, out var installRoot))
            {
                TryAddCandidate(
                    candidates,
                    installRoot,
                    MidsDiscoverySource.RunningProcess,
                    runningProcessOrder++,
                    diagnostics,
                    executablePath);
            }
            else
            {
                diagnostics.RecordAttempt(
                    MidsDiscoverySource.RunningProcess,
                    executablePath,
                    isValid: false,
                    "Executable path does not match Mids Reborn layout.");
            }
        }

        var registryOrder = 0;
        foreach (var registryHint in MidsRegistryHintHelper.EnumerateInstallHints())
        {
            TryAddCandidate(
                candidates,
                registryHint,
                MidsDiscoverySource.Registry,
                registryOrder++,
                diagnostics);
        }

        var commonPathOrder = 0;
        foreach (var commonPath in MidsPathRules.CommonInstallationCandidates)
        {
            TryAddCandidate(
                candidates,
                commonPath,
                MidsDiscoverySource.CommonPath,
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

    private MidsInstallation FinalizeDiscovery(
        MidsInstallDiscoveryDiagnostics diagnostics,
        AppSettings settings,
        DiscoveryCandidate selected)
    {
        var executablePath = MidsPathRules.ResolveExecutablePath(
            selected.Installation.InstallRoot,
            settings.MidsExecutablePath);
        var installation = new MidsInstallation
        {
            InstallRoot = selected.Installation.InstallRoot,
            ExecutablePath = executablePath,
            ApplicationVersion = selected.Installation.ApplicationVersion,
            HomecomingDatabasePath = selected.Installation.HomecomingDatabasePath,
            HomecomingDatabaseVersion = selected.Installation.HomecomingDatabaseVersion,
            DiscoverySource = selected.Source
        };

        CurrentInstallation = installation;
        diagnostics.RecordSelection(
            installation.InstallRoot,
            selected.Source,
            installation.ExecutablePath,
            installation.ApplicationVersion,
            installation.HomecomingDatabaseVersion);
        LastDiscovery = diagnostics;
        PersistInstallation(settings, installation);
        return installation;
    }

    private static bool TrySelectValidatedCandidate(
        string? candidatePath,
        MidsDiscoverySource source,
        MidsInstallDiscoveryDiagnostics diagnostics,
        out DiscoveryCandidate installation)
    {
        installation = default;

        if (!MidsPathRules.TryValidateInstallRoot(candidatePath, out var validated, out var failureReason))
        {
            diagnostics.RecordAttempt(source, candidatePath, isValid: false, failureReason);
            return false;
        }

        diagnostics.RecordAttempt(source, validated.InstallRoot, isValid: true, failureReason: null);
        installation = new DiscoveryCandidate(validated, source, 0);
        return true;
    }

    private static void AddShortcutCandidates(
        List<DiscoveryCandidate> candidates,
        ShortcutDetails shortcut,
        MidsDiscoverySource source,
        ref int shortcutOrder,
        MidsInstallDiscoveryDiagnostics diagnostics)
    {
        var derivedRoots = MidsPathRules.DeriveInstallRootCandidates(shortcut).ToList();
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
        MidsDiscoverySource source,
        int order,
        MidsInstallDiscoveryDiagnostics diagnostics,
        string? attemptPath = null)
    {
        if (!MidsPathRules.TryValidateInstallRoot(candidatePath, out var validated, out var failureReason))
        {
            diagnostics.RecordAttempt(source, attemptPath ?? candidatePath, isValid: false, failureReason);
            return;
        }

        diagnostics.RecordAttempt(source, validated.InstallRoot, isValid: true, failureReason: null);

        var existingIndex = -1;
        for (var index = 0; index < candidates.Count; index++)
        {
            if (string.Equals(candidates[index].Installation.InstallRoot, validated.InstallRoot, StringComparison.OrdinalIgnoreCase))
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
                    validated,
                    source,
                    Math.Min(existing.Order, order));
            }

            return;
        }

        candidates.Add(new DiscoveryCandidate(validated, source, order));
    }

    private static bool TrySelectBestCandidate(
        IReadOnlyList<DiscoveryCandidate> candidates,
        MidsInstallDiscoveryDiagnostics diagnostics,
        out DiscoveryCandidate selected)
    {
        selected = default;

        if (candidates.Count == 0)
        {
            return false;
        }

        var runningProcessCandidates = candidates
            .Where(candidate => candidate.Source == MidsDiscoverySource.RunningProcess)
            .ToList();
        if (runningProcessCandidates.Count > 0)
        {
            return TrySelectSingleRoot(runningProcessCandidates, diagnostics, out selected);
        }

        var officialDefault = candidates.FirstOrDefault(candidate =>
            candidate.Source == MidsDiscoverySource.OfficialDefault);
        if (officialDefault.Installation.InstallRoot is not null)
        {
            selected = officialDefault;
            return true;
        }

        var shortcutCandidate = candidates
            .Where(candidate => candidate.Source is MidsDiscoverySource.DesktopShortcut
                or MidsDiscoverySource.StartMenuShortcut)
            .OrderBy(candidate => candidate.Order)
            .FirstOrDefault();
        if (shortcutCandidate.Installation.InstallRoot is not null)
        {
            selected = shortcutCandidate;
            return true;
        }

        var registryCandidate = candidates
            .Where(candidate => candidate.Source == MidsDiscoverySource.Registry)
            .OrderBy(candidate => candidate.Order)
            .FirstOrDefault();
        if (registryCandidate.Installation.InstallRoot is not null)
        {
            selected = registryCandidate;
            return true;
        }

        var commonPathCandidate = candidates
            .Where(candidate => candidate.Source == MidsDiscoverySource.CommonPath)
            .OrderBy(candidate => candidate.Order)
            .FirstOrDefault();
        if (commonPathCandidate.Installation.InstallRoot is not null)
        {
            selected = commonPathCandidate;
            return true;
        }

        return false;
    }

    private static bool TrySelectSingleRoot(
        IReadOnlyList<DiscoveryCandidate> candidates,
        MidsInstallDiscoveryDiagnostics diagnostics,
        out DiscoveryCandidate selected)
    {
        selected = default;

        var distinctRoots = candidates
            .Select(candidate => candidate.Installation.InstallRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctRoots.Count == 1)
        {
            selected = candidates
                .OrderBy(candidate => candidate.Order)
                .First(candidate => string.Equals(
                    candidate.Installation.InstallRoot,
                    distinctRoots[0],
                    StringComparison.OrdinalIgnoreCase));
            return true;
        }

        diagnostics.RecordAmbiguity(distinctRoots);
        return false;
    }

    private static int GetSourcePriority(MidsDiscoverySource source) =>
        source switch
        {
            MidsDiscoverySource.RunningProcess => 0,
            MidsDiscoverySource.OfficialDefault => 1,
            MidsDiscoverySource.DesktopShortcut => 2,
            MidsDiscoverySource.StartMenuShortcut => 3,
            MidsDiscoverySource.Registry => 4,
            MidsDiscoverySource.CommonPath => 5,
            _ => int.MaxValue
        };

    private void PersistInstallation(AppSettings settings, MidsInstallation installation)
    {
        if (string.Equals(settings.MidsInstallPath, installation.InstallRoot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(settings.MidsExecutablePath, installation.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        settings.MidsInstallPath = installation.InstallRoot;
        settings.MidsExecutablePath = installation.ExecutablePath;
        _settingsService.Save(settings);
    }

    private readonly record struct DiscoveryCandidate(
        MidsValidatedInstallation Installation,
        MidsDiscoverySource Source,
        int Order);
}
