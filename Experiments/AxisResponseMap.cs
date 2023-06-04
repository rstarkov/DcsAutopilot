using DcsAutopilot;

namespace DcsExperiments;

class AxisResponseMap : MultiTester
{
    public void Run(string[] args)
    {
        MapPitchAxis();
    }

    private void MapPitchAxis()
    {
        var ratesPitch = new List<double>();
        var ratesVelPitch = new List<double>();
        InitialPitchError = 0.5;
        InitialPitchErrorRate = 0.01;
        InitialAltitudeError = 500;
        foreach (var alt in new[] { 10_000.0, 15_000.0, 20_000.0, 5_000.0 })
            foreach (var speed in new[] { 300.0, 500.0, 400.0, 200.0 })
                foreach (var input in new[] { 0.01, -0.01, 0.02, -0.02, 0.03, -0.03, 0.04, -0.04, 0.05, -0.05, 0.1, -0.1, 0.2, -0.2, 0.3, -0.3, 0.4, -0.4, 0.5, -0.5, 0.6, -0.6, 0.7, -0.7, 0.8, -0.8, 0.9, -0.9, 1.0, -1.0 })
                {
                    //for (var input = -0.02; input <= 0.02; input += 0.001)
                    //for (var input = 0.0115; input <= 0.0125; input += 0.0001)
                    InitialAltitude = alt;
                    InitialSpeed = speed;
                    InitialMinFuel = 0.9;
                    InitialConditions();
                    var startTime = _dcs.LastFrame.SimTime;
                    var done = false;
                    var filtVP = Filters.BesselD20;
                    var filtDT = Filters.BesselD20;
                    var vpPrev = double.NaN;
                    ratesPitch.Clear();
                    ratesVelPitch.Clear();
                    double firstPitchRate = double.NaN, firstVelPitchRate = double.NaN;
                    _ctrl.TgtSpeed = speed.KtsToMs();
                    _ctrl.PostProcess = (frame, control) =>
                    {
                        if (done) return;
                        control.PitchAxis = input;
                        var dt = filtDT.Step(frame.dT);
                        var vp = filtVP.Step(frame.VelPitch);
                        if (double.IsNaN(firstPitchRate)) firstPitchRate = frame.GyroPitch;
                        if (double.IsNaN(firstVelPitchRate) && !double.IsNaN(vpPrev)) firstVelPitchRate = (vp - vpPrev) / dt;
                        if (frame.SimTime - startTime > 1)
                        {
                            ratesPitch.Add(frame.GyroPitch);
                            ratesVelPitch.Add((vp - vpPrev) / dt);
                        }
                        if (frame.SimTime - startTime > 10 || Math.Abs(frame.Pitch) > 45)
                            done = true;
                        vpPrev = vp;
                    };
                    while (!done)
                        Thread.Sleep(100);
                    ratesPitch.Sort();
                    ratesVelPitch.Sort();
                    double median(List<double> vals) => vals[vals.Count / 2];
                    double minmax(List<double> vals) => input > 0 ? vals.Max() : vals.Min();
                    var result = $"{alt},{speed},{input},{firstPitchRate},{firstVelPitchRate},{_dcs.LastFrame.FuelInternal},{ratesPitch.Average()},{median(ratesPitch)},{minmax(ratesPitch)},{ratesVelPitch.Average()},{median(ratesVelPitch)},{minmax(ratesVelPitch)}";
                    Console.WriteLine(result);
                    File.AppendAllLines($"axis-pitch.csv", new[] { result });
                }
    }
}
