using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public class ViperClimber : FlightControllerBase
{
    public override string Name { get; set; } = "Viper Climber";

    private BasicPid _pidThrottle = new BasicPid { MaxControl = 1.5, IntegrationLimit = 1 /*m/s / sec*/ }.SetZiNiClassic(1.3, 4.33); // F-16 at 20,000, 300kts, min weight
    private BasicPid _pidPitch = new BasicPid { MinControl = -0.1, MaxControl = 0.1, IntegrationLimit = 0.01, DerivativeSmoothing = 0.95 }.SetZiNiClassic(1.7, 3.17);
    private double _tgtSpeedKts = 300;
    private bool _initialPitchUp; // don't throttle down or alter pitch until we're close to target speed

    public override void Reset()
    {
        _initialPitchUp = true;
    }

    public override ControlData ProcessFrame(FrameData frame)
    {
        var ctrl = new ControlData();
        var tgtThrottle = 1.50;

        _pidThrottle.MinControl = tgtThrottle - 1.0; // limit error integration beyond these limits
        _pidThrottle.MaxControl = tgtThrottle + 1.0;

        var speedError = _tgtSpeedKts - frame.SpeedCalibrated.MsToKts();
        ctrl.ThrottleAxis = _pidThrottle.Update(speedError.KtsToMs(), frame.dT).Clip(0, tgtThrottle); // allow it to throttle below target, which makes it much easier for the pitch PID to stay in the stable region
        var throttleError = tgtThrottle - _pidThrottle.OutputRaw;
        var pitchAxis = _pidPitch.Update(throttleError, frame.dT);
        if (frame.VelPitch > 0 && frame.VelPitch < 1)
            pitchAxis *= frame.VelPitch;
        pitchAxis += 0.044 * Math.Sign(pitchAxis); // dead zone
        ctrl.PitchAxis = pitchAxis;
        if (frame.VelPitch <= 0)
            ctrl.PitchAxis = null;
        if (_initialPitchUp)
        {
            ctrl.ThrottleAxis = tgtThrottle;
            ctrl.PitchAxis = null;
            if (speedError > -20)
                _initialPitchUp = false;
        }

        Status = $"thr={throttleError:0.000} pitch={ctrl.PitchAxis:0.000} erri={_pidPitch.ErrorIntegral:0.000}  derr={_pidPitch.Derivative:0.000}";

        return ctrl;
    }
}

// Viper throttle: smooth control in 0.0..1.0 range. 1.5 is 0% afterburner which is marginally stronger than 1.0. There are 6 distinct afterburner settings, from 1.5 to 2.0 in 0.1 increments.
// Viper pitch: deadzone from -0.04375 to 0.04375; pitch rate commanded is linear in the low region and intersects zero at +/-0.04375.

public class ViperControl
{
    public ParamPid PidSpeed2Throttle = new(
        new Vec(200, 0, -30), new Vec(700, 40000, 30),
        //new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.5, Bias = 0.213 }.SetZiNiClassic(0.3, 5.9), 300, 300), // at 300kts, 300ft. Dlim: from above 1.2 steady 0.839 reversal; from below 3.3 steady 1.006 reversal
        //new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.4, Bias = 0.655 }.SetZiNiClassic(0.3, 4.7), 600, 300), // at 600kts, 300ft; Dlim: 0.75 from above, 0.05 from below

        new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.1, Bias = 0.205 }.SetZiNiClassic(0.4, 5.2), 200, 300, 0),
        new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.1, Bias = 0.62 }.SetZiNiClassic(0.4, 5.2), 200, 1000, +10),
        new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.1, Bias = 0.042 }.SetZiNiClassic(0.4, 5.2), 200, 1000, -5),
        new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.5, Bias = 0.655 }.SetZiNiClassic(0.3, 4.7), 600, 300, 0),
        new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.5, Bias = 0.97 }.SetZiNiClassic(0.3, 4.7), 600, 300, +5),
        new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.5, Bias = 0.25 }.SetZiNiClassic(0.3, 4.7), 600, 300, -10),

        new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.1, Bias = 0.454 }.SetZiNiClassic(0.8, 5.7), 200, 15200, 0), // d/dt 0.241 from above, 0.040 from below
        new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.1, Bias = 0.098 }.SetZiNiClassic(0.8, 5.7), 200, 15200, -4.9),
        new ParamPidPt(new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.1, Bias = 0.863 }.SetZiNiClassic(0.8, 5.7), 200, 15200, +5.5)
    );

    public void ControlSpeedIAS(FrameData frame, ControlData ctrl, double tgtKts)
    {
        PidSpeed2Throttle.Update(new Vec(frame.SpeedCalibrated.MsToKts(), frame.AltitudeAsl.MetersToFeet(), frame.VelPitch));
        ctrl.ThrottleAxis = PidSpeed2Throttle.PID.Update(tgtKts.KtsToMs() - frame.SpeedCalibrated, frame.dT);
    }
}

