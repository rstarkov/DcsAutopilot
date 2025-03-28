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

        var segments = new List<CurveSegment>();
        segments.Add(new SineSegment(0.5, 35));
        segments.Add(new LineSegment(35, 42));
        segments.Add(new LineSegment(42, 95));
        segments.Add(new LineSegment(95, 152));
        segments.Add(new LineSegment(152, 182));
        segments.Add(new LineSegment(182, 199));
        segments.Add(new LineSegment(199, 255));
        segments.Add(new LineSegment(255, 303));
        segments.Add(new LineSegment(303, 355));
        segments.Add(new LineSegment(355, 402));
        segments.Add(new LineSegment(402, 455));
        segments.Add(new LineSegment(455, 500));
        segments.Add(new LineSegment(500, 552));
        segments.Add(new LineSegment(554, 574));
        segments.Add(new LineSegment(575, 594));
        segments.Add(new LineSegment(596, 613));
        segments.Add(new LineSegment(613, 621));
        segments.Add(new LineSegment(623, 633));
        segments.Add(new LineSegment(633, 642));
        segments.Add(new LineSegment(642, 649));
        segments.Add(new LineSegment(653, 669));
        segments.Add(new LineSegment(672, 697));
        segments.Add(new LineSegment(699, 707));
        segments.Add(new LineSegment(709, 715));
        segments.Add(new LineSegment(717, 723));
        segments.Add(new LineSegment(726, 750));
        segments.Add(new LineSegment(750, 797));
        segments.Add(new LineSegment(797, 820));
        foreach (var seg in segments.OfType<LineSegment>())
            fitLine(seg, data, points.Where(pt => pt.DialIAS >= seg.FromX && pt.DialIAS < seg.ToX).ToList());
        foreach (var seg in segments.OfType<SineSegment>())
            fitSine(seg, data, points.Where(pt => pt.DialIAS >= seg.FromX && pt.DialIAS < seg.ToX).ToList());
        foreach (var (seg1, seg2) in segments.ConsecutivePairs(false).Where(p => p.Item1 is LineSegment && p.Item2 is LineSegment).Select(p => (p.Item1 as LineSegment, p.Item2 as LineSegment)))
        {
            if (seg1.ToX == seg2.FromX)
                continue;
            var intersect = Intersect.LineWithLine(seg1.Edge, seg2.Edge).point;
            seg1.ToX = intersect.Value.X;
            seg2.FromX = intersect.Value.X;
        }
        foreach (var seg in segments)
        {
            var pts = points.Where(pt => pt.DialIAS >= seg.FromX && pt.DialIAS < seg.ToX);
            SetShift(data, pts, seg.Misc1);
            foreach (var pt in pts)
                pt.ErrorFit = seg.Calc(pt.DialIAS);
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
        seg.Phase = bestVector[3];
        seg.Offset = bestVector[3];
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
