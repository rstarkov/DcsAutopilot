using System.Windows.Controls;
using static DcsAutopilot.Globals;

namespace DcsAutopilot;

public partial class UiControlPositions : UserControl
{
    public UiControlPositions()
    {
        InitializeComponent();
    }

    public void UpdateGuiTimer()
    {
        void setSlider(Slider sl, double? value)
        {
            sl.IsEnabled = Dcs.IsRunning ? value != null : false;
            sl.Value = Dcs.IsRunning ? value ?? 0 : 0;
        }
        setSlider(ctrlPitch, -Dcs.LastControl?.PitchAxis);
        setSlider(ctrlRoll, Dcs.LastControl?.RollAxis);
        setSlider(ctrlYaw, Dcs.LastControl?.YawAxis);
        setSlider(ctrlThrottle, Dcs.LastControl?.ThrottleAxis);
    }
}
