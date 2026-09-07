using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Services;
using CoHAnalytics.Shell;
using CoHAnalytics.ViewModels;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics;

public partial class MainWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int HtCaption = 2;
    private const int HtTop = 12;
    private const double CaptionHeight = 31;
    private const double DefaultCaptionButtonAreaWidth = 138;

    private readonly AppServices _services;
    private readonly IFolderInteractionService _folderInteractionService;
    private readonly IExternalUriService _externalUriService;
    private readonly ILocalDocumentService _localDocumentService;
    private double _captionButtonAreaWidth = DefaultCaptionButtonAreaWidth;

    public MainWindow(AppServices services)
    {
        _services = services;
        _folderInteractionService = new WindowsFolderInteractionService();
        _externalUriService = new WindowsExternalUriService();
        _localDocumentService = new WindowsLocalDocumentService();
        InitializeComponent();
        Closing += OnClosing;
        SourceInitialized += OnSourceInitialized;

        var mainViewModel = new MainViewModel(
            services.Orchestrator,
            services.RuntimeService,
            services.HomecomingInstallationService,
            services.SettingsService,
            _folderInteractionService,
            services.AccountDiscoveryService,
            services.MidsInstallationService,
            services.CharacterRepository,
            services.MonitoringSessionManager,
            services.GameplaySessionManager,
            services.GameplaySessionIdentityReadService,
            services.ViewedContextService,
            services.GameplaySessionContextResolver,
            services.LogActivityService,
            services.ActivityLogService,
            services.ItemReferenceCatalog,
            services.AcquisitionObservationService,
            services.AcquisitionClassificationService,
            services.AccountAnonymityService,
            services.InternalFeatureGate,
            services.SessionStore,
            services.EnhancementIconCompositor,
            services.BoostMetadataProvider,
            services.InstalledGameAssetProvider,
            services.CharacterBadgeAcquisitionRepository,
            services.CharacterHistoricalPerformanceReadService,
            services.CharacterPerformanceObservationRepository,
            services.CharacterBuildImportService,
            services.BuiltInCharacterIconService,
            _externalUriService,
            services.CustomCharacterIconService);

        DataContext = mainViewModel;
        ApplicationTitleBar.DataContext = new ApplicationTitleBarViewModel(
            ShowSettingsDialog,
            Close,
            ShowSupportDialog,
            ShowAboutDialog,
            _externalUriService);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProcedure);
        NativeCaptionBottomFill.Height = Math.Max(
            0,
            CaptionHeight - SystemParameters.WindowNonClientFrameThickness.Top);
        WindowCaptionTheme.Apply(handle);

        if (WindowCaptionTheme.TryGetCaptionButtonAreaWidth(handle, out var nativeCaptionButtonAreaWidth))
        {
            var captionButtonWidth = nativeCaptionButtonAreaWidth / 2;
            _captionButtonAreaWidth = nativeCaptionButtonAreaWidth + captionButtonWidth;
            ApplicationTitleBar.Margin = new Thickness(
                0,
                0,
                _captionButtonAreaWidth,
                0);
            MinimizeButton.Width = captionButtonWidth;
            MinimizeButton.Margin = new Thickness(
                0,
                0,
                nativeCaptionButtonAreaWidth,
                0);
            NativeCaptionBottomFill.Width = _captionButtonAreaWidth;
        }
    }

    private nint WindowProcedure(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message != WmNcHitTest || !GetWindowRect(windowHandle, out var windowBounds))
        {
            return nint.Zero;
        }

        var screenX = unchecked((short)(long)lParam);
        var screenY = unchecked((short)((long)lParam >> 16));
        var dpiScale = GetDpiForWindow(windowHandle) / 96d;
        var resizeBand = Math.Max(
            1,
            (int)Math.Ceiling(SystemParameters.WindowResizeBorderThickness.Top * dpiScale));

        var isTopEdge = screenY >= windowBounds.Top
                        && screenY < windowBounds.Top + resizeBand
                        && screenX >= windowBounds.Left + resizeBand
                        && screenX < windowBounds.Right - resizeBand;
        if (!isTopEdge)
        {
            var captionBottom = windowBounds.Top + (int)Math.Ceiling(CaptionHeight * dpiScale);
            var nativeButtonsLeft = windowBounds.Right
                                    - (int)Math.Ceiling(_captionButtonAreaWidth * dpiScale);
            var isCaption = screenY >= windowBounds.Top + resizeBand
                            && screenY < captionBottom
                            && screenX >= windowBounds.Left
                            && screenX < nativeButtonsLeft;
            if (!isCaption
                || ApplicationTitleBar.IsPointOverApplicationMenu(new Point(screenX, screenY)))
            {
                return nint.Zero;
            }

            handled = true;
            return HtCaption;
        }

        handled = true;
        return HtTop;
    }

    private void ShowAboutDialog()
    {
        var aboutWindow = new AboutWindow(_localDocumentService, _externalUriService)
        {
            Owner = this
        };

        _ = aboutWindow.ShowDialog();
    }

    private void ShowSupportDialog()
    {
        var supportWindow = new SupportWindow(_externalUriService)
        {
            Owner = this
        };

        _ = supportWindow.ShowDialog();
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ShowSettingsDialog()
    {
        using var viewModel = new SettingsViewModel(
            _services.Orchestrator,
            _services.RuntimeService,
            _services.HomecomingInstallationService,
            _services.SettingsService,
            _services.SessionStore,
            _folderInteractionService);
        var settingsWindow = new SettingsWindow
        {
            Owner = this,
            DataContext = viewModel
        };

        _ = settingsWindow.ShowDialog();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.DisposeMainWindowViewModel();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out WindowBounds windowBounds);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowBounds
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
