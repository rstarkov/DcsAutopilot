using DcsAutopilot;

namespace ClimbPerf;

class TunePidController : IFlightController
{
    public BasicPid PidSpeedIndicated, PidSpeedMach;
    public BasicPid PidBank;
    public BasicPid PidPitch, PidVelPitch;
    public ISmoothMover SmoothThrottle, SmoothRoll, SmoothPitch;

    public string Status => "";
    public double TgtSpeed, TgtRoll, TgtPitch;
    public double ErrSpeed, ErrRoll, ErrPitch;
    public double ErrRateSpeed, ErrRateRoll, ErrRatePitch;
    public Action<FrameData> Tick;

    public void NewSession(BulkData bulk)
    {
    }

    public void ProcessBulkUpdate(BulkData bulk)
    {
    }

    public ControlData ProcessFrame(FrameData frame)
    {
        Tick?.Invoke(frame);
        var ctl = new ControlData();

        double update(BasicPid pid, double error, ref double refError, ref double refErrorRate)
        {
            refErrorRate = (error - refError) / frame.dT;
            refError = error;
            return pid.Update(error, frame.dT);
        }

        if (PidSpeedIndicated != null)
            ctl.ThrottleAxis = update(PidSpeedIndicated, TgtSpeed - frame.SpeedIndicated, ref ErrSpeed, ref ErrRateSpeed);
        else if (PidSpeedMach != null)
            ctl.ThrottleAxis = update(PidSpeedMach, TgtSpeed - frame.SpeedMach, ref ErrSpeed, ref ErrRateSpeed);
        else
            throw new InvalidOperationException("No PID for throttle");

        if (PidBank != null)
            ctl.RollAxis = update(PidBank, TgtRoll - frame.Bank, ref ErrRoll, ref ErrRateRoll);
        else
            throw new InvalidOperationException("No PID for bank");

        if (PidPitch != null)
            ctl.PitchAxis = update(PidPitch, TgtPitch - frame.Pitch, ref ErrPitch, ref ErrRatePitch);
        else if (PidVelPitch != null)
            ctl.PitchAxis = update(PidVelPitch, TgtPitch - frame.VelPitch, ref ErrPitch, ref ErrRatePitch);
        else
            throw new InvalidOperationException("No PID for pitch");

        if (SmoothThrottle != null)
            ctl.ThrottleAxis = SmoothThrottle.MoveTo(ctl.ThrottleAxis.Value, frame.SimTime);
        if (SmoothRoll != null)
            ctl.RollAxis = SmoothRoll.MoveTo(ctl.RollAxis.Value, frame.SimTime);
        if (SmoothPitch != null)
            ctl.PitchAxis = SmoothPitch.MoveTo(ctl.PitchAxis.Value, frame.SimTime);

        return ctl;
    }
}
