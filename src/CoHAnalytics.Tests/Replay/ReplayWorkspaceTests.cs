using System.Text.Json;
using CoHAnalytics.Replay;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayWorkspaceTests
{
    [Fact]
    public async Task CreateAsync_builds_production_shaped_layout()
    {
        await using var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);

        Assert.True(Directory.Exists(workspace.HomecomingRoot));
        Assert.True(Directory.Exists(workspace.AccountRoot));
        Assert.True(Directory.Exists(workspace.LogsDirectory));
        Assert.True(File.Exists(Path.Combine(workspace.AccountRoot, "settings.json")));

        var settingsJson = await File.ReadAllTextAsync(Path.Combine(workspace.AccountRoot, "settings.json"));
        using var document = JsonDocument.Parse(settingsJson);
        Assert.True(document.RootElement.TryGetProperty("ui.chat.timestamps", out _));

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task ResolveDestinationLogPath_uses_dated_chatlog_name()
    {
        await using var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        var path = workspace.ResolveDestinationLogPath(new DateOnly(2026, 8, 1));
        Assert.EndsWith(Path.Combine("Logs", "chatlog 2026-08-01.txt"), path);

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task CleanupAsync_removes_workspace_by_default()
    {
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: false);
        var root = workspace.RootPath;
        Assert.True(Directory.Exists(root));

        await workspace.CleanupAsync();
        Assert.False(Directory.Exists(root));
        Assert.True(workspace.CleanupCompleted);
    }

    [Fact]
    public async Task CleanupAsync_completes_when_caller_token_is_cancelled()
    {
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: false);
        var root = workspace.RootPath;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await workspace.CleanupAsync(cancellation.Token);

        Assert.True(workspace.CleanupRequested);
        Assert.True(workspace.CleanupCompleted);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task KeepWorkspace_retains_directory_for_diagnosis()
    {
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        var root = workspace.RootPath;

        await workspace.CleanupAsync();
        Assert.True(workspace.CleanupRequested);
        Assert.True(Directory.Exists(root));
        // CleanupCompleted is false when retention is active: no deletion occurred.
        Assert.False(workspace.CleanupCompleted);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task DisposeAsync_does_not_delete_parent_directory()
    {
        var parent = Path.Combine(Path.GetTempPath(), "coh-analytics-replay");
        Directory.CreateDirectory(parent);
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: false);
        var root = workspace.RootPath;

        await workspace.DisposeAsync();
        Assert.False(Directory.Exists(root));
        Assert.True(Directory.Exists(parent));

        if (!Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }
    }

    [Fact]
    public async Task Cleanup_of_one_workspace_does_not_delete_sibling_workspace()
    {
        var parent = Path.Combine(Path.GetTempPath(), "coh-analytics-replay");
        Directory.CreateDirectory(parent);

        var workspaceA = await ReplayWorkspace.CreateAsync(keepWorkspace: false);
        var workspaceB = await ReplayWorkspace.CreateAsync(keepWorkspace: false);
        var rootA = workspaceA.RootPath;
        var rootB = workspaceB.RootPath;

        Assert.NotEqual(rootA, rootB);
        Assert.Equal(parent, Path.GetDirectoryName(rootA));
        Assert.Equal(parent, Path.GetDirectoryName(rootB));

        await workspaceA.CleanupAsync();
        Assert.False(Directory.Exists(rootA));
        Assert.True(Directory.Exists(rootB));
        Assert.True(Directory.Exists(parent));

        await workspaceB.CleanupAsync();
        Assert.False(Directory.Exists(rootB));
        Assert.True(Directory.Exists(parent));

        if (!Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }
    }
}
