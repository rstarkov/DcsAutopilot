using DcsAutopilot;

namespace DcsExperiments;

class AxisResponseMap : MultiTester
{
    public void Run(string[] args)
    {
        MapYawAxis();
    }

    private void MapRollAxis()
    {
        var rates = new List<double>();
        InitialPitchError = 0.5;
        InitialPitchErrorRate = 0.1;
        InitialRollError = 0.5;
        InitialRollErrorRate = 0.01;
        InitialAltitudeError = 500;
        InitialAltitude = 10_000;
        InitialSpeed = 300;
        foreach (var input in Util.Range(0.001, 0.001, 0.020).Concat(Util.Range(0.03, 0.01, 0.10)).Concat(Util.Range(0.2, 0.1, 1.0)))
            foreach (var sign in new[] { 1, -1 })
            {
                InitialConditions();
                var startTime = _dcs.LastFrame.SimTime;
                var done = false;
                double pitch = double.NaN;
                rates.Clear();
                _ctrl.TgtSpeed = InitialSpeed.KtsToMs();
                _ctrl.TgtPitch = 0;
                _ctrl.PostProcess = (frame, control) =>
                {
                    if (done) return;
                    if (double.IsNaN(pitch)) pitch = control.PitchAxis.Value;
                    control.RollAxis = input * sign;
                    control.PitchAxis = pitch;
                    if (frame.SimTime - startTime > 1)
                        rates.Add(frame.GyroRoll);
                    if (frame.SimTime - startTime > (input < 0.02 ? 15 : input < 0.1 ? 10 : 5))
                        done = true;
                };
                while (!done)
                    Thread.Sleep(100);
                rates.Sort();
                double median(List<double> vals) => vals[vals.Count / 2];
                var result = $"{input * sign},{rates.Average()},{median(rates)}";
                Console.WriteLine(result);
                File.AppendAllLines($"axis-roll.csv", new[] { result });
            }
    }

    private void MapYawAxis()
    {
        var rates = new List<double>();
        InitialPitchError = 0.5;
        InitialPitchErrorRate = 0.05;
        InitialRollError = 0.5;
        InitialRollErrorRate = 0.05;
        InitialAltitudeError = 500;
        InitialAltitude = 10_000;
        InitialSpeed = 300;
        foreach (var input in Util.Range(0.001, 0.001, 0.020).Concat(Util.Range(0.03, 0.01, 0.10)).Concat(Util.Range(0.2, 0.1, 1.0)))
            foreach (var sign in new[] { 1, -1 })
            {
                InitialConditions();
                var startTime = _dcs.LastFrame.SimTime;
                var done = false;
                double max = 0;
                rates.Clear();
                _ctrl.TgtSpeed = InitialSpeed.KtsToMs();
                _ctrl.TgtPitch = 0;
                _ctrl.PostProcess = (frame, control) =>
                {
                    if (done) return;
                    control.YawAxis = input * sign;
                    max = sign == 1 ? Math.Max(max, frame.GyroYaw) : Math.Min(max, frame.GyroYaw); // yaw "settles" and the gyro just reports our rate of heading change, so we really want the max rate
                    if (frame.SimTime - startTime > 1)
                        rates.Add(frame.GyroYaw);
                    if (frame.SimTime - startTime > 5)
                        done = true;
                };
                while (!done)
                    Thread.Sleep(100);
                rates.Sort();
                double median(List<double> vals) => vals[vals.Count / 2];
                var result = $"{input * sign},{max},{rates.Average()},{median(rates)}";
                Console.WriteLine(result);
                File.AppendAllLines($"axis-yaw.csv", new[] { result });
            }
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
