namespace DcsAutopilot;

class ViperTune : FlightControllerBase
{
    public override string Name { get; set; } = "Viper Tune";
    public override string Status => $"d/dt={_speed2axisPID.Derivative:0.000}: {_Dmin:0.000} to {_Dmax:0.000}    Σ={_speed2axisPID.ErrorIntegral:0.000}, Bias={_speed2axisPID.ErrorIntegral * _speed2axisPID.I:0.000}";

    public double _Dmin, _Dmax;
    private BasicPid _speed2axisPID = new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.5, Bias = 0.213 }.SetZiNiClassic(0.3, 5.9); // at 300kts, 300ft. Dlim: from above 1.2 steady 0.839 reversal; from below 3.3 steady 1.006 reversal
    //private BasicPid _speed2axisPID = new BasicPid { MinControl = 0.0, MaxControl = 1.0, IntegrationLimit = 0.4, Bias = 0.655 }.SetZiNiClassic(0.3, 4.7); // at 600kts, 300ft; Dlim: 0.75 from above, 0.05 from below

    public override ControlData ProcessFrame(FrameData frame)
    {
        if (!Enabled)
            return null;
        var ctl = new ControlData();
        ctl.ThrottleAxis = _speed2axisPID.Update(300.KtsToMs() - frame.SpeedIndicated, frame.dT);
        _Dmin = Math.Min(_Dmin, _speed2axisPID.Derivative);
        _Dmax = Math.Max(_Dmax, _speed2axisPID.Derivative);
        return ctl;
    }
}

