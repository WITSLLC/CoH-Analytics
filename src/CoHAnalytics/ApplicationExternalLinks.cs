namespace CoHAnalytics;

public static class ApplicationExternalLinks
{
    internal const string SupportAppHostedButtonId = "Q263DCVKTKM5W";

    public const string ProjectHomeUri = "https://github.com/WITSLLC/CoH-Analytics";

    public const string ReportBugUri =
        "https://github.com/WITSLLC/CoH-Analytics/issues/new?template=bug_report.yml";

    public static string SupportAppUri =>
        $"https://www.paypal.com/donate/?hosted_button_id={SupportAppHostedButtonId}";

    public static string SupportAppQrResourcePath =>
        $"/CoHAnalytics;component/Assets/Images/Application/paypal-donation-{SupportAppHostedButtonId}-qr.png";

    /// <summary>
    /// Official PayPal Monogram (Full Color RGB) from PayPal Newsroom media resources.
    /// Source package: https://newsroom.paypal-corp.com/download/PayPal-Monogram-Logo-2024.zip
    /// </summary>
    public const string SupportAppMonogramResourcePath =
        "/CoHAnalytics;component/Assets/Images/Branding/paypal-monogram.png";

    public const string HomecomingUri = "https://forums.homecomingservers.com/";

    public static bool HasDocumentationDestination => false;
}
