using System.Windows.Input;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

class HornetSlowFlightController : FlightControllerBase
{
    public override string Name { get; set; } = "Hornet Slow Flight";
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

    public override void Reset()
    {
        _pitch.Reset(0);
        _throttle.Reset(1);
    }

    public override ControlData ProcessFrame(FrameData frame)
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
            ctl.ThrottleAxis = _speed2axisPID.Update(wantedSpeed.KtsToMs() - frame.SpeedCalibrated, frame.dT);
        }
        else
        {
            ctl.ThrottleAxis = 1.65 + _vspeed2axisSlowPID.Update(wantedVS - frame.SpeedVertical, frame.dT);
            ctl.PitchAxis = _pitch.MoveTo(1, frame.SimTime);//_pitch.MoveTo(_pitch2axisSlowPID.Update(50.ToRad() - frame.Pitch, frame.dT), frame.SimTime);
            ctl.RollAxis = _bank2axisSlowPID.Update(wantedBank - frame.Bank, frame.dT);
        }

        Status = $"vspd={_vspeed2pitchPID.Integrating}; speed={_speed2axisPID.Integrating};\npitch={_pitch2axisPID.Integrating}; bank={_bank2axisPID.Integrating}; wanted={wantedSpeed:0.0}";
        return ctl;
    }
}
