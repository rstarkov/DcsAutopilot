using System.Windows.Input;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public class RollAutoTrim : FlightControllerBase
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

public class SmartThrottle : FlightControllerBase
{
    public override string Name { get; set; } = "Smart Throttle";
    public bool UseIdleSpeedbrake { get; set; } = true;
    public bool UseAfterburnerDetent { get; set; } = true;
    public bool AutothrottleAfterburner { get; set; } = false;

    public double? AutothrottleSpeedKts { get; set; }
    public bool AfterburnerActive { get; private set; }
    public bool SpeedbrakeActive { get; private set; }

    private BasicPid _pid = new() { P = 0.5, I = 0.7, D = 0.05, MinControl = 0, MaxControl = 2.0, IntegrationLimit = 1 /*m/s / sec*/ };
    private double _autothrottleInitialPos; // to detect throttle movement and disengage
    private double _lastSpeedBrake; // time at which speedbrake was last extended - to enable us to retract it for N seconds when no longer needed
    private bool _pastAfterburnerDetent;
    private double _lastAfterburnerSound;

    private Sound SndAfterburnerBump = new("LoudClick.mp3", 50);
    private Sound SndAfterburnerUnbump = new("ReverseLoudClick.mp3", 40);
    private Sound SndAfterburnerActive = new("SarahAfterburner.mp3", 50);
    private Sound SndAutothrottleEngaged = new("AirbusAutopilotDisengageSingle.mp3");
    private Sound SndAutothrottleDisengaged = new("AirbusAutopilotDisengage.mp3");

    public override ControlData ProcessFrame(FrameData frame)
    {
        var ctrl = new ControlData();
        var throttlePos = mapThrottle(Dcs.Joystick.GetAxis("throttle"));
        var prevThrottlePos = mapThrottle(Dcs.Joystick.GetAxisPrev("throttle"));
        ctrl.ThrottleAxis = throttlePos;

        if (AutothrottleSpeedKts != null)
        {
            _pid.MaxControl = AutothrottleAfterburner ? 2.0 : 1.5;
            var speedError = AutothrottleSpeedKts.Value - frame.SpeedIndicated.MsToKts();
            ctrl.ThrottleAxis = _pid.Update(speedError.KtsToMs(), frame.dT);
            if (speedError < -20)
                ctrl.SpeedBrakeRate = 1;
            // detect movement and disengage
            if (Math.Abs(throttlePos - _autothrottleInitialPos) > 0.1)
            {
                AutothrottleSpeedKts = null;
                SndAutothrottleDisengaged?.Play();
            }
        }
        else
        {
            if (UseAfterburnerDetent)
            {
                if (!_pastAfterburnerDetent)
                {
                    if (throttlePos >= 1.99)
                    {
                        _pastAfterburnerDetent = true;
                        _lastAfterburnerSound = frame.SimTime;
                        SndAfterburnerActive?.Play();
                    }
                    else if (throttlePos > 1.5)
                    {
                        ctrl.ThrottleAxis = 1.5;
                        if (prevThrottlePos <= 1.5)
                            SndAfterburnerBump?.Play();
                    }
                }
                else
                {
                    if (throttlePos < 0.5)
                    {
                        _pastAfterburnerDetent = false;
                        if (prevThrottlePos >= 0.5)
                            SndAfterburnerUnbump?.Play();
                    }
                }
            }
            if (UseIdleSpeedbrake)
            {
                if (throttlePos <= 0.01 && frame.SpeedIndicated.MsToKts() > 200)
                    ctrl.SpeedBrakeRate = 1;
            }
        }

        // Afterburner warning
        if (ctrl.ThrottleAxis > 1.5 && frame.SimTime - _lastAfterburnerSound > 30)
        {
            _lastAfterburnerSound = frame.SimTime;
            SndAfterburnerActive?.Play();
        }
        // Retract speedbrake for 2 seconds if we're not trying to extend it
        if (ctrl.SpeedBrakeRate > 0)
            _lastSpeedBrake = frame.SimTime;
        if (ctrl.SpeedBrakeRate == null && frame.SimTime - _lastSpeedBrake < 2)
            ctrl.SpeedBrakeRate = -1;
        // Update indicators
        AfterburnerActive = ctrl.ThrottleAxis > 1.5;
        SpeedbrakeActive = ctrl.SpeedBrakeRate > 0;
        return ctrl;
    }

    public override bool HandleKey(KeyEventArgs e)
    {
        if (e.Down && e.Key == Key.H && e.Modifiers == default)
        {
            AutothrottleSpeedKts = Dcs.LastFrame.SpeedIndicated.MsToKts();
            _autothrottleInitialPos = mapThrottle(Dcs.Joystick.GetAxis("throttle"));
            SndAutothrottleEngaged?.Play();
            return true;
        }
        return false;
    }

    private double mapThrottle(double throttleAxis) => Util.Linterp(0, 1, 0, 2, throttleAxis);
}

class SoundWarnings : FlightControllerBase
{
    public override string Name { get; set; } = "Sound warnings";

    public bool UseGearNotUp { get; set; } = true;
    public bool? IsGearNotUp { get; set; }
    public double GearNotUpMinAltFt { get; set; } = 4000;
    public double GearNotUpMinSpdKts { get; set; } = 300;
    private Sound SndGearNotUp = new("SarahGearUp.mp3");
    private double _lastGearNotUpT;

    public bool UseGearNotDown { get; set; } = true;
    public bool? IsGearNotDown { get; set; }
    public double GearNotDownMaxAltFt { get; set; } = 800;
    public double GearNotDownMaxSpdKts { get; set; } = 220;
    private Sound SndGearNotDown = new("SarahGearDown.mp3");
    private double _lastGearNotDownT;

    public bool UseAfterburner { get; set; }
    public bool? IsAfterburner { get; set; }
    private double _lastAfterburnerT;

    public override void Reset()
    {
        _lastGearNotUpT = double.MinValue;
        _lastGearNotDownT = double.MinValue;
        _lastAfterburnerT = double.MinValue;
    }

    public override ControlData ProcessFrame(FrameData frame)
    {
        var speedKts = frame.SpeedIndicated.MsToKts();
        var altitudeAglFt = frame.AltitudeRadar.MetersToFeet();
        if (!UseGearNotUp)
            IsGearNotUp = null;
        else
        {
            IsGearNotUp = frame.LandingGear > 0 && speedKts > GearNotUpMinSpdKts && altitudeAglFt > GearNotUpMinAltFt;
            if (IsGearNotUp == true && frame.SimTime - _lastGearNotUpT > 60)
            {
                SndGearNotUp?.Play();
                _lastGearNotUpT = frame.SimTime;
            }
        }

        if (!UseGearNotDown)
            IsGearNotDown = null;
        else
        {
            IsGearNotDown = frame.LandingGear < 1 && speedKts < GearNotDownMaxSpdKts && altitudeAglFt < GearNotDownMaxAltFt;
            if (IsGearNotDown == true && frame.SimTime - _lastGearNotDownT > 60)
            {
                SndGearNotDown?.Play();
                _lastGearNotDownT = frame.SimTime;
            }
        }

        return null;
    }
}
