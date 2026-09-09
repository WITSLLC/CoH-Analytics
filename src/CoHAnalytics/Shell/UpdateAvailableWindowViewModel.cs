using CoHAnalytics.Updates;

namespace CoHAnalytics.Shell;

public sealed class UpdateAvailableWindowViewModel
{
    public UpdateAvailableWindowViewModel(UpdateAvailablePresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        AvailableMessage = presentation.AvailableMessage;
        SourceBuildMessage = presentation.SourceBuildMessage;
        ReleaseActionLabel = presentation.ReleaseActionLabel;
        DownloadActionLabel = presentation.DownloadActionLabel;
    }

    public string AvailableMessage { get; }

    public string? SourceBuildMessage { get; }

    public bool HasSourceBuildMessage => !string.IsNullOrWhiteSpace(SourceBuildMessage);

    public string ReleaseActionLabel { get; }

    public string? DownloadActionLabel { get; }

    public bool HasDownloadAction => !string.IsNullOrWhiteSpace(DownloadActionLabel);
}
