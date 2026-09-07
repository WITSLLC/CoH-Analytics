using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.Shell;

public sealed partial class SupportWindowViewModel
{
    private readonly IExternalUriService _externalUriService;

    public SupportWindowViewModel(IExternalUriService externalUriService)
    {
        _externalUriService = externalUriService;
    }

    public string Description =>
        "If you enjoy CoH Analytics and would like to support its continued development, you can contribute through PayPal.";

    public string QrCodeSource => ApplicationExternalLinks.SupportAppQrResourcePath;

    [RelayCommand]
    private void OpenSupportApp()
    {
        _ = _externalUriService.TryOpenUri(ApplicationExternalLinks.SupportAppUri, out _);
    }
}
