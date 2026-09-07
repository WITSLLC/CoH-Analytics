using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Controls;

public partial class GameStatusIconView : UserControl
{
    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(
            nameof(Status),
            typeof(GameStatusState),
            typeof(GameStatusIconView),
            new PropertyMetadata(GameStatusState.Running, OnStatusChanged));

    private Storyboard? _activePulse;

    public GameStatusIconView()
    {
        InitializeComponent();
        ApplyVisualState(Status);
    }

    public GameStatusState Status
    {
        get => (GameStatusState)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GameStatusIconView view && e.NewValue is GameStatusState state)
        {
            view.ApplyVisualState(state);
        }
    }

    private void ApplyVisualState(GameStatusState state)
    {
        StopActivePulse();

        var attention = (Color)FindResource("Color.Attention");
        var success = (Color)FindResource("Color.Success");
        var error = (Color)FindResource("Color.Error");

        switch (state)
        {
            case GameStatusState.Off:
                CrystalFill.Color = success;
                CrystalIllumination.Opacity = 0.06;
                CrystalBloom.Color = success;
                CrystalBloom.BlurRadius = 0;
                CrystalBloom.Opacity = 0;
                break;

            case GameStatusState.Idle:
                CrystalFill.Color = attention;
                CrystalIllumination.Opacity = 0.60;
                CrystalBloom.Color = attention;
                CrystalBloom.BlurRadius = 10;
                CrystalBloom.Opacity = 0.24;
                BeginPulse("GameStatus.Pulse.Idle");
                break;

            case GameStatusState.Running:
                CrystalFill.Color = success;
                CrystalIllumination.Opacity = 0.80;
                CrystalBloom.Color = success;
                CrystalBloom.BlurRadius = 16;
                CrystalBloom.Opacity = 0.42;
                BeginPulse("GameStatus.Pulse.Running");
                break;

            case GameStatusState.Error:
                CrystalFill.Color = error;
                CrystalIllumination.Opacity = 0.72;
                CrystalBloom.Color = error;
                CrystalBloom.BlurRadius = 18;
                CrystalBloom.Opacity = 0.48;
                BeginPulse("GameStatus.Pulse.Error");
                break;
        }
    }

    private void BeginPulse(string resourceKey)
    {
        if (FindResource(resourceKey) is not Storyboard storyboard)
        {
            return;
        }

        _activePulse = storyboard;
        _activePulse.Begin(CrystalIllumination, true);
    }

    private void StopActivePulse()
    {
        if (_activePulse is null)
        {
            return;
        }

        _activePulse.Stop(CrystalIllumination);
        _activePulse = null;

        CrystalIllumination.BeginAnimation(UIElement.OpacityProperty, null);
    }
}
