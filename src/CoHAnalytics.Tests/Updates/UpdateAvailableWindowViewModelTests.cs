using CoHAnalytics.Shell;
using CoHAnalytics.Updates;

namespace CoHAnalytics.Tests.Updates;

public sealed class UpdateAvailableWindowViewModelTests
{
    [Fact]
    public void Packaged_copy_exposes_download_action_without_source_message()
    {
        var viewModel = new UpdateAvailableWindowViewModel(new UpdateAvailablePresentation(
            "Update available",
            null,
            "View Release Notes",
            "Download Installer"));

        Assert.False(viewModel.HasSourceBuildMessage);
        Assert.True(viewModel.HasDownloadAction);
        Assert.Equal("Download Installer", viewModel.DownloadActionLabel);
    }

    [Fact]
    public void Source_copy_exposes_source_message_without_download_action()
    {
        var viewModel = new UpdateAvailableWindowViewModel(new UpdateAvailablePresentation(
            "Update available",
            "This copy was built from source.",
            "View Release",
            null));

        Assert.True(viewModel.HasSourceBuildMessage);
        Assert.False(viewModel.HasDownloadAction);
        Assert.Equal("View Release", viewModel.ReleaseActionLabel);
    }
}
