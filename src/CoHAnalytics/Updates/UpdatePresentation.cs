namespace CoHAnalytics.Updates;

public enum UpdateUserAction
{
    Later,
    ViewRelease,
    DownloadPackage
}

public sealed record UpdateAvailablePresentation(
    string AvailableMessage,
    string? SourceBuildMessage,
    string ReleaseActionLabel,
    string? DownloadActionLabel);

public interface IUpdatePresentation
{
    bool CanPresent { get; }

    UpdateUserAction ShowUpdateAvailable(UpdateAvailablePresentation presentation);

    void ShowCurrent(string message);

    void ShowUnableToCheck(string message);

    void ShowUnableToOpenLink();
}
