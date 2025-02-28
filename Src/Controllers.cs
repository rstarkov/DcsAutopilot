using System.Windows.Input;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

class RollAutoTrim : FlightControllerBase
{
    public override string Name { get; set; } = "Roll Auto-Trim";
    private bool _active;
    public bool UsingBankRate { get; private set; }

    public override void Reset()
    {
        _active = false;
        _status = "(no data)";
        UsingBankRate = false;
    }

    public override ControlData ProcessFrame(FrameData frame)
    {
        UsingBankRate = Math.Abs(frame.Bank) > 30;
        if (!_active)
        {
            _status = "(press T)";
            return null;
        }
        var ctrl = new ControlData();
        var rollRate = UsingBankRate ? frame.BankRate : frame.GyroRoll; // BankRate works better for keeping a turn absolutely steady, as the GyroRoll is never exactly zero in this scenario
        bool supportsAbsoluteTrim = Dcs.LastBulk?.Aircraft == "F-16C_50"; // can not only set absolute trim, but also read it back (critical to interop with manual trim nicely)
        var P = supportsAbsoluteTrim ? 0.1 : Math.Abs(rollRate) < 2 ? 0.1 : 0.5;
        var trimRateLimit = !supportsAbsoluteTrim ? 1.0 : 0.2; // Viper: -1.0 to 1.0 trim takes 10 seconds, so 20%/s max
        var trimRate = -(P * rollRate).Clip(-trimRateLimit, trimRateLimit);
        if (!supportsAbsoluteTrim && Math.Abs(rollRate) < 0.10) // one tick of relative roll trim changes roll rate by about 0.1 deg/sec so don't try to make this trim any better
            trimRate = 0;
        _status = Util.SignStr(trimRate * 100, "0.0", "⮜ ", "⮞ ", "⬥ ") + (supportsAbsoluteTrim ? "%/s" : "%");
        if (supportsAbsoluteTrim)
            ctrl.RollTrim = (frame.TrimRoll.Value + trimRate * frame.dT).Clip(-1, 1);
        else
            ctrl.RollTrimRate = trimRate;
        return ctrl;
        // pitch trim for Hornet: one tick changes pitch rate by about 0.02 deg/sec
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

class SmartThrottle : FlightControllerBase
{
    public override string Name { get; set; } = "Smart Throttle";
    public bool AllowAfterburner { get; set; } = false;
    public bool AllowSpeedbrake { get; set; } = true;
    public double ThrottleInput { get; set; }
    public double? TargetSpeedIasKts { get; set; }
    public bool AfterburnerActive { get; private set; }
    public bool SpeedbrakeActive { get; private set; }

    private BasicPid _pid = new() { P = 0.5, I = 0.7, D = 0.05, MinControl = 0, MaxControl = 2.0, IntegrationLimit = 1 /*m/s / sec*/ };
    private IFilter _throttleFilter = Filters.BesselD10;

    private double _lastSpeedBrake;

    public override ControlData ProcessFrame(FrameData frame)
    {
        ThrottleInput = Util.Linterp(0.082, 0.890, 0, 1, Dcs.Joystick.GetAxis("throttle"));
        // todo: use separate bool for active
        if (!Enabled || frame.LandingGear > 0)
        {
            _status = !Enabled ? "off" : "GEAR";
            TargetSpeedIasKts = null;
            AfterburnerActive = SpeedbrakeActive = false;
            if (frame.SimTime - _lastSpeedBrake < 2)
                return new ControlData { SpeedBrakeRate = -1 };
            return null;
        }

        var t = _throttleFilter.Step(ThrottleInput);
        if (t > 0.7)
        {
            TargetSpeedIasKts = null;
            AfterburnerActive = SpeedbrakeActive = false;
            _status = "THR";
            return null;
        }
        else
        {
            var ctrl = new ControlData();
            TargetSpeedIasKts = Util.Linterp(0, 0.65, 180, 500, t.Clip(0, 0.65));
            _pid.MaxControl = AllowAfterburner ? 2.0 : 1.5;
            var speedError = TargetSpeedIasKts.Value - frame.SpeedIndicated.MsToKts();
            ctrl.ThrottleAxis = _pid.Update(speedError.KtsToMs(), frame.dT);
            _status = "act";
            AfterburnerActive = ctrl.ThrottleAxis > 1.5;
            SpeedbrakeActive = false;
            if (AllowSpeedbrake && speedError < -20)
            {
                ctrl.SpeedBrakeRate = 1;
                SpeedbrakeActive = true;
                _lastSpeedBrake = frame.SimTime;
            }
            else if (frame.SimTime - _lastSpeedBrake < 2)
                ctrl.SpeedBrakeRate = -1;
            return ctrl;
        }
    }

    public override bool HandleKey(KeyEventArgs e)
    {
        if (e.Key == Key.T && e.Modifiers == default)
        {
            // todo: use separate bool for active
            Enabled = !Enabled;
            return true;
        }
        return false;
    }
}
