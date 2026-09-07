using CoHAnalytics.Shell;

namespace CoHAnalytics.Tests.Shell;

public sealed class SupportWindowViewModelTests
{
    [Fact]
    public void Support_copy_explicitly_describes_application_development()
    {
        var viewModel = new SupportWindowViewModel(new RecordingExternalUriService());

        Assert.Contains("continued development", viewModel.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CoH Analytics", viewModel.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Qr_code_uses_the_shared_hosted_button_resource()
    {
        var viewModel = new SupportWindowViewModel(new RecordingExternalUriService());

        Assert.Equal(ApplicationExternalLinks.SupportAppQrResourcePath, viewModel.QrCodeSource);
    }

    [Fact]
    public void PayPal_action_uses_the_shared_hosted_donation_uri()
    {
        var externalUriService = new RecordingExternalUriService();
        var viewModel = new SupportWindowViewModel(externalUriService);

        viewModel.OpenSupportAppCommand.Execute(null);

        Assert.Equal(ApplicationExternalLinks.SupportAppUri, externalUriService.LastOpenedUri);
    }

    [Fact]
    public void PayPal_action_failure_does_not_throw()
    {
        var viewModel = new SupportWindowViewModel(new FailingExternalUriService());

        var exception = Record.Exception(() => viewModel.OpenSupportAppCommand.Execute(null));

        Assert.Null(exception);
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
