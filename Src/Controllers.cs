using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

class BasicAltitudeController : IFlightController
{
    private BasicPid _vspeed2pitchPID = new() { P = 1, MinControl = -20.ToRad(), MaxControl = 20.ToRad() };
    private BasicPid _pitch2axisPID = new() { P = 4.2, I=3.05, D=1.44, MinControl = -0.3, MaxControl = 0.3 }; // oscillates at P=7 T=2.74s
    private SmoothMover _pitch = new(1, -1, 1);

    public double TargetAltitudeFt { get; set; } = 2000;

    public void NewSession(BulkData bulk)
    {
        _pitch.Reset(0);
    }

    public void ProcessBulkUpdate(BulkData bulk)
    {
    }

    public ControlData ProcessFrame(FrameData frame)
    {
        var ctl = new ControlData();

        var wantedPitch = _vspeed2pitchPID.Update(-frame.SpeedVertical, frame.dT);
        wantedPitch = 15.ToRad();
        var wantedPitchAxis = _pitch2axisPID.Update(wantedPitch - frame.Pitch, frame.dT);
        ctl.PitchAxis = _pitch.MoveTo(wantedPitchAxis, frame.SimTime);

        return ctl;
    }
}

class BasicPid
{
    public double P { get; set; }
    public double I { get; set; }
    public double D { get; set; }

    public double MinControl { get; set; }
    public double MaxControl { get; set; }
    public double ErrorIntegral { get; set; }
    public double Derivative { get; set; }
    public double DerivativeSmoothing { get; set; } = 0.8;
    private double _prevError;

    public double Update(double error, double dt)
    {
        Derivative = DerivativeSmoothing * Derivative + (1 - DerivativeSmoothing) * (error - _prevError) / dt;
        _prevError = error;

        double output = P * error + I * ErrorIntegral + D * Derivative;

        output = output.Clip(MinControl, MaxControl);
        if (output > MinControl && output < MaxControl)
            ErrorIntegral += error * dt;
        return output;
    }
}
