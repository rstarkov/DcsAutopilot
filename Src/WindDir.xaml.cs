using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DcsAutopilot;

public partial class WindDir : UserControl
{
    public WindDir()
    {
        InitializeComponent();
        SetWind(0, 0, 0);
    }

    public void SetWind(double windVecDeg, double windSpeedKts, double trueHdgDeg)
    {
        lblTop.Content = $"{(windVecDeg + 180) % 360:0}°";
        lblBottom.Content = $"{windSpeedKts:0} kt";
        vbArrow.RenderTransform = new RotateTransform(180 + windVecDeg - trueHdgDeg, 30, 30);
        vbArrow.Visibility = windSpeedKts > 1 ? Visibility.Visible : Visibility.Hidden;
    }
}
