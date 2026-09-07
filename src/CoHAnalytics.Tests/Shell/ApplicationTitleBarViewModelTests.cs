using CoHAnalytics.Shell;

namespace CoHAnalytics.Tests.Shell;

public sealed class ApplicationTitleBarViewModelTests
{
    [Fact]
    public void Settings_command_opens_through_supplied_modal_action()
    {
        var settingsCalls = 0;
        var viewModel = CreateViewModel(
            showSettings: () => settingsCalls++,
            closeApplication: () => { },
            showSupport: () => { },
            showAbout: () => { },
            createDiagnosticsReport: () => { });

        viewModel.OpenSettingsCommand.Execute(null);

        Assert.Equal(1, settingsCalls);
    }

    [Fact]
    public void Exit_command_closes_through_supplied_window_close_action()
    {
        var closeCalls = 0;
        var viewModel = CreateViewModel(
            showSettings: () => { },
            closeApplication: () => closeCalls++,
            showSupport: () => { },
            showAbout: () => { },
            createDiagnosticsReport: () => { });

        viewModel.ExitCommand.Execute(null);

        Assert.Equal(1, closeCalls);
    }

    [Fact]
    public void About_command_opens_through_supplied_modal_action()
    {
        var aboutCalls = 0;
        var viewModel = CreateViewModel(
            showSettings: () => { },
            closeApplication: () => { },
            showSupport: () => { },
            showAbout: () => aboutCalls++,
            createDiagnosticsReport: () => { });

        viewModel.ShowAboutCommand.Execute(null);

        Assert.Equal(1, aboutCalls);
    }

    [Fact]
    public void Support_command_opens_through_supplied_modal_action()
    {
        var supportCalls = 0;
        var viewModel = CreateViewModel(
            showSettings: () => { },
            closeApplication: () => { },
            showSupport: () => supportCalls++,
            showAbout: () => { },
            createDiagnosticsReport: () => { });

        viewModel.ShowSupportCommand.Execute(null);

        Assert.Equal(1, supportCalls);
    }

    [Fact]
    public void Create_diagnostics_report_command_invokes_supplied_action()
    {
        var calls = 0;
        var viewModel = CreateViewModel(
            showSettings: () => { },
            closeApplication: () => { },
            showSupport: () => { },
            showAbout: () => { },
            createDiagnosticsReport: () => calls++);

        viewModel.CreateDiagnosticsReportCommand.Execute(null);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Help_exposes_expected_external_link_commands()
    {
        var viewModel = CreateViewModel(
            showSettings: () => { },
            closeApplication: () => { },
            showSupport: () => { },
            showAbout: () => { },
            createDiagnosticsReport: () => { });

        Assert.NotNull(viewModel.CreateDiagnosticsReportCommand);
        Assert.NotNull(viewModel.OpenProjectHomeCommand);
        Assert.NotNull(viewModel.OpenReportBugCommand);
        Assert.NotNull(viewModel.OpenHomecomingCommand);
        Assert.NotNull(viewModel.ShowSupportCommand);
        Assert.NotNull(viewModel.ShowAboutCommand);
    }

    [Fact]
    public void Settings_remains_a_direct_top_level_action()
    {
        var viewModel = CreateViewModel(
            showSettings: () => { },
            closeApplication: () => { },
            showSupport: () => { },
            showAbout: () => { },
            createDiagnosticsReport: () => { });

        Assert.NotNull(viewModel.OpenSettingsCommand);
    }

    [Fact]
    public void Project_home_uses_expected_github_uri()
    {
        var externalUriService = new RecordingExternalUriService();
        var viewModel = CreateViewModel(
            showSettings: () => { },
            closeApplication: () => { },
            showSupport: () => { },
            showAbout: () => { },
            createDiagnosticsReport: () => { },
            externalUriService: externalUriService);

        viewModel.OpenProjectHomeCommand.Execute(null);

        Assert.Equal(ApplicationExternalLinks.ProjectHomeUri, externalUriService.LastOpenedUri);
    }

    [Fact]
    public void Report_a_bug_uses_expected_github_issues_uri()
    {
        var externalUriService = new RecordingExternalUriService();
        var viewModel = CreateViewModel(
            showSettings: () => { },
            closeApplication: () => { },
            showSupport: () => { },
            showAbout: () => { },
            createDiagnosticsReport: () => { },
            externalUriService: externalUriService);

        viewModel.OpenReportBugCommand.Execute(null);

        Assert.Equal(ApplicationExternalLinks.ReportBugUri, externalUriService.LastOpenedUri);
    }

    [Fact]
    public void Homecoming_uses_verified_official_uri()
    {
        var externalUriService = new RecordingExternalUriService();
        var viewModel = CreateViewModel(
            showSettings: () => { },
            closeApplication: () => { },
            showSupport: () => { },
            showAbout: () => { },
            createDiagnosticsReport: () => { },
            externalUriService: externalUriService);

        viewModel.OpenHomecomingCommand.Execute(null);

        Assert.Equal(ApplicationExternalLinks.HomecomingUri, externalUriService.LastOpenedUri);
    }

    [Fact]
    public void Documentation_destination_is_not_available_for_help_menu()
    {
        Assert.False(ApplicationExternalLinks.HasDocumentationDestination);
    }

    [Theory]
    [InlineData(nameof(ApplicationTitleBarViewModel.OpenProjectHomeCommand))]
    [InlineData(nameof(ApplicationTitleBarViewModel.OpenReportBugCommand))]
    [InlineData(nameof(ApplicationTitleBarViewModel.OpenHomecomingCommand))]
    public void External_link_command_failure_does_not_throw(string commandPropertyName)
    {
        var viewModel = CreateViewModel(
            showSettings: () => { },
            closeApplication: () => { },
            showSupport: () => { },
            showAbout: () => { },
            createDiagnosticsReport: () => { },
            externalUriService: new FailingExternalUriService());

        var command = typeof(ApplicationTitleBarViewModel).GetProperty(commandPropertyName)?.GetValue(viewModel);
        Assert.NotNull(command);

        var exception = Record.Exception(() => ((CommunityToolkit.Mvvm.Input.IRelayCommand)command).Execute(null));

        Assert.Null(exception);
    }

    private static ApplicationTitleBarViewModel CreateViewModel(
        Action showSettings,
        Action closeApplication,
        Action showSupport,
        Action showAbout,
        Action createDiagnosticsReport,
        CoHAnalytics.Services.IExternalUriService? externalUriService = null)
    {
        return new ApplicationTitleBarViewModel(
            showSettings,
            closeApplication,
            showSupport,
            showAbout,
            createDiagnosticsReport,
            externalUriService ?? new RecordingExternalUriService());
    }

    private sealed class RecordingExternalUriService : CoHAnalytics.Services.IExternalUriService
    {
        public string? LastOpenedUri { get; private set; }

        public bool TryOpenUri(string uri, out string? failureReason)
        {
            LastOpenedUri = uri;
            failureReason = null;
            return true;
        }
    }

    private sealed class FailingExternalUriService : CoHAnalytics.Services.IExternalUriService
    {
        public bool TryOpenUri(string uri, out string? failureReason)
        {
            failureReason = "Simulated failure.";
            return false;
        }
    }
}
