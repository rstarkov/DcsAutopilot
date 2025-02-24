using System.Windows.Input;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

class RollAutoTrim : FlightControllerBase
{
    public override string Name { get; set; } = "Roll Auto-Trim";
    private bool _active = false;
    private double _rollTrim = 0;

    public override void Reset()
    {
        _active = false;
        _rollTrim = 0;
        _status = "(no data)";
    }

    public override ControlData ProcessFrame(FrameData frame)
    {
        if (!_active)
        {
            _status = "(press T)";
            return null;
        }
        var trimRate = (-0.1 * frame.GyroRoll).Clip(-0.2, 0.2); // Viper: -1.0 to 1.0 trim takes 10 seconds, so 20%/s max
        if (frame.TrimRoll != null)
            _rollTrim = frame.TrimRoll.Value;
        _rollTrim = (_rollTrim + trimRate * frame.dT).Clip(-1, 1);
        _status = Util.SignStr(trimRate * 100, "0.0", "⮜ ", "⮞ ", "⬥ ") + "%/s";
        var ctrl = new ControlData();
        ctrl.RollTrim = _rollTrim;
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
