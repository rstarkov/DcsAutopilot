using System.Windows.Input;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

class RollAutoTrim : FlightControllerBase
{
    public override string Name { get; set; } = "Roll Auto-Trim";
    private bool _active;

    public override void Reset()
    {
        _active = false;
        _status = "(no data)";
    }

    public override ControlData ProcessFrame(FrameData frame)
    {
        if (!_active)
        {
            _status = "(press T)";
            return null;
        }
        var ctrl = new ControlData();
        bool supportsAbsoluteTrim = Dcs.LastBulk?.Aircraft == "F-16C_50"; // can not only set absolute trim, but also read it back (critical to interop with manual trim nicely)
        var P = supportsAbsoluteTrim ? 0.1 : 0.5;
        var trimRateLimit = !supportsAbsoluteTrim ? 1.0 : 0.2; // Viper: -1.0 to 1.0 trim takes 10 seconds, so 20%/s max
        var rollRate = frame.GyroRoll;
        var trimRate = -(P * rollRate).Clip(-trimRateLimit, trimRateLimit);
        if (!supportsAbsoluteTrim && Math.Abs(rollRate) < 0.10) // one tick of relative roll trim changes roll rate by about 0.1 deg/sec so don't try to make this trim any better
            trimRate = 0;
        _status = Util.SignStr(trimRate * 100, "0.0", "⮜ ", "⮞ ", "⬥ ") + (supportsAbsoluteTrim ? "%/s" : "%");
        if (supportsAbsoluteTrim)
            ctrl.RollTrim = (frame.TrimRoll.Value + trimRate * frame.dT).Clip(-1, 1);
        else
            ctrl.RollTrimRate = trimRate;
        return ctrl;
    }

    public override bool HandleKey(KeyEventArgs e)
    {
        if (e.Key == Key.T && e.Modifiers == default)
        {
            _active = e.Down;
            return true;
        }
        return false;
    }
}
