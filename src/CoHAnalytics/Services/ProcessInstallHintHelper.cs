using System.Diagnostics;

namespace CoHAnalytics.Services;

internal static class ProcessInstallHintHelper
{
    public static IEnumerable<string> EnumerateRunningClientExecutablePaths()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("cityofheroes");
        }
        catch
        {
            yield break;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                if (ProcessHelper.TryGetExecutablePath(process.Id, out var executablePath)
                    && !string.IsNullOrWhiteSpace(executablePath))
                {
                    yield return executablePath;
                }
            }
        }
    }

    public static IEnumerable<string> EnumerateRunningLauncherExecutablePaths()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("launcher");
        }
        catch
        {
            yield break;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                if (ProcessHelper.TryGetExecutablePath(process.Id, out var executablePath)
                    && !string.IsNullOrWhiteSpace(executablePath)
                    && executablePath.EndsWith("launcher.exe", StringComparison.OrdinalIgnoreCase))
                {
                    yield return executablePath;
                }
            }
        }
    }

    public static IEnumerable<string> EnumerateRunningMidsExecutablePaths()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("MidsReborn");
        }
        catch
        {
            yield break;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                if (ProcessHelper.TryGetExecutablePath(process.Id, out var executablePath)
                    && !string.IsNullOrWhiteSpace(executablePath)
                    && executablePath.EndsWith("MidsReborn.exe", StringComparison.OrdinalIgnoreCase))
                {
                    yield return executablePath;
                }
            }
        }
    }
}
