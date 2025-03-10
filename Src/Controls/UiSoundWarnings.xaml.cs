using System.Windows.Controls;
using static DcsAutopilot.Globals;

namespace DcsAutopilot;

public partial class UiSoundWarnings : UserControl
{
    private bool _updating = false;

    public UiSoundWarnings()
    {
        InitializeComponent();
        if (Dcs?.FlightControllers != null)
            Dcs.FlightControllers.CollectionChanged += (_, _) => pnlMain.DataContext = Dcs.GetController<SoundWarnings>();
    }

    private void btnOnOff_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var ctrl = Dcs.GetController<SoundWarnings>(orAdd: true);
        ctrl.Enabled = !ctrl.Enabled;
        UpdateGui();
        UpdateGuiTimer();
    }

    public void UpdateGui()
    {
        _updating = true;
        var ctrl = Dcs.GetController<SoundWarnings>();
        chkUseAfterburnerActive.IsChecked = ctrl?.UseAfterburner;
        chkUseGearNotUp.IsChecked = ctrl?.UseGearNotUp;
        chkUseGearNotDown.IsChecked = ctrl?.UseGearNotDown;
        _updating = false;
    }

    public void UpdateGuiTimer()
    {
        var ctrl = Dcs.GetController<SoundWarnings>();
        if (ctrl?.Enabled != true)
            return;
        MyProperties.SetIndicatorState(lblAfterburner, ctrl.IsAfterburner == true ? "redtext" : "off");
        MyProperties.SetIndicatorState(lblGearNotUp, ctrl.IsGearNotUp == true ? "yellowtext" : "off");
        MyProperties.SetIndicatorState(lblGearNotDown, ctrl.IsGearNotDown == true ? "yellowtext" : "off");
    }

    private void chkUseAfterburnerActive_Checked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_updating) return;
        Dcs.GetController<SoundWarnings>().UseAfterburner = chkUseAfterburnerActive.IsChecked == true;
    }

    private void chkUseGearNotUp_Checked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_updating) return;
        Dcs.GetController<SoundWarnings>().UseGearNotUp = chkUseGearNotUp.IsChecked == true;
    }

    private void chkUseGearNotDown_Checked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_updating) return;
        Dcs.GetController<SoundWarnings>().UseGearNotDown = chkUseGearNotDown.IsChecked == true;
    }
}
