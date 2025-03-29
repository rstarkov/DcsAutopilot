using System.Collections.Concurrent;
using DcsAutopilot;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Geometry;
using Seekers;

namespace DcsExperiments;

public class CharacteriseAirspeedDial
{
    public static void CaptureData()
    {
        // Procedure: set mission to ISA (15 deg, 29.92); start on ground as close to sea level as possible; clean config.
        // Take off and stay below 100ft ASL. Accelerate slowly to max speed, then decelerate slowly to min speed, maintaining consistent acceleration.
        // Repeat with max power / max speedbrake. Land.

        // To enable data logging, add to Aircraft:
        // frame.Test1 = pkt.Entries["spd_dial_ias"][0].ParseDouble();
        // frame.Test2 = pkt.Entries["spd_dial_mach"][0].ParseDouble();

        var dcs = new DcsController();
        var ctrl = new CharacteriseLogger();
        dcs.FlightControllers.Add(ctrl);
        ctrl.LogFrame = frame => $"{frame.SimTime},{frame.AltitudeAsl.MetersToFeet()},{frame.AngleOfAttack},{frame.SpeedVertical},{frame.SpeedTrue.MsToKts()},{frame.SpeedIndicatedBad.MsToKts()},{frame.SpeedMachBad},{frame.Test1},{frame.Test2}";
        ctrl.LogConsole = frame => $"Accel: {(frame.SpeedTrue - dcs.PrevFrame.SpeedTrue).MsToKts() / frame.dT:0.000}   TrueCAS?: {frame.SpeedIndicatedBad.MsToKts():0.000}";
        ctrl.Enabled = true;
        dcs.Start();
        while (dcs.LastFrame == null)
            Thread.Sleep(100);
        Console.WriteLine("STARTED");
        while (true)
            Thread.Sleep(100);
    }

    public static void FitData(string input, string output)
    {
        var data = Ut.ParseCsvFile(input).Select(r => new PtRaw
        {
            Time = r[0].ParseDouble(),
            AltFt = r[1].ParseDouble(),
            AoA = r[2].ParseDouble(),
            VSpd = r[3].ParseDouble(),
            TrueCAS = r[5].ParseDouble(),
            RawDialIAS = r[7].ParseDouble() * 1000,
        }).ToList();
        foreach (var pt in data)
            pt.Include = pt.AltFt > 1 && pt.AltFt < 95 && pt.RawDialIAS >= 1.5
                && !(pt.Time >= 167.297 && pt.Time <= 172.378) && !(pt.Time >= 311.882 && pt.Time <= 318.509);
        var points = data.Select((rpt, i) => new Pt { DialIAS = rpt.RawDialIAS, Index = i }).ToList();

        // global shift and dump for visualisation
        if (false)
        {
            //var opt = new RomanOptim();
            //opt.Evaluate = (double[] vec) =>
            //{
            //    SetShift(data, vec[0]);
            //    return -data.Where(filters).Sum(pt => pt.Error * pt.Error);
            //};
            //opt.GenerateRandomVector = _ => [Random.Shared.NextDouble(0.1, 0.6)];
            //var bestVector = new double[] { 0.32 };
            //var bestEval = opt.Evaluate(bestVector);
            //opt.OptimizeOnce(ref bestEval, ref bestVector);
            //SetShift(data, bestVector[0]);
        }
        else
            SetShift(data, points, 0.31470); // from last run
        File.WriteAllLines(output, points.Where(pt => pt.Include).Select(pt => $"{pt.DialIAS},{pt.Error}, {0}"));

        var curve = new Curve();
        curve.Segments = [
            new SineSegment(0.5, 35),
            new LineSegment(35, 42),
            new LineSegment(42, 95),
            new LineSegment(95, 152),
            new LineSegment(152, 182),
            new LineSegment(182, 199),
            new LineSegment(199, 255),
            new LineSegment(255, 303),
            new LineSegment(303, 355),
            new LineSegment(355, 402),
            new LineSegment(402, 455),
            new LineSegment(455, 500),
            new LineSegment(500, 552),
            new LineSegment(554, 574),
            new LineSegment(575, 594),
            new LineSegment(596, 613),
            new LineSegment(613, 621),
            new LineSegment(623, 633),
            new LineSegment(633, 642),
            new LineSegment(642, 649),
            new LineSegment(653, 669),
            new LineSegment(672, 697),
            new LineSegment(699, 707),
            new LineSegment(709, 715),
            new LineSegment(717, 723),
            new LineSegment(726, 750),
            new LineSegment(750, 797),
            new LineSegment(797, 820),
        ];
        foreach (var seg in curve.Segments.OfType<LineSegment>())
            fitLine(seg, data, points.Where(pt => pt.DialIAS >= seg.FromX && pt.DialIAS < seg.ToX).ToList());
        foreach (var seg in curve.Segments.OfType<SineSegment>())
            fitSine(seg, data, points.Where(pt => pt.DialIAS >= seg.FromX && pt.DialIAS < seg.ToX).ToList());
        // Make all lines join where they intersect
        foreach (var (seg1, seg2) in curve.Segments.ConsecutivePairs(false).Where(p => p.Item1 is LineSegment && p.Item2 is LineSegment).Select(p => (p.Item1 as LineSegment, p.Item2 as LineSegment)))
        {
            var intersect = Intersect.LineWithLine(seg1.Edge, seg2.Edge).point;
            seg1.ToX = intersect.Value.X;
            seg2.FromX = intersect.Value.X;
        }
        saveResult(curve, points, data, output);
        curve = new Curve();
        curve.Add(new SineSegment(0.5, 34.8, -35.6, 12.37, -0.06033, -35.6));
        curve.AddPolyline((34.8, -35.61), (42, -41.46), (95.2, -7.87), (152, -0.55), (182, 9.65), (199, -3.28), (255, 2.71), (303, 0.77), (355, 2.86), (402, 0.09), (455, 3.39), (504.8, 3.34), (553.8, 3.35), (573.3, 3.27), (592.7, 3.11), (612.7, 2.74), (622, 2.52), (632.5, 2.08), (641.8, 1.52), (651, 0.14), (670.6, -9.91), (695.5, 0.69), (707.4, 6.27), (716.4, 4.97), (725.8, 4.3), (750, 3.13), (796, -4.93), (820, -6.52));
        saveResult(curve, points, data, output);
        fitCurve(curve, points, data);
        saveResult(curve, points, data, output);
    }

