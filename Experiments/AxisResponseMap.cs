using DcsAutopilot;
using RT.Serialization;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace DcsExperiments;

class AxisResponseMap : MultiTester
{
    public void Run(string[] args)
    {
        //Map("hornet");
        Process("hornet");
    }

    private void Map(string name)
    {
        InitialPitchError = 0.5;
        InitialPitchErrorRate = 0.05;
        InitialRollError = 0.5;
        InitialRollErrorRate = 0.05;
        InitialYawError = 0.1;
        InitialYawErrorRate = 0.05;
        InitialAltitudeError = 500;
        InitialAltitude = 10_000;
        InitialSpeed = 300;
        while (true)
        {
            MapYawAxis($"axis-{name}-yaw.csv");
            MapRollAxis($"axis-{name}-roll.csv");
            MapPitchAxis($"axis-{name}-pitch.csv");
        }
    }

    private void Process(string name)
    {
        CollateResults($"axis-{name}-yaw.csv", symmetric: true);
        CollateResults($"axis-{name}-roll.csv", symmetric: true);
        CollateResults($"axis-{name}-pitch.csv", symmetric: false);
        var p = new Plane
        {
            MapYaw = ResultsToAxis($"axis-{name}-yaw.collate.csv", 1, -0.01m, 0.01m, symmetric: true, 0.001), // must manually determine deadzone
            MapRoll = ResultsToAxis($"axis-{name}-roll.collate.csv", 1, -0.0083333m, 0.0083333m, symmetric: true, 0.001), // must manually determine deadzone
            MapPitch = ResultsToAxis($"axis-{name}-pitch.collate.csv", 4 /*gyro avg*/, -0.012m, 0.012m, symmetric: false, 0.001, -0.5m), // must manually determine deadzone and cutoffs; must clean up 4th column
        };
        ClassifyXml.SerializeToFile(p, "hornet.xml");
        File.WriteAllLines($"axis-{name}-yaw.result.csv", p.MapYaw.Map.Select(p => $"{p.RawInput},{p.NormalisedInput}"));
        File.WriteAllLines($"axis-{name}-roll.result.csv", p.MapRoll.Map.Select(p => $"{p.RawInput},{p.NormalisedInput}"));
        File.WriteAllLines($"axis-{name}-pitch.result.csv", p.MapPitch.Map.Select(p => $"{p.RawInput},{p.NormalisedInput}"));
    }

    private void CollateResults(string filename, bool symmetric)
    {
        var output = PathUtil.AppendBeforeExtension(filename, ".collate");
        if (File.Exists(output))
            return;
        var csv = Ut.ParseCsvFile(filename).ToList();
        var data = csv.Skip(1).Select(r => (raw: decimal.Parse(r[0]), rates: r.Skip(1).Select(rr => double.Parse(rr)).ToArray()));
        if (symmetric)
            data = data.Select(x => (raw: Math.Abs(x.raw), rates: x.rates.Select(rr => Math.Abs(rr)).ToArray()));
        IEnumerable<double> summariseColumns(IEnumerable<double[]> inputs, Func<IEnumerable<double>, double> func)
        {
            var count = inputs.Max(vs => vs.Length);
            for (int i = 0; i < count; i++)
                yield return func(inputs.Where(vs => i < vs.Length).Select(vs => vs[i]));
        }
        var points = data.GroupBy(v => v.raw, v => v.rates)
            .Select(g => (raw: g.Key, rates: summariseColumns(g, g => g.Median(0.2)).ToArray()))
            .OrderBy(x => x.raw);
        var maxs = summariseColumns(points.Select(x => x.rates), x => x.Max());
        var result = points.Select(x => $"{x.raw},{x.rates.Zip(maxs, (r, m) => r / m).JoinString(",")}");
        File.WriteAllLines(output, Ut.FormatCsvRow(csv[0]).Concat(result));
    }

    private PlaneControlAxis ResultsToAxis(string filename, int rateCol, decimal deadzoneNeg, decimal deadzonePos, bool symmetric, double simplifyTolerance, decimal min = -1.0m, decimal max = 1.0m)
    {
        var pts = Ut.ParseCsvFile(filename).Skip(1).ToDictionary(r => decimal.Parse(r[0]), r => double.Parse(r[rateCol]));
        if (symmetric)
            foreach (var kvp in pts.ToList())
                pts[-kvp.Key] = kvp.Value == 0 ? 0 : -kvp.Value; // avoid -0 for aesthetic reasons
        pts[deadzoneNeg] = 0;
        pts[deadzonePos] = 0;
        pts[0] = 0;
        var points = pts.Where(x => (x.Key <= deadzoneNeg || x.Key >= deadzonePos) && x.Key >= min && x.Key <= max)
            .Select(x => new AxisMapPoint { RawInput = (double)x.Key, NormalisedInput = x.Value })
            .OrderBy(p => p.NormalisedInput).ThenBy(p => p.RawInput)
            .ToList();
        SimplifyMap(points, simplifyTolerance);
        return new PlaneControlAxis(points);
    }

    private void SimplifyMap(List<AxisMapPoint> points, double toleranceRaw)
    {
        var originalPoints = points.ToList();
        var original = new PlaneControlAxis(originalPoints);
        while (true)
        {
            bool anyRemoved = false;
            for (int i = 1; i < points.Count - 1; i++)
            {
                var test = new PlaneControlAxis(points.Except(new[] { points[i] }));
                foreach (var pt in originalPoints)
                    if (Math.Abs(test.NormToRaw(pt.NormalisedInput) - original.NormToRaw(pt.NormalisedInput)) > toleranceRaw)
                        goto errorTooBig;
                foreach (var normMidpoint in originalPoints.ConsecutivePairs(false).Select(p => (p.Item1.NormalisedInput + p.Item2.NormalisedInput) / 2))
                    if (Math.Abs(test.NormToRaw(normMidpoint) - original.NormToRaw(normMidpoint)) > toleranceRaw)
                        goto errorTooBig;
                points.RemoveAt(i); // and keep going - this way a sequence of redundant points gets thinned incrementally
                anyRemoved = true;
                errorTooBig:;
            }
            if (!anyRemoved)
                return;
        }
    }

