using System.Diagnostics;
using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class HomecomingClientDetectorTests : IDisposable
{
    private readonly string _installRoot;

    public HomecomingClientDetectorTests()
    {
        _installRoot = HomecomingRuntimeTestSupport.CreateInstallRoot();
    }

    public void Dispose()
    {
        Directory.Delete(_installRoot, recursive: true);
    }

    [Fact]
    public void DetectRunningClients_retains_process_id_executable_path_and_start_time()
    {
        var clientPath = HomecomingRuntimeTestSupport.ClientExecutablePath(_installRoot);
        HomecomingRuntimeTestSupport.WriteProcessJson(_installRoot, (Environment.ProcessId, clientPath));

        using var currentProcess = Process.GetCurrentProcess();
        var expectedStartTime = new DateTimeOffset(currentProcess.StartTime);

        var clients = HomecomingClientDetector.DetectRunningClients(CreateInstallation());

        var client = Assert.Single(clients);
        Assert.Equal(Environment.ProcessId, client.ProcessId);
        Assert.Equal(clientPath, client.ExecutablePath);
        Assert.Equal(expectedStartTime, client.ProcessStartTime);
    }

    [Fact]
    public void DetectRunningClients_orders_clients_by_process_id()
    {
        var aliveProcessIds = HomecomingRuntimeTestSupport.GetAliveProcessIds(2);
        var clientPath = HomecomingRuntimeTestSupport.ClientExecutablePath(_installRoot);
        HomecomingRuntimeTestSupport.WriteProcessJson(
            _installRoot,
            (aliveProcessIds[1], clientPath),
            (aliveProcessIds[0], clientPath));

        var clients = HomecomingClientDetector.DetectRunningClients(CreateInstallation());

        Assert.Equal(2, clients.Count);
        Assert.True(clients[0].ProcessId < clients[1].ProcessId);
    }

    [Fact]
    public void DetectRunningClients_skips_process_json_entry_when_start_time_cannot_be_read()
    {
        var clientPath = HomecomingRuntimeTestSupport.ClientExecutablePath(_installRoot);
        HomecomingRuntimeTestSupport.WriteProcessJson(_installRoot, (int.MaxValue, clientPath));

        var clients = HomecomingClientDetector.DetectRunningClients(CreateInstallation());

        Assert.Empty(clients);
    }

    [Fact]
    public void TryGetLiveProcessInfo_returns_false_for_missing_process_without_throwing()
    {
        var succeeded = ProcessHelper.TryGetLiveProcessInfo(int.MaxValue, out var executablePath, out var processStartTime);

        Assert.False(succeeded);
        Assert.Null(executablePath);
        Assert.Equal(default(DateTimeOffset), processStartTime);
    }

    private HomecomingInstallation CreateInstallation() =>
        new()
        {
            InstallRoot = _installRoot,
            LauncherPath = Path.Combine(_installRoot, "Launcher.exe")
        };
}

internal static class HomecomingRuntimeTestSupport
{
    public static string CreateInstallRoot()
    {
        var installRoot = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-runtime-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(installRoot, "settings", "launcher"));
        Directory.CreateDirectory(Path.GetDirectoryName(ClientExecutablePath(installRoot))!);
        WriteLauncherExecutable(installRoot);
        return installRoot;
    }

    public static void WriteLauncherExecutable(string installRoot)
    {
        var launcherDirectory = Path.Combine(installRoot, "bin", "win64");
        Directory.CreateDirectory(launcherDirectory);
        var launcherPath = Path.Combine(launcherDirectory, "launcher.exe");
        if (!File.Exists(launcherPath))
        {
            File.WriteAllBytes(launcherPath, []);
        }
    }

    public static string ClientExecutablePath(string installRoot) =>
        Path.Combine(installRoot, "bin", "win64", "live", "cityofheroes.exe");

    public static void WriteProcessJson(string installRoot, params (int ProcessId, string ExecutablePath)[] entries)
    {
        var serializedEntries = string.Join(
            ",",
            entries.Select(entry =>
                $$"""{"pid":{{entry.ProcessId}},"path":{{JsonSerializer.Serialize(entry.ExecutablePath)}}}"""));

        var processJsonPath = Path.Combine(installRoot, "settings", "launcher", "process.json");
        File.WriteAllText(processJsonPath, $$"""{"default":[{{serializedEntries}}]}""");
    }

    public static IReadOnlyList<int> GetAliveProcessIds(int count)
    {
        var processIds = new List<int>(count);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    processIds.Add(process.Id);
                    if (processIds.Count >= count)
                    {
                        break;
                    }
                }
                catch
                {
                }
            }
        }

        if (processIds.Count < count)
        {
            throw new InvalidOperationException($"Could not find {count} alive processes for test setup.");
        }

        return processIds;
    }

    public static void SetCurrentInstallation(
        HomecomingInstallationService service,
        HomecomingInstallation installation) =>
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.CurrentInstallation))!
            .SetValue(service, installation);
}
