using System.Windows;
using System.Windows.Controls;
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
        _updating = false;
    }

    public void UpdateGuiTimer()
    {
        var ctrl = Dcs.GetController<SmartThrottle>();
        if (ctrl?.Enabled != true)
            return;
        MyProperties.SetIndicatorState(lblAftB, ctrl.AfterburnerActive ? "redtext" : "off");
        MyProperties.SetIndicatorState(lblSpdB, ctrl.SpeedbrakeActive ? "yellowtext" : "off");
        MyProperties.SetIndicatorState(lblSpdHold, ctrl.AutothrottleSpeedKts == null ? "off" : Math.Abs(ctrl.AutothrottleSpeedKts.Value - (Dcs.LastFrame?.SpeedCalibrated ?? 0).MsToKts()) <= 15 ? "greentext" : "yellowtext");
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
}
