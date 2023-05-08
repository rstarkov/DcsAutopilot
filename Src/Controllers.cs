using System;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

class SlowFlightController : IFlightController
{
    private BasicPid _vspeed2pitchPID = new() { P = 0.004, I = 0.003, D = 0.00127 * 0.05, MinControl = -90.ToRad(), MaxControl = 90.ToRad(), IntegrationLimit = 1 /*m/s / sec*/ }; // oscillates at P=0.005 T=3.4s
    private BasicPid _pitch2axisPID = new() { P = 2, I = 1, D = 0.3, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ }; // oscillates at P=7 T=2.74s
    private BasicPid _speed2axisPID = new() { P = 0.5, I = 0.7, D = 0.05, MinControl = 0, MaxControl = 1.5, IntegrationLimit = 1 /*m/s / sec*/ };
    //private BasicPid _bank2axisPID = new() { P = 2.4, I = 2.8, D = 0.51, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ }; // oscillates at P=4 T=1.7
    private BasicPid _bank2axisPID = new() { P = 5, I = 0, D = 0, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ }; // oscillates at P=4 T=1.7
    private SmoothMover _pitch = new(1, -1, 1);
    private double wantedSpeed = 200;

    public double TargetAltitudeFt { get; set; } = 2000;

    public string Status => $"vspd={_vspeed2pitchPID.Integrating}; speed={_speed2axisPID.Integrating};\npitch={_pitch2axisPID.Integrating}; bank={_bank2axisPID.Integrating}; wanted={wantedSpeed:0.0}";

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
        var wantedPitchAxis = _pitch2axisPID.Update(wantedPitch - frame.Pitch, frame.dT);
        ctl.PitchAxis = _pitch.MoveTo(wantedPitchAxis, frame.SimTime);
        ctl.RollAxis = _bank2axisPID.Update(0 - frame.Bank, frame.dT);

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
        ctl.ThrottleAxis = _speed2axisPID.Update(wantedSpeed.KtsToMs() - frame.SpeedIndicated, frame.dT);

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
}
