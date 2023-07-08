using System;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

class HornetAutoTrim : IFlightController
{
    private string _status = "";
    public string Status => _status;
    public double _timeoutUntil;
    private double _neutralPitch = 0.120;
    private double _neutralRoll = 0;

    public bool Enabled { get; set; } = false;

    public void NewSession(BulkData bulk)
    {
    }

    public void ProcessBulkUpdate(BulkData bulk)
    {
    }

    public ControlData ProcessFrame(FrameData frame)
    {
        if (!Enabled)
            return null;
        var ctrl = new ControlData();
        _status = "";
        if (Math.Abs(frame.Bank) > 15)
        {
            _status = "[bank limit]";
            return ctrl;
        }
        if (Math.Abs(frame.JoyPitch - _neutralPitch) > 0.005 || Math.Abs(frame.JoyRoll - _neutralRoll) > 0.005)
        {
            _status = "[stick deflection]";
            _timeoutUntil = frame.SimTime + 1.5;
            return ctrl;
        }
        if (frame.SimTime < _timeoutUntil)
        {
            _status = "[stick timeout]";
            return ctrl;
        }
        {
            var rate = frame.GyroPitch;
            if (Math.Abs(rate) < 0.02) // in the Hornet one tick of pitch trim changes roll rate by about 0.02 deg/sec
                _status += $" [pitch]";
            else
            {
                _status += $" [PITCH]";
                ctrl.PitchTrimRate = (-rate * 2.0).Clip(-1, 1);
            }
        }
        {
            var rate = frame.GyroRoll;
            if (Math.Abs(rate) < 0.10) // in the Hornet one tick of roll trim changes roll rate by about 0.1 deg/sec
                _status += $" [roll]";
            else
            {
                _status += $" [ROLL]";
                ctrl.RollTrimRate = (-rate * 0.5).Clip(-1, 1);
            }
        }
        return ctrl;
    }
}

class HornetSmartThrottle : IFlightController
{
    public bool Enabled { get; set; } = false;
    public string Status { get; private set; } = "";
    public bool AllowAfterburner { get; set; } = false;
    public bool AllowSpeedbrake { get; set; } = true;
    public double ThrottleInput { get; set; }
    public double? TargetSpeedIasKts { get; set; }
    public bool AfterburnerActive { get; private set; }
    public bool SpeedbrakeActive { get; private set; }

    private BasicPid _pid = new() { P = 0.5, I = 0.7, D = 0.05, MinControl = 0, MaxControl = 2.0, IntegrationLimit = 1 /*m/s / sec*/ };
    private IFilter _throttleFilter = Filters.BesselD10;

    public void NewSession(BulkData bulk)
    {
    }

    public void ProcessBulkUpdate(BulkData bulk)
    {
    }

    private double _lastSpeedBrake;