class ViperTune : FlightControllerBase
{
    public override string Name { get; set; } = "Viper Tune";
    //public override string Status => $"Σ={_speed2axisPID.ErrorIntegral:0.000}, Bias={_speed2axisPID.ErrorIntegral * _speed2axisPID.I:0.000}   d/dt={_speed2axisPID.Derivative:0.000}: {_Dmin:0.000} to {_Dmax:0.000}";
    //public override string Status => $"Σ={_speed2axisPID.PID.ErrorIntegral:0.000}, Bias={_speed2axisPID.PID.ErrorIntegral * _speed2axisPID.PID.I:0.000}   V={_Vmin.MsToKts():0.00} to {_Vmax.MsToKts():0.00}\r\nd/dt: {_Dmin:0.000} to {_Dmax:0.000}\r\n" + _speed2axisPID.Points.OrderByDescending(p => p.Scale).Select(p => $"{p.Scale * 100:0.00}: {p.Name}").JoinString("\r\n");

    private ViperControl _control = new();
    private double _Dmin, _Dmax;
    private double _Vmin, _Vmax;
    // setup one: altitude -> velpitch -> pitchaxis
    // setup two: altitude -> vspeed -> pitchaxis
    // setup three: altitude -> vspeed -> velpitch -> pitchaxis
    // setup four: altitude -> vspeed -> velpitch -> -> pitch -> pitchaxis
    // setup five: altitude -> compute velpitch from speed and diff -> velpitch directly to pitchaxis
    // setup six: altitude -> compute velpitch from speed and diff -> velpitch to pitch with bias calc
    // setup seven: altitude -> compute velpitch from speed and diff -> compute gyro rate directly from velpitch diff -> gyro to pitch axis
    private ParamPid _gyro2pitchPID = new(
        new Vec(200), new Vec(700),
        new ParamPidPt(new BasicPid { MinControl = -1.0, MaxControl = 1.0, IntegrationLimit = 0 }.SetZiNiClassic(0.11, 0.43), 500)
    );
    private double _tgtPitchRate = 0;

    public override ControlData ProcessFrame(FrameData frame)
    {
        _Dmin = Math.Min(_Dmin, _control.PidSpeed2Throttle.PID.Derivative);
        _Dmax = Math.Max(_Dmax, _control.PidSpeed2Throttle.PID.Derivative);
        _Vmin = Math.Min(_Vmin, frame.SpeedCalibrated);
        _Vmax = Math.Max(_Vmax, frame.SpeedCalibrated);
        var ctl = new ControlData();
        _control.ControlSpeedIAS(frame, ctl, 500);
        _gyro2pitchPID.Update(new Vec(frame.SpeedCalibrated));
        ctl.PitchAxis = fixPitchAxis(2 * _gyro2pitchPID.PID.Update(_tgtPitchRate - frame.GyroPitch, frame.dT));
        // feed an estimate of what the pitch axis should be OUTSIDE the PID
        return ctl;
    }

    private double fixPitchAxis(double pitchAxis)
    {
        if (pitchAxis > 0)
            return 0.04375 + pitchAxis;
        else if (pitchAxis < 0)
            return -0.04375 + pitchAxis;
        else
            return 0;
    }

    public override void HandleSignal(string signal)
    {
        if (signal == "A")
        {
            _Dmin = 0;
            _Dmax = 0;
            _Vmin = _Vmax = Dcs.LastFrame.SpeedCalibrated;
        }
        else if (signal == "B")
            _tgtPitchRate = 0;
        else if (signal == "C")
            _tgtPitchRate = 3;
        else if (signal == "D")
            _tgtPitchRate = -3;
    }
}

class ViperTestPitch : FlightControllerBase
{
    public override string Name { get; set; } = "Viper Test Pitch";

    private double _pitch = 0.04375;// 0.04374 no 0.04379 yes
    private Queue<FrameData> _frames = new();

    public override ControlData ProcessFrame(FrameData frame)
    {
        _frames.Enqueue(frame);
        while (_frames.Peek().SimTime < frame.SimTime - 5.0)
            _frames.Dequeue();
        Status = $"{_frames.Average(f => f.GyroPitch):0.000000}";

        //if (!Enabled) return null;
        //var ctrl = new ControlData();
        //_pitch = Math.Abs(_pitch) + 0.000001 * frame.dT;
        //if (((int)frame.SimTime / 5) % 2 == 0)
        //    _pitch = -_pitch;
        //ctrl.PitchAxis = ((int)frame.SimTime / 3) % 2 == 0 ? _pitch : -_pitch;

        var ctrl = new ControlData();
        ctrl.PitchAxis = Enabled ? 0.043751 : 0; // 0.043751 yes  0.043750 no
        // todo: this method won't be called if !Enabled anymore
        return ctrl;
    }
}

#if false
Viper speed tune tests without ParamPid

at 300:
deploy: 297.20 - 300.38
retract: 299.58 to 302.65

MISSING: pitch at 300

at 600:
full deploy: exceeds control limit
0.35: 599.04 to 600.15
retract: 599.92 to 602.42

at 600:
pull up to 5 deg: 598.80 to 600
down to -5 deg: 600 to 601.71

at 600 10k and 10 deg descent: what's the steady state?
601.0 to 600.5 due to changing conditions - never 600.00.
#endif
