using System;
using System.Windows;
using System.Windows.Controls;

namespace DcsAutopilot;

public partial class WindComp : UserControl
{
    public WindComp()
    {
        InitializeComponent();
        SetWind(0, 0);
    }

    public void SetWind(double headwind, double crosswind)
    {
        if (Math.Abs(headwind) < 1)
        {
            ctArrowUp.Visibility = lblUp.Visibility = Visibility.Hidden;
            ctArrowDown.Visibility = lblDown.Visibility = Visibility.Hidden;
        }
        else if (headwind > 0)
        {
            ctArrowUp.Visibility = lblUp.Visibility = Visibility.Hidden;
            ctArrowDown.Visibility = lblDown.Visibility = Visibility.Visible;
            lblDown.Content = $"{headwind:0}";
        }
        else
        {
            ctArrowUp.Visibility = lblUp.Visibility = Visibility.Visible;
            ctArrowDown.Visibility = lblDown.Visibility = Visibility.Hidden;
            lblUp.Content = $"{-headwind:0}";
        }

        if (Math.Abs(crosswind) < 1)
        {
            ctArrowLeft.Visibility = lblLeft.Visibility = Visibility.Hidden;
            ctArrowRight.Visibility = lblRight.Visibility = Visibility.Hidden;
        }
        else if (crosswind > 0)
        {
            ctArrowLeft.Visibility = lblLeft.Visibility = Visibility.Hidden;
            ctArrowRight.Visibility = lblRight.Visibility = Visibility.Visible;
            lblRight.Content = $"{crosswind:0}";
        }
        else
        {
            ctArrowLeft.Visibility = lblLeft.Visibility = Visibility.Visible;
            ctArrowRight.Visibility = lblRight.Visibility = Visibility.Hidden;
            lblLeft.Content = $"{-crosswind:0}";
        }
    }
}