    private static void tweakShifts(Curve curve, List<Pt> points, List<PtRaw> data)
    {
        foreach (var seg in curve.Segments)
        {
            var pts = points.Where(pt => pt.DialIAS >= seg.FromX && pt.DialIAS < seg.ToX).ToList();
            var opt = new RomanOptim();
            opt.Evaluate = (double[] vec) => { SetShift(data, pts, vec[0]); return -segmentEval(seg, pts); };
            opt.GenerateRandomVector = _ => [Random.Shared.NextDouble(0.29, 0.35)];
            var bestVector = new double[] { seg.Misc1 };
            var bestEval = opt.Evaluate(bestVector);
            opt.OptimizeOnce(ref bestEval, ref bestVector);
            seg.Misc1 = bestVector[0];
            SetShift(data, pts, seg.Misc1);
        }
    }

    private static void setShifts(Curve curve, List<Pt> points, List<PtRaw> data)
    {
        foreach (var seg in curve.Segments)
            SetShift(data, points.Where(pt => pt.DialIAS >= seg.FromX && pt.DialIAS < seg.ToX), seg.Misc1);
    }

    private static void saveResult(Curve curve, List<Pt> points, List<PtRaw> data, string output)
    {
        tweakShifts(curve, points, data);
        Console.WriteLine($"Curve eval: {curveEval(curve, points)}");
        foreach (var seg in curve.Segments)
        {
            var pts = points.Where(pt => pt.DialIAS >= seg.FromX && pt.DialIAS < seg.ToX);
            foreach (var pt in pts)
                pt.ErrorFit = seg.Calc(pt.DialIAS);
        }
        foreach (var grp in curve.Segments.GroupConsecutiveBy(s => s.GetType()))
            if (grp.Key == typeof(SineSegment))
                foreach (var seg in grp)
                    Console.WriteLine(seg.ToCsharp());
            else
                Console.WriteLine(grp.Cast<LineSegment>().Select((seg, i) => (i == 0 ? $"({seg.FromX:0.###},{seg.FromY:0.###})," : "") + $"({seg.ToX:0.###},{seg.ToY:0.###})").JoinString(", "));
        File.WriteAllLines(output, points.Select(pt => pt.Include ? $"{pt.DialIAS},{pt.Error},{pt.ErrorFit},,{pt.Raw1?.Time}" : $"{pt.DialIAS},,,{pt.Error},{pt.Raw1?.Time}"));
    }

