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
        ctrl.LogConsole = frame => $"Accel: {(frame.SpeedTrue - dcs.PrevFrame.SpeedTrue).MsToKts() / frame.dT:0.000}";
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

        var segments = new List<Segment>();
        segments.Add(new SineSegment { FromSpd = 0.5, ToSpd = 35 });
        segments.Add(new LineSegment() { FromSpd = 35, ToSpd = 42 });
        segments.Add(new LineSegment() { FromSpd = 42, ToSpd = 95 });
        segments.Add(new LineSegment() { FromSpd = 95, ToSpd = 152 });
        segments.Add(new LineSegment() { FromSpd = 152, ToSpd = 182 });
        segments.Add(new LineSegment() { FromSpd = 182, ToSpd = 199 });
        segments.Add(new LineSegment() { FromSpd = 199, ToSpd = 255 });
        segments.Add(new LineSegment() { FromSpd = 255, ToSpd = 303 });
        segments.Add(new LineSegment() { FromSpd = 303, ToSpd = 355 });
        segments.Add(new LineSegment() { FromSpd = 355, ToSpd = 402 });
        segments.Add(new LineSegment() { FromSpd = 402, ToSpd = 455 });
        segments.Add(new LineSegment() { FromSpd = 455, ToSpd = 500 });
        segments.Add(new LineSegment() { FromSpd = 500, ToSpd = 552 });
        segments.Add(new LineSegment() { FromSpd = 554, ToSpd = 574 });
        segments.Add(new LineSegment() { FromSpd = 575, ToSpd = 594 });
        segments.Add(new LineSegment() { FromSpd = 596, ToSpd = 613 });
        segments.Add(new LineSegment() { FromSpd = 613, ToSpd = 621 });
        segments.Add(new LineSegment() { FromSpd = 623, ToSpd = 633 });
        segments.Add(new LineSegment() { FromSpd = 633, ToSpd = 642 });
        segments.Add(new LineSegment() { FromSpd = 642, ToSpd = 649 });
        segments.Add(new LineSegment() { FromSpd = 653, ToSpd = 669 });
        segments.Add(new LineSegment() { FromSpd = 672, ToSpd = 697 });
        segments.Add(new LineSegment() { FromSpd = 699, ToSpd = 707 });
        segments.Add(new LineSegment() { FromSpd = 709, ToSpd = 715 });
        segments.Add(new LineSegment() { FromSpd = 717, ToSpd = 723 });
        segments.Add(new LineSegment() { FromSpd = 726, ToSpd = 750 });
        segments.Add(new LineSegment() { FromSpd = 750, ToSpd = 797 });
        segments.Add(new LineSegment() { FromSpd = 797, ToSpd = 820 });
        foreach (var seg in segments.OfType<LineSegment>())
            fitLine(seg, data, points.Where(pt => pt.DialIAS >= seg.FromSpd && pt.DialIAS < seg.ToSpd).ToList());
        foreach (var seg in segments.OfType<SineSegment>())
            fitSine(seg, data, points.Where(pt => pt.DialIAS >= seg.FromSpd && pt.DialIAS < seg.ToSpd).ToList());
        foreach (var (seg1, seg2) in segments.ConsecutivePairs(false).Where(p => p.Item1 is LineSegment && p.Item2 is LineSegment).Select(p => (p.Item1 as LineSegment, p.Item2 as LineSegment)))
        {
            if (seg1.ToSpd == seg2.FromSpd)
                continue;
            var intersect = Intersect.LineWithLine(seg1.Line, seg2.Line).point;
            seg1.ToSpd = intersect.Value.X;
            seg2.FromSpd = intersect.Value.X;
        }
        foreach (var seg in segments)
        {
            var pts = points.Where(pt => pt.DialIAS >= seg.FromSpd && pt.DialIAS < seg.ToSpd);
            SetShift(data, pts, seg.Shift);
            seg.Apply(pts);
        }
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
        seg.Shift = bestVector[0];
        SetShift(data, pts, bestVector[0]);
        var finalEval = Fit.OrthoLinReg(pts);
        seg.Err = -finalEval.PerpRMS;
        seg.Gradient = finalEval.Slope;
        seg.Intercept = finalEval.Intercept;
        Console.WriteLine($"Line {seg.FromSpd}-{seg.ToSpd}: MSE={seg.Err:0.00000}, shift={seg.Shift:0.00000}, grad={seg.Gradient:0.00000}, offs={seg.Intercept:0.00000}");
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
        Console.WriteLine($"Sine {seg.FromSpd}-{seg.ToSpd}: MSE={-bestEval:0.00000}, shift={bestVector[0]:0.00000}, a={bestVector[1]:0.0000}, b={bestVector[2]:0.00000}, c={bestVector[3]:0.0000}");
        seg.Shift = bestVector[0];
        seg.A = bestVector[1];
        seg.B = bestVector[2];
        seg.C = bestVector[3];
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

    abstract class Segment
    {
        public double FromSpd, ToSpd;
        public double Shift;
        public double Err;
        public abstract void Apply(IEnumerable<Pt> points);
    }

    class LineSegment : Segment
    {
        public double Gradient, Intercept;
        public override void Apply(IEnumerable<Pt> points)
        {
            foreach (var pt in points)
                pt.ErrorFit = Gradient * pt.DialIAS + Intercept;
        }
        public EdgeD Line => new(FromSpd, Gradient * FromSpd + Intercept, ToSpd, Gradient * ToSpd + Intercept);
    }

    class SineSegment : Segment
    {
        public double A, B, C;
        public override void Apply(IEnumerable<Pt> points)
        {
            foreach (var pt in points)
                pt.ErrorFit = A * Math.Sin(B * pt.DialIAS + C) + C;
        }
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
