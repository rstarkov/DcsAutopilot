using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DcsAutopilot.Controls;

static class UiShared
{
    public static Brush BrushToggleBorderNormal = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
    public static Brush BrushToggleBorderActive = new SolidColorBrush(Color.FromRgb(0x00, 0x99, 0x07)); // 1447FF
    public static Brush BrushToggleBorderHigh = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));
    public static Brush BrushToggleBackNormal = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    public static Brush BrushToggleBackActive = new SolidColorBrush(Color.FromRgb(0xB5, 0xFF, 0xA3)); // BFFAFF
    public static Brush BrushToggleBackHigh = new SolidColorBrush(Color.FromRgb(0xFF, 0xDE, 0xDB));

    public static void UpdateUiPanel(FlightControllerBase ctrl, DependencyObject panel, Button btnOnOff)
    {
        if (ctrl?.Enabled == true)
        {
            btnOnOff.Content = "ON";
            enableTree(panel, true);
        }
        else
        {
            btnOnOff.Content = "off";
            enableTree(panel, false);
            enableParents(btnOnOff, panel);
        }
    }

    static void enableTree(DependencyObject obj, bool enable)
    {
        if (obj is Control ctrl)
            ctrl.IsEnabled = enable;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            enableTree(VisualTreeHelper.GetChild(obj, i), enable);
    }

    static void enableParents(DependencyObject obj, DependencyObject stop)
    {
        if (obj == null || obj == stop)
            return;
        if (obj is Control ctrl)
            ctrl.IsEnabled = true;
        enableParents(VisualTreeHelper.GetParent(obj), stop);
    }
}