    private IEnumerable<decimal> EqualCounts(string filename, IEnumerable<decimal> inputs)
    {
        var have = Ut.ParseCsvFile(filename).Skip(1).Select(r => decimal.Parse(r[0])).GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
        var minmax = inputs.Select(v => have.GetValueOrDefault(v)).MinMaxSumCount();
        Console.WriteLine($"min={minmax.Min}, max={minmax.Max}");
        if (minmax.Min == minmax.Max)
            return inputs.Distinct();
        else
            return inputs.Where(v => have.GetValueOrDefault(v) < minmax.Max).Distinct().ToList();
    }

    private static IEnumerable<decimal> R(decimal start, decimal endInclusive, decimal step)
    {
        for (var v = start; v <= endInclusive; v += step)
            yield return v;
    }

    private static IEnumerable<T> C<T>(params IEnumerable<T>[] enumerables)
    {
        IEnumerable<T> result = null;
        foreach (var e in enumerables)
            result = result == null ? e : result.Concat(e);
        return result;
    }

    private void MapRollAxis(string filename)
    {
        InitialRollErrorRate = 0.01;
        var rates = new List<double>();
        if (!File.Exists(filename))
            File.AppendAllLines(filename, new[] { "input,average,median" });
        var inputs = C(R(0.001m, 0.020m, step: 0.001m), R(0.03m, 0.10m, step: 0.01m), R(0.1m, 1.0m, step: 0.05m), R(0.0082m, 0.0086m, step: 0.0001m)).SelectMany(v => new[] { v, -v });
        foreach (var input in EqualCounts(filename, inputs))
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
                control.RollAxis = (double)input;
                control.PitchAxis = pitch;
                if (frame.SimTime - startTime > 1)
                    rates.Add(frame.GyroRoll);
                if (frame.SimTime - startTime > (Math.Abs(input) <= 0.02m ? 15 : Math.Abs(input) <= 0.1m ? 10 : 5))
                    done = true;
            };
            while (!done)
                Thread.Sleep(100);
            rates.Sort();
            var result = $"{input},{rates.Average()},{rates.Median()}";
            Console.WriteLine(result);
            File.AppendAllLines(filename, new[] { result });
        }
    }

    private void MapYawAxis(string filename)
    {
        InitialYawErrorRate = 0.01;
        var rates = new List<double>();
        if (!File.Exists(filename))
            File.AppendAllLines(filename, new[] { "input,max,average,median" });
        var inputs = C(R(0.001m, 0.020m, step: 0.001m), R(0.03m, 0.10m, step: 0.01m), R(0.1m, 1.0m, step: 0.1m)).SelectMany(v => new[] { v, -v });
        foreach (var input in EqualCounts(filename, inputs))
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
                control.YawAxis = (double)input;
                max = input > 0 ? Math.Max(max, frame.GyroYaw) : Math.Min(max, frame.GyroYaw); // yaw "settles" and the gyro just reports our rate of heading change, so we really want the max rate
                if (frame.SimTime - startTime > 1)
                    rates.Add(frame.GyroYaw);
                if (frame.SimTime - startTime > 5)
                    done = true;
            };
            while (!done)
                Thread.Sleep(100);
            rates.Sort();
            var result = $"{input},{max},{rates.Average()},{rates.Median()}";
            Console.WriteLine(result);
            File.AppendAllLines(filename, new[] { result });
        }
    }

    private void MapPitchAxis(string filename)
    {
        InitialPitchErrorRate = 0.01;
        InitialRollError = 0.1;
        var ratesPitch = new List<double>();
        var ratesVelPitch = new List<double>();
        if (!File.Exists(filename))
            File.AppendAllLines(filename, new[] { "input,initial,vel-initial,fuel,average,median,max,vel-average,vel-median,vel-max" });
        var inputs = C(R(0.005m, 0.020m, step: 0.001m), R(0.03m, 0.10m, step: 0.01m), R(0.2m, 1.0m, step: 0.1m)).SelectMany(v => new[] { v, -v });
        inputs = C(inputs, R(-0.55m, -0.45m, step: 0.02m), R(0.90m, 1.00m, step: 0.02m));
        foreach (var input in EqualCounts(filename, inputs.Order()))
        {
            InitialConditions();
            var startTime = _dcs.LastFrame.SimTime;
            var done = false;
            var filtVP = Filters.BesselD20;
            var filtDT = Filters.BesselD20;
            var vpPrev = double.NaN;
            ratesPitch.Clear();
            ratesVelPitch.Clear();
            double firstPitchRate = double.NaN, firstVelPitchRate = double.NaN;
            _ctrl.TgtSpeed = InitialSpeed.KtsToMs();
            _ctrl.PostProcess = (frame, control) =>
            {
                if (done) return;
                control.PitchAxis = (double)input;
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
            double minmax(List<double> vals) => input > 0 ? vals.Max() : vals.Min();
            var result = $"{input},{firstPitchRate},{firstVelPitchRate},{_dcs.LastFrame.FuelInternal},{ratesPitch.Average()},{ratesPitch.Median()},{minmax(ratesPitch)},{ratesVelPitch.Average()},{ratesVelPitch.Median()},{minmax(ratesVelPitch)}";
            Console.WriteLine(result);
            File.AppendAllLines(filename, new[] { result });
        }
    }
}
