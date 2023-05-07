namespace DcsAutopilot;

class BasicAltitudeController : IFlightController
{
    public void NewSession(BulkData bulk)
    {
    }

    public void ProcessBulkUpdate(BulkData bulk)
    {
    }

    public ControlData ProcessFrame(FrameData frame)
    {
        var ctl = new ControlData();
        ctl.PitchAxis = 0.25;
        return ctl;
    }
}