    public ControlData ProcessFrame(FrameData frame)
    {
        if (!Enabled || frame.LandingGear > 0)
        {
            Status = !Enabled ? "off" : "GEAR";
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
            Status = "THR";
            return null;
        }
        else
        {
            var ctrl = new ControlData();
            TargetSpeedIasKts = Util.Linterp(0, 0.65, 180, 500, t.Clip(0, 0.65));
            _pid.MaxControl = AllowAfterburner ? 2.0 : 1.5;
            var speedError = TargetSpeedIasKts.Value - frame.SpeedIndicated.MsToKts();
            ctrl.ThrottleAxis = _pid.Update(speedError.KtsToMs(), frame.dT);
            Status = "act";
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
}

class HornetSlowFlightController : IFlightController
{
    private BasicPid _vspeed2pitchPID = new() { P = 0.004, I = 0.003, D = 0.00127 * 0.05, MinControl = -90.ToRad(), MaxControl = 90.ToRad(), IntegrationLimit = 1 /*m/s / sec*/ }; // oscillates at P=0.005 T=3.4s
    private BasicPid _pitch2axisPID = new() { P = 2, I = 1, D = 0.3, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ }; // oscillates at P=7 T=2.74s
    private BasicPid _speed2axisPID = new() { P = 0.5, I = 0.7, D = 0.05, MinControl = 0, MaxControl = 1.5, IntegrationLimit = 1 /*m/s / sec*/ };
    //private BasicPid _bank2axisPID = new() { P = 2.4, I = 2.8, D = 0.51, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ }; // oscillates at P=4 T=1.7
    private BasicPid _bank2axisPID = new() { P = 5, I = 0, D = 0, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ }; // oscillates at P=4 T=1.7
    // for the afterburner phase
    private BasicPid _vspeed2axisSlowPID = new() { P = 0.02, I = 0.001, D = 0, MinControl = -0.25, MaxControl = 0.25, IntegrationLimit = 10 /*m/s / sec*/ };
    private BasicPid _alt2axisSlowPID = new() { P = 0.02, I = 0.001, D = 0, MinControl = -0.25, MaxControl = 0.25, IntegrationLimit = 10 /*m/s / sec*/ };
    private BasicPid _pitch2axisSlowPID = new() { P = 6, I = 1, D = 0.3, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ };
    private BasicPid _bank2axisSlowPID = new() { P = 15, I = 0, D = 0, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ };

    private SmoothMover _pitch = new(1, -1, 1);
    private SmoothMover _throttle = new(1, 0, 2);
    private double wantedSpeed = 200;

    public double TargetAltitudeFt { get; set; } = 2000;

    public string Status => $"vspd={_vspeed2pitchPID.Integrating}; speed={_speed2axisPID.Integrating};\npitch={_pitch2axisPID.Integrating}; bank={_bank2axisPID.Integrating}; wanted={wantedSpeed:0.0}";

    public bool Enabled { get; set; } = true;

    public void NewSession(BulkData bulk)
    {
        _pitch.Reset(0);
        _throttle.Reset(1);
    }

    public void ProcessBulkUpdate(BulkData bulk)
    {
    }

    public ControlData ProcessFrame(FrameData frame)
    {
        var ctl = new ControlData();

        if (wantedSpeed > 140)
            wantedSpeed -= frame.dT / 1.0;
        if (wantedSpeed > 120)
            wantedSpeed -= frame.dT / 2.0;
        if (wantedSpeed > 110)
            wantedSpeed -= frame.dT / 3.0;
        else if (wantedSpeed > 102)
            wantedSpeed -= frame.dT / 5.0;
        else if (wantedSpeed > 101)
            wantedSpeed -= frame.dT / 20;
        else
            wantedSpeed = 101;
        var wantedHeading = frame.SimTime < 435 ? 233.01 : 227.0;
        var wantedBank = (wantedHeading.ToRad() - frame.Heading).Clip(-1.ToRad(), 1.ToRad());
        var wantedAltitude = frame.SimTime < 100 ? 300 : frame.SimTime < 300 ? Util.Linterp(100, 300, 300, 150, frame.SimTime) : frame.SimTime < 380 ? Util.Linterp(300, 380, 150, 90, frame.SimTime) : 90;
        var wantedVS = (0.05 * (wantedAltitude.FeetToMeters() - frame.AltitudeAsl)).Clip(-100.FeetToMeters(), 100.FeetToMeters());
        if (wantedSpeed > 101 || false /* true to prevent transition to the afterburner phase */)
        {
            var wantedPitch = _vspeed2pitchPID.Update(wantedVS - frame.SpeedVertical, frame.dT);
            var wantedPitchAxis = _pitch2axisPID.Update(wantedPitch - frame.Pitch, frame.dT);
            ctl.PitchAxis = _pitch.MoveTo(wantedPitchAxis, frame.SimTime);
            ctl.RollAxis = _bank2axisPID.Update(wantedBank - frame.Bank, frame.dT);
            ctl.ThrottleAxis = _speed2axisPID.Update(wantedSpeed.KtsToMs() - frame.SpeedIndicated, frame.dT);
        }
        else
        {
            ctl.ThrottleAxis = 1.65 + _vspeed2axisSlowPID.Update(wantedVS - frame.SpeedVertical, frame.dT);
            ctl.PitchAxis = _pitch.MoveTo(1, frame.SimTime);//_pitch.MoveTo(_pitch2axisSlowPID.Update(50.ToRad() - frame.Pitch, frame.dT), frame.SimTime);
            ctl.RollAxis = _bank2axisSlowPID.Update(wantedBank - frame.Bank, frame.dT);
        }

        return ctl;
    }
}

public class BasicPid
{
    public double P { get; set; }
    public double I { get; set; }
    public double D { get; set; }

    public double MinControl { get; set; }
    public double MaxControl { get; set; }
    public double ErrorIntegral { get; set; }
    public double Derivative { get; set; }
    public double DerivativeSmoothing { get; set; } = 0.8;
    public double IntegrationLimit { get; set; } // derivative must be less than this to start integrating error
    private double _prevError;
    public bool Integrating { get; private set; }

    public double Update(double error, double dt)
    {
        Derivative = DerivativeSmoothing * Derivative + (1 - DerivativeSmoothing) * (error - _prevError) / dt;
        _prevError = error;

        double output = P * error + I * ErrorIntegral + D * Derivative;

        output = output.Clip(MinControl, MaxControl);
        Integrating = Math.Abs(Derivative) < IntegrationLimit && output > MinControl && output < MaxControl;
        if (Integrating)
            ErrorIntegral += error * dt;
        return output;
    }

    public BasicPid SetP(double p)
    {
        P = p;
        I = 0;
        D = 0;
        return this;
    }

    public BasicPid SetZiNiClassic(double pCritical, double tOsc)
    {
        // https://en.wikipedia.org/wiki/Ziegler%E2%80%93Nichols_method
        P = 0.6 * pCritical;
        I = 1.2 * pCritical / tOsc;
        D = 0.075 * pCritical * tOsc;
        return this;
    }

    public BasicPid SetZiNiSome(double pCritical, double tOsc)
    {
        P = 0.33 * pCritical;
        I = 0.66 * pCritical / tOsc;
        D = 0.11 * pCritical * tOsc;
        return this;
    }

    public BasicPid SetZiNiNone(double pCritical, double tOsc)
    {
        P = 0.20 * pCritical;
        I = 0.40 * pCritical / tOsc;
        D = 0.066 * pCritical * tOsc;
        return this;
    }
}
