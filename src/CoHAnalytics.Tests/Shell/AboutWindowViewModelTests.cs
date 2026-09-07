using CoHAnalytics.Services;
using CoHAnalytics.Shell;

namespace CoHAnalytics.Tests.Shell;

public sealed class AboutWindowViewModelTests
{
    [Fact]
    public void Version_label_uses_runtime_application_metadata()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(ApplicationMetadata.VersionLabel, viewModel.VersionLabel);
        Assert.NotEqual("Version 0.0.0-test", viewModel.VersionLabel);
    }

    [Fact]
    public void Privacy_statement_describes_local_processing()
    {
        var viewModel = CreateViewModel();

        Assert.Contains("processes gameplay data locally", viewModel.PrivacyStatement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remain on your computer", viewModel.PrivacyStatement, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Independence_disclaimer_is_present()
    {
        var viewModel = CreateViewModel();

        Assert.Contains("independent community application", viewModel.IndependenceDisclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not affiliated with or endorsed by", viewModel.IndependenceDisclaimer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Copyright_is_the_closing_ownership_statement()
    {
        var viewModel = CreateViewModel();

        Assert.Contains("© 2026", viewModel.CopyrightLine, StringComparison.Ordinal);
        Assert.Contains("All rights reserved", viewModel.CopyrightLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_license_opens_local_license_when_available()
    {
        var documents = new RecordingLocalDocumentService { Succeed = true };
        var external = new RecordingExternalUriService();
        var viewModel = CreateViewModel(documents, external);

        viewModel.OpenLicenseCommand.Execute(null);

        Assert.NotNull(documents.LastOpenedPath);
        Assert.Equal("LICENSE", Path.GetFileName(documents.LastOpenedPath), StringComparer.OrdinalIgnoreCase);
        Assert.Null(external.LastOpenedUri);
    }

    [Fact]
    public void Open_license_falls_back_to_repository_url_when_local_open_fails()
    {
        var documents = new RecordingLocalDocumentService { Succeed = false };
        var external = new RecordingExternalUriService();
        var viewModel = CreateViewModel(documents, external);

        viewModel.OpenLicenseCommand.Execute(null);

        Assert.Equal(ApplicationLegalDocuments.RepositoryLicenseUri, external.LastOpenedUri);
    }

    private static AboutWindowViewModel CreateViewModel(
        ILocalDocumentService? localDocumentService = null,
        IExternalUriService? externalUriService = null) =>
        new(
            localDocumentService ?? new RecordingLocalDocumentService { Succeed = true },
            externalUriService ?? new RecordingExternalUriService());

    private sealed class RecordingLocalDocumentService : ILocalDocumentService
    {
        public bool Succeed { get; init; } = true;

        public string? LastOpenedPath { get; private set; }

        public bool TryOpenDocument(string path, out string? failureReason)
        {
            LastOpenedPath = path;
            failureReason = Succeed ? null : "failed";
            return Succeed;
        }
    }

    private sealed class RecordingExternalUriService : IExternalUriService
    {
        public string? LastOpenedUri { get; private set; }

        public bool TryOpenUri(string uri, out string? failureReason)
        {
            LastOpenedUri = uri;
            failureReason = null;
            return true;
        }
    }
}