    private static void fitLine(LineSegment seg, List<PtRaw> data, List<Pt> pts)
    {
        var opt = new RomanOptim();
        opt.Evaluate = (double[] vec) =>
        {
            SetShift(data, pts, vec[0]);
            return -Fit.OrthoLinReg(pts).PerpRMS;
        };
        opt.GenerateRandomVector = _ => [Random.Shared.NextDouble(0.1, 0.6)];
        var bestVector = new double[] { 0.32 };
        var bestEval = opt.Evaluate(bestVector);
        opt.OptimizeOnce(ref bestEval, ref bestVector);
        seg.Misc1 = bestVector[0];
        SetShift(data, pts, bestVector[0]);
        var finalEval = Fit.OrthoLinReg(pts);
        seg.Slope = finalEval.Slope;
        seg.Offset = finalEval.Intercept;
        Console.WriteLine($"Line {seg.FromX}-{seg.ToX}: MSE={-finalEval.PerpRMS:0.00000}, shift={seg.Misc1:0.00000}, grad={seg.Slope:0.00000}, offs={seg.Offset:0.00000}");
    }

    private static void fitSine(SineSegment seg, List<PtRaw> data, List<Pt> pts)
    {
        var opt = new RomanOptim();
        opt.Evaluate = (double[] vec) =>
        {
            SetShift(data, pts, vec[0].Clip(0.1, 0.6));
            var total = 0.0;
            var n = 0;
            foreach (var pt in pts)
                if (pt.Include)
                {
                    pt.ErrorFit = vec[1] * Math.Sin(vec[2] * pt.DialIAS + vec[3]) + vec[3];
                    total += (pt.Error - pt.ErrorFit) * (pt.Error - pt.ErrorFit);
                    n++;
                }
            return -Math.Sqrt(total / n);
        };
        var bestVector = new double[] { 0.31503, 12.3759, -0.0603, -35.6010 };
        opt.GenerateRandomVector = _ => [Random.Shared.NextDouble(0.25, 0.4), Random.Shared.NextDouble(-100, 100), Random.Shared.NextDouble(-100, 100), Random.Shared.NextDouble(-100, 100)];
        var bestEval = opt.Evaluate(bestVector);
        opt.OptimizeOnce(ref bestEval, ref bestVector);
        Console.WriteLine($"Sine {seg.FromX}-{seg.ToX}: MSE={-bestEval:0.00000}, shift={bestVector[0]:0.00000}, a={bestVector[1]:0.0000}, b={bestVector[2]:0.00000}, c={bestVector[3]:0.0000}");
        seg.Misc1 = bestVector[0];
        seg.Ampl = bestVector[1];
        seg.Freq = bestVector[2];
        seg.Phase = seg.Offset = bestVector[3]; // coincidence?
    }

    private static void SetShift(List<PtRaw> data, IEnumerable<Pt> pts, double shift)
    {
        foreach (var pt in pts)
        {
            var targetTime = data[pt.Index].Time - shift;
            pt.TrueCAS = -1; // in case we reach end of data
            pt.Raw1 = pt.Raw2 = null;
            pt.Include = false;
            for (int k = pt.Index; k > 0; k--)
            {
                var rpt1 = data[k - 1];
                var rpt2 = data[k];

                if (rpt1.Time <= targetTime && targetTime <= rpt2.Time)
                {
                    pt.TrueCAS = Util.Linterp(rpt1.Time, rpt2.Time, rpt1.TrueCAS, rpt2.TrueCAS, targetTime);
                    pt.Raw1 = rpt1;
                    pt.Raw2 = rpt2;
                    pt.Include = rpt1.Include && rpt2.Include;
                    break;
                }
            }
        }
    }

    private static double segmentEval(CurveSegment seg, List<Pt> points, bool mse = true)
    {
        var err = 0.0;
        int n = 0;
        foreach (var pt in points)
            if (pt.Include)
            {
                if (mse)
                    err += (pt.Error - pt.ErrorFit) * (pt.Error - pt.ErrorFit);
                else
                    err += Math.Abs(pt.Error - pt.ErrorFit); // MAE
                n++;
            }
        if (mse)
            return Math.Sqrt(err / n);
        else
            return err / n;
    }

    private static double curveEval(Curve curve, List<Pt> points, bool mse = true)
    {
        var err = 0.0;
        int n = 0;
        foreach (var pt in points)
            if (pt.Include)
            {
                pt.ErrorFit = curve.Calc(pt.DialIAS);
                if (mse)
                    err += (pt.Error - pt.ErrorFit) * (pt.Error - pt.ErrorFit);
                else
                    err += Math.Abs(pt.Error - pt.ErrorFit); // MAE
                n++;
            }
        if (mse)
            return Math.Sqrt(err / n);
        else
            return err / n;
    }

