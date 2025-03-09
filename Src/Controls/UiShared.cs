using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DcsAutopilot;

static class UiShared
{
    public static Pen[] ChartPens = [new Pen(Brushes.Red, 1), new Pen(Brushes.Lime, 1), new Pen(Brushes.Yellow, 1)];

    static UiShared()
    {
        foreach (var pen in ChartPens)
            pen.Freeze();
    }

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

public static class MyProperties
{
    public static readonly DependencyProperty IndicatorStateProperty = DependencyProperty.RegisterAttached("IndicatorState", typeof(string), typeof(MyProperties), new PropertyMetadata(null));
    public static string GetIndicatorState(Label obj) => (string)obj.GetValue(IndicatorStateProperty);
    public static void SetIndicatorState(Label obj, string value) => obj.SetValue(IndicatorStateProperty, value);
}

public class OnOffConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? "ON" : "off";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
