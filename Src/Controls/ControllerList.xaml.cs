using System.Windows;
using System.Windows.Controls;
using static DcsAutopilot.Globals;

namespace DcsAutopilot;

public partial class ControllerList : UserControl
{
    public ControllerList()
    {
        InitializeComponent();
        ctControllers.ItemsSource = Dcs?.FlightControllers;
    }

    private void btnOnOff_Click(object sender, RoutedEventArgs e)
    {
        var ctrl = (FlightControllerBase)((Button)sender).DataContext;
        ctrl.Enabled = !ctrl.Enabled;
    }

    private void ControllerButton_Click(object sender, RoutedEventArgs e)
    {
        var ctrl = (FlightControllerBase)(ctControllers.SelectedItem);
        var signal = ((Button)sender).Content.ToString();
        if (ctrl.Enabled)
            ctrl.HandleSignal(signal);
    }
}
