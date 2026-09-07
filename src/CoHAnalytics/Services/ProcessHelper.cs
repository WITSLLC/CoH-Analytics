using System.Diagnostics;

namespace CoHAnalytics.Services;

internal static class ProcessHelper
{
    public static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetExecutablePath(int processId, out string? executablePath)
    {
        executablePath = null;

        try
        {
            using var process = Process.GetProcessById(processId);
            executablePath = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(executablePath);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetLiveProcessInfo(
        int processId,
        out string? executablePath,
        out DateTimeOffset processStartTime)
    {
        executablePath = null;
        processStartTime = default;

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            processStartTime = new DateTimeOffset(process.StartTime);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
