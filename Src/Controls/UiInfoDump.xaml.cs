using System.Text;
using System.Windows.Controls;
using static DcsAutopilot.Globals;

namespace DcsAutopilot;

public partial class UiInfoDump : UserControl
{
    public UiInfoDump()
    {
        InitializeComponent();
    }

    public void UpdateGuiTimer()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Altitude ASL: {Dcs.LastFrame?.AltitudeAsl.MetersToFeet():#,0.000} ft");
        sb.AppendLine($"Vertical speed: {Dcs.LastFrame?.SpeedVertical.MetersToFeet() * 60:#,0.000} ft/min");
        sb.AppendLine($"AoA: {Dcs.LastFrame?.AngleOfAttack:0.00}°    AoSS: {Dcs.LastFrame?.AngleOfSideSlip:0.000}°");
        sb.AppendLine($"Mach: {Dcs.LastFrame?.SpeedMach:0.00000}    IAS: {Dcs.LastFrame?.SpeedIndicated.MsToKts():0.0} kts");
        sb.AppendLine($"Pitch: {Dcs.LastFrame?.Pitch:0.00}°/{Dcs.LastFrame?.VelPitch:0.00}°   Bank: {Dcs.LastFrame?.Bank:0.00}°   Hdg: {Dcs.LastFrame?.Heading:0.00}°");
        sb.AppendLine($"Gyros: pitch={Dcs.LastFrame?.GyroPitch:0.00}   roll={Dcs.LastFrame?.GyroRoll:0.00}   yaw={Dcs.LastFrame?.GyroYaw:0.00}");
        sb.AppendLine($"Joystick: {Dcs.LastFrame?.JoyPitch:0.000}   {Dcs.LastFrame?.JoyRoll:0.000}   {Dcs.LastFrame?.JoyYaw:0.000}   T:{Dcs.LastFrame?.JoyThrottle1:0.000}");
        sb.AppendLine($"Flaps: {Dcs.LastFrame?.Flaps:0.000}   Speedbrakes: {Dcs.LastFrame?.Airbrakes:0.000}   Gear: {Dcs.LastFrame?.LandingGear:0.000}");
        sb.AppendLine($"Fuel: flow={Dcs.LastFrame?.FuelFlow:0.000}   int={Dcs.LastFrame?.FuelInternal:0.000}   ext={Dcs.LastFrame?.FuelExternal:0.000}");
        sb.AppendLine($"Acc: {Dcs.LastFrame?.AccX:0.000} / {Dcs.LastFrame?.AccY:0.000} / {Dcs.LastFrame?.AccZ:0.000}");
        sb.AppendLine($"Test: {Dcs.LastFrame?.FuelFlow:#,0} / {Dcs.LastFrame?.Test1:0.000} / {Dcs.LastFrame?.Test2:0.000} / {Dcs.LastFrame?.Test3:0.000} / {Dcs.LastFrame?.Test4:0.000}");
        lblInfo.Text = sb.ToString();
    }
}
