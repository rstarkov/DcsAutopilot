using System.Windows;
using System.Windows.Controls;
using static DcsAutopilot.Globals;

namespace DcsAutopilot;

public partial class UiRollAutoTrim : UserControl
{
    public UiRollAutoTrim()
    {
        InitializeComponent();
        if (Dcs?.FlightControllers != null)
            Dcs.FlightControllers.CollectionChanged += (_, _) => pnlMain.DataContext = Dcs.GetController<RollAutoTrim>();
    }

    private void btnOnOff_Click(object sender, RoutedEventArgs e)
    {
        var ctrl = Dcs.GetController<RollAutoTrim>(orAdd: true);
        ctrl.Enabled = !ctrl.Enabled;
        UpdateGui();
        UpdateGuiTimer();
    }

    public void UpdateGui()
    {
        var ctrl = Dcs.GetController<RollAutoTrim>();
        if (ctrl?.Enabled != true)
        {
            lblRoll.Content = "?";
            lblTrim.Content = "?";
            lblState.Content = "disabled";
        }
    }

    public void UpdateGuiTimer()
    {
        var ctrl = Dcs.GetController<RollAutoTrim>();
        if (ctrl?.Enabled != true)
            return;
        lblRollLabel.Content = ctrl.UsingBankRate ? "Bank:" : "Roll:";
        lblRoll.Content = Dcs.LastFrame == null ? "?" : ctrl.UsingBankRate ? (Util.SignStr(Dcs.LastFrame.Bank, "0.00", "⮜ ", "⮞ ", "⬥ ") + "°") : (Util.SignStr(Dcs.LastFrame.GyroRoll, "0.00", "⮜ ", "⮞ ", "⬥ ") + "°/s");
        lblTrim.Content = Dcs.LastFrame?.TrimRoll == null ? "?" : Util.SignStr(Dcs.LastFrame.TrimRoll.Value * 100, "0.0", "⮜ ", "⮞ ", "⬥ ") + "%";
        lblState.Content = ctrl.Status;
    }
}
