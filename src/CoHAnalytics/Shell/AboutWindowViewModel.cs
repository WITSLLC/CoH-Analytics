using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.Shell;

public sealed partial class AboutWindowViewModel
{
    private readonly ILocalDocumentService _localDocumentService;
    private readonly IExternalUriService _externalUriService;

    public AboutWindowViewModel(
        ILocalDocumentService localDocumentService,
        IExternalUriService externalUriService)
    {
        _localDocumentService = localDocumentService;
        _externalUriService = externalUriService;
    }

    public string VersionLabel => ApplicationMetadata.VersionLabel;

    public string Description =>
        "CoH Analytics is a desktop companion for City of Heroes: Homecoming that uses locally generated game logs to provide live session information, combat analytics, character history, badges, and game reference data.";

    public string PrivacyStatement =>
        "CoH Analytics processes gameplay data locally. Game logs and application data remain on your computer unless you explicitly export them.";

    public string CopyrightLine =>
        "© 2026 Willow Information Technology Services, LLC\nAll rights reserved.";

    public string IndependenceDisclaimer =>
        "CoH Analytics is an independent community application and is not affiliated with or endorsed by Homecoming, City of Heroes, or their respective rights holders.";

    [RelayCommand]
    private void OpenLicense()
    {
        if (ApplicationLegalDocuments.TryResolveLicensePath(out var licensePath)
            && _localDocumentService.TryOpenDocument(licensePath, out _))
        {
            return;
        }

        _ = _externalUriService.TryOpenUri(ApplicationLegalDocuments.RepositoryLicenseUri, out _);
    }
}