    private static void fitCurve(Curve curve, List<Pt> points, List<PtRaw> data)
    {
        var opt = new RomanOptim();
        //double snapX(double x) => x;
        //double snapY(double x) => x;
        //double snapX(double x) => Math.Abs(x - Math.Round(x)) < 0.1 ? Math.Round(x) : x;
        double snapX(double x) => Math.Abs(x - Math.Round(x)) < 0.15 ? Math.Round(x) : Math.Round(x, 1);
        double snapY(double x) => Math.Round(x, 2);
        opt.Evaluate = (double[] vec) =>
        {
            int vp = 0;
            CurveSegment prev = null;
            foreach (var seg in curve.Segments)
            {
                if (seg is LineSegment ls)
                {
                    ls.SetPts((prev.ToX, snapY(prev.Calc(prev.ToX))), (snapX(vec[vp++]), snapY(vec[vp++])));
                }
                else if (seg is SineSegment ss)
                {
                    ss.ToX = snapX(vec[vp++]);
                    ss.Phase = ss.Offset = snapX(vec[vp++]);
                    ss.Ampl = snapY(vec[vp++]);
                    ss.Freq = vec[vp++];
                }
                prev = seg;
            }
            curve.Segments[^1].ToX = 820;
            //setShifts(curve, points, data); // called for but extremely slow
            return -curveEval(curve, points);
        };
        var bestVectorL = new List<double>();
        foreach (var seg in curve.Segments)
            if (seg is LineSegment ls)
            {
                bestVectorL.Add(ls.ToX);
                bestVectorL.Add(ls.ToY);
            }
            else if (seg is SineSegment ss)
            {
                bestVectorL.Add(ss.ToX);
                bestVectorL.Add(ss.Phase);
                bestVectorL.Add(ss.Ampl);
                bestVectorL.Add(ss.Freq);
            }
        opt.GenerateRandomVector = vec => vec.Select(v => v * Random.Shared.NextDouble(0.999, 1.001)).ToArray();
        var bestVector = bestVectorL.ToArray();
        var bestEval = opt.Evaluate(bestVector);
        Console.WriteLine($"Whole curve eval: {-bestEval}");
        //for (int z = 0; z < 20; z++)
        while (true)
        {
            // One random
            var vector = opt.GenerateRandomVector(bestVector);
            var eval = opt.Evaluate(vector);
            opt.OrthogonalTraverse(ref eval, ref vector);
            if (eval > bestEval)
            {
                bestEval = eval;
                bestVector = vector.ToArray();
            }
            // Three from best
            for (int i = 0; i < 3; i++)
                opt.OrthogonalTraverse(ref bestEval, ref bestVector);
            // Individual dimensions
            var dir = new double[bestVector.Length];
            for (int d = 0; d < bestVector.Length; d++)
            {
                Array.Fill(dir, 0);
                dir[d] = 1;
                opt.TraverseDirection(dir, ref bestEval, ref bestVector);
            }
            if (Random.Shared.NextDouble() < 0.1)
                saveResult(curve, points, data, "dummy.csv");
            Console.WriteLine($"Whole curve eval: {-bestEval}");
        }
        opt.Evaluate(bestVector); // just to update the curve back to best
    }

    class PtRaw
    {
        public double Time;
        public double AltFt;
        public double AoA;
        public double VSpd;
        public double TrueCAS;
        public double RawDialIAS;
        public bool Include;
    }

    class Pt : Fit.IPt
    {
        public double DialIAS;
        public double TrueCAS;
        public int Index; // of the raw point that contained this DialIAS
        public PtRaw Raw1, Raw2;
        public double Error => DialIAS - TrueCAS;
        public double ErrorFit = 0;
        public bool Include;

        bool Fit.IPt.Include => Include;
        double Fit.IPt.X => DialIAS;
        double Fit.IPt.Y => Error;
    }
}

class CharacteriseLogger : FlightControllerBase
{
    public override string Name { get; set; } = "Logger";
    public ConcurrentQueue<string> Log = new();
    public Func<FrameData, string> LogFrame;
    public Func<FrameData, string> LogConsole;

    public override ControlData ProcessFrame(FrameData frame)
    {
        if (Math.Floor(frame.SimTime * 2) != Math.Floor(Dcs.PrevFrame.SimTime * 2))
            Console.WriteLine(LogConsole(frame));
        Log.Enqueue(LogFrame(frame));
        if (Log.Count > 100)
        {
            var lines = new List<string>();
            while (Log.TryDequeue(out var line))
                lines.Add(line);
            Task.Run(() => File.AppendAllLines("datalogger.csv", lines));
        }
        return null;
    }
}
