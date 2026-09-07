using CommunityToolkit.Mvvm.Input;
using CoHAnalytics.Services;

namespace CoHAnalytics.Shell;

public sealed partial class ApplicationTitleBarViewModel
{
    private readonly Action _showSettings;
    private readonly Action _closeApplication;
    private readonly Action _showSupport;
    private readonly Action _showAbout;
    private readonly Action _createDiagnosticsReport;
    private readonly IExternalUriService _externalUriService;

    public ApplicationTitleBarViewModel(
        Action showSettings,
        Action closeApplication,
        Action showSupport,
        Action showAbout,
        Action createDiagnosticsReport,
        IExternalUriService externalUriService)
    {
        _showSettings = showSettings;
        _closeApplication = closeApplication;
        _showSupport = showSupport;
        _showAbout = showAbout;
        _createDiagnosticsReport = createDiagnosticsReport;
        _externalUriService = externalUriService;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _showSettings();
    }

    [RelayCommand]
    private void Exit()
    {
        _closeApplication();
    }

    [RelayCommand]
    private void ShowAbout()
    {
        _showAbout();
    }

    [RelayCommand]
    private void OpenProjectHome()
    {
        _ = _externalUriService.TryOpenUri(ApplicationExternalLinks.ProjectHomeUri, out _);
    }

    [RelayCommand]
    private void CreateDiagnosticsReport()
    {
        _createDiagnosticsReport();
    }

    [RelayCommand]
    private void OpenReportBug()
    {
        _ = _externalUriService.TryOpenUri(ApplicationExternalLinks.ReportBugUri, out _);
    }

    [RelayCommand]
    private void OpenHomecoming()
    {
        _ = _externalUriService.TryOpenUri(ApplicationExternalLinks.HomecomingUri, out _);
    }

    [RelayCommand]
    private void ShowSupport()
    {
        _showSupport();
    }
}
