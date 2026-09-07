using System.Diagnostics;
using System.Text.Json;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

internal static class HomecomingClientDetector
{
    public static IReadOnlyList<DetectedClient> DetectRunningClients(HomecomingInstallation installation)
    {
        var clients = new Dictionary<int, DetectedClient>();

        foreach (var client in DetectFromProcessJson(installation))
        {
            clients[client.ProcessId] = client;
        }

        foreach (var client in DetectFromProcessEnumeration(installation))
        {
            clients.TryAdd(client.ProcessId, client);
        }

        return clients.Values
            .OrderBy(client => client.ProcessId)
            .ToList();
    }

    internal static IReadOnlyList<HomecomingProcessInstance> ToProcessInstances(
        IReadOnlyList<DetectedClient> clients) =>
        clients
            .Select(client => new HomecomingProcessInstance
            {
                ProcessId = client.ProcessId,
                ProcessStartTime = client.ProcessStartTime,
                ExecutablePath = client.ExecutablePath
            })
            .OrderBy(instance => instance.ProcessId)
            .ToArray();

    private static IEnumerable<DetectedClient> DetectFromProcessJson(HomecomingInstallation installation)
    {
        var processJsonPath = Path.Combine(installation.InstallRoot, "settings", "launcher", "process.json");
        if (!File.Exists(processJsonPath))
        {
            yield break;
        }

        JsonDocument document;
        try
        {
            var json = File.ReadAllText(processJsonPath);
            document = JsonDocument.Parse(json);
        }
        catch
        {
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            foreach (var profileProperty in document.RootElement.EnumerateObject())
            {
                if (profileProperty.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in profileProperty.Value.EnumerateArray())
                {
                    if (!TryParseProcessJsonEntry(entry, installation, out var client))
                    {
                        continue;
                    }

                    yield return client;
                }
            }
        }
    }

    private static IEnumerable<DetectedClient> DetectFromProcessEnumeration(HomecomingInstallation installation)
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
                string? executablePath;
                DateTimeOffset processStartTime;

                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    executablePath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(executablePath))
                    {
                        continue;
                    }

                    processStartTime = new DateTimeOffset(process.StartTime);
                }
                catch
                {
                    continue;
                }

                if (!HomecomingPathRules.IsKnownClientExecutable(installation.InstallRoot, executablePath))
                {
                    continue;
                }

                yield return new DetectedClient(process.Id, processStartTime, executablePath);
            }
        }
    }

    private static bool TryParseProcessJsonEntry(
        JsonElement entry,
        HomecomingInstallation installation,
        out DetectedClient client)
    {
        client = default!;

        if (entry.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!entry.TryGetProperty("pid", out var pidElement) || !pidElement.TryGetInt32(out var processId))
        {
            return false;
        }

        if (!entry.TryGetProperty("path", out var pathElement))
        {
            return false;
        }

        var executablePath = pathElement.GetString();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        if (!ProcessHelper.IsProcessAlive(processId))
        {
            return false;
        }

        if (!HomecomingPathRules.IsKnownClientExecutable(installation.InstallRoot, executablePath))
        {
            return false;
        }

        if (!ProcessHelper.TryGetLiveProcessInfo(processId, out _, out var processStartTime))
        {
            return false;
        }

        client = new DetectedClient(processId, processStartTime, executablePath);
        return true;
    }

    internal readonly record struct DetectedClient(
        int ProcessId,
        DateTimeOffset ProcessStartTime,
        string ExecutablePath);
}
