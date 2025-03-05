using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static DcsAutopilot.Globals;

namespace DcsAutopilot;

public partial class UiSmartThrottle : UserControl
{
    private bool _updating = false;

    public UiSmartThrottle()
    {
        InitializeComponent();
        if (Dcs?.FlightControllers != null)
            Dcs.FlightControllers.CollectionChanged += (_, _) => pnlMain.DataContext = Dcs.GetController<SmartThrottle>();
    }

    private void btnOnOff_Click(object sender, RoutedEventArgs e)
    {
        var ctrl = Dcs.GetController<SmartThrottle>(orAdd: true);
        ctrl.Enabled = !ctrl.Enabled;
        UpdateGui();
        UpdateGuiTimer();
    }

    public void UpdateGui()
    {
        _updating = true;
        var ctrl = Dcs.GetController<SmartThrottle>();
        chkUseIdleSpeedbrake.IsChecked = ctrl?.UseIdleSpeedbrake;
        chkUseAfterburnerDetent.IsChecked = ctrl?.UseAfterburnerDetent;
        chkAutothrottleAfterburner.IsChecked = ctrl?.AutothrottleAfterburner;
        if (ctrl?.Enabled != true)
        {
            lblAftB.Background = UiShared.BrushToggleBackNormal;
            lblSpdB.Background = UiShared.BrushToggleBackNormal;
            lblSpdHold.Background = UiShared.BrushToggleBackNormal;
            lblSpdHold.ClearValue(Label.ForegroundProperty);
        }
        _updating = false;
    }

    public void UpdateGuiTimer()
    {
        var ctrl = Dcs.GetController<SmartThrottle>();
        if (ctrl?.Enabled != true)
            return;
        lblAftB.Background = ctrl.AfterburnerActive ? UiShared.BrushToggleBackActive : UiShared.BrushToggleBackNormal;
        lblSpdB.Background = ctrl.SpeedbrakeActive ? UiShared.BrushToggleBackActive : UiShared.BrushToggleBackNormal;
        lblSpdHold.Background = ctrl.AutothrottleSpeedKts != null ? UiShared.BrushToggleBackActive : UiShared.BrushToggleBackNormal;
        if (ctrl.AutothrottleSpeedKts != null)
            lblSpdHold.Foreground = Math.Abs(ctrl.AutothrottleSpeedKts.Value - (Dcs.LastFrame?.SpeedIndicated ?? 0).MsToKts()) <= 15 ? Brushes.Black : Brushes.DarkRed;
        else
            lblSpdHold.ClearValue(Label.ForegroundProperty);
    }

    private void chkUseIdleSpeedbrake_Checked(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        Dcs.GetController<SmartThrottle>().UseIdleSpeedbrake = chkUseIdleSpeedbrake.IsChecked == true;
    }

    private void chkUseAfterburnerDetent_Checked(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        Dcs.GetController<SmartThrottle>().UseAfterburnerDetent = chkUseAfterburnerDetent.IsChecked == true;
    }

    private void chkAutothrottleAfterburner_Checked(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        Dcs.GetController<SmartThrottle>().AutothrottleAfterburner = chkAutothrottleAfterburner.IsChecked == true;
    }
}
