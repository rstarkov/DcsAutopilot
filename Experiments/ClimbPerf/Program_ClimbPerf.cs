using System.Collections.Concurrent;
using DcsAutopilot;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace ClimbPerf;

internal class Program_ClimbPerf
{
    public static ConcurrentQueue<string> Log = new();

    static void Main(string[] args)
    {
        Console.CursorVisible = false;

        var config = new StraightClimbTest.TestConfig
        {
            FinalTargetAltitudeFt = 35000,
            FinalTargetMach = 0.90,
            Throttle = 2.0,
            PreClimbSpeedKts = 350,
            ClimbAngle = 30,
            LevelOffAltFt = loadLeveloffTgt("lvloff.txt"),
            METotalMassLb = 37737,
            MEFuelMassIntLb = 10803,
            MEFuelMassExtLb = 0,
        };
        DoFlightTest(config);
    }

    static StraightClimbTest DoFlightTest(StraightClimbTest.TestConfig cfg)
    {
        Log.Clear();
        var ctrl = new ClimbPerfStraightController();
        ctrl.Test = new();
        ctrl.Test.LogName = $"straightclimb--{cfg.FinalTargetAltitudeFt:0}-{cfg.FinalTargetMach:0.00}--{cfg.Throttle:0.0}-{cfg.PreClimbSpeedKts:0}-{cfg.ClimbAngle:0}.csv";
        ctrl.Test.Config = cfg;

        var dcs = new DcsController();
        dcs.FlightControllers.Add(ctrl);
        dcs.Start();
        try
        {
            while (true)
            {
                Thread.Sleep(100);
                PrintLine(0, $"{dcs.LastFrame?.SimTime:0.0} - {dcs.Status}");
                PrintLine(1, $"{ctrl.Stage}; tgtpitch={ctrl.TgtPitch:0.0}; lvloff={ctrl.Test.Config.LevelOffAltFt:#,0}; max={ctrl.Test.Result.MaxAltitudeFt:#,0}");
                if (Log.Count > 0)
                {
                    var lines = new List<string>();
                    while (Log.TryDequeue(out var line))
                        lines.Add(line);
                    File.AppendAllLines("log.csv", lines);
                }
                if (ctrl.Status == "done" || ctrl.Status == "failed")
                {
                    var t = ctrl.Test;
                    t.Result.FuelUsedLb = (t.Result.RawFuelAtEndInt - t.Result.RawFuelAtStartInt) * t.Config.MEFuelMassIntLb + (t.Result.RawFuelAtEndExt - t.Result.RawFuelAtStartExt) * t.Config.MEFuelMassExtLb;
                    File.AppendAllLines("lvloff.txt", new[] { Ut.FormatCsvRow(t.Config.LevelOffAltFt, t.Result.MaxAltitudeFt, dcs.LastFrame.Skips, t.Result.RawFuelAtEndInt * 10803) });
                    return t;
                }
            }
        }
        finally
        {
            dcs.Stop();
        }
    }

    private static double loadLeveloffTgt(string filename)
    {
        if (!File.Exists(filename))
            return 33000;
        var data = Ut.ParseCsvFile(filename).Select(r => (tgt: double.Parse(r[0]), final: double.Parse(r[1]))).ToList();
        if (data.Count == 1)
            return data[0].tgt - (data[0].final - 35000) * 1.2;
        var lo = data.Where(d => d.final < 35000).MaxElementOrDefault(d => d.tgt);
        var hi = data.Where(d => d.final > 35000).MinElementOrDefault(d => d.tgt);
        if (lo == default || hi == default)
        {
            var nearest = data.OrderBy(d => Math.Abs(d.final - 35000)).Take(2).ToList();
            lo = nearest[0]; // lo doesn't have to actually be less than hi
            hi = nearest[1];
        }
        return lo.tgt + (35000 - lo.final) / (hi.final - lo.final) * (hi.tgt - lo.tgt);
    }

    static void PrintLine(int row, string line)
    {
        Console.SetCursorPosition(0, row);
        Console.Write(line.PadRight(Console.WindowWidth));
    }
}

class StraightClimbTest
{
    public string LogName;

    public TestConfig Config = new();
    public class TestConfig
    {
        public double FinalTargetMach;
        public double FinalTargetAltitudeFt;

        public double Throttle;
        public double PreClimbSpeedKts;
        public double ClimbAngle;
        public double LevelOffAltFt;

        public double METotalMassLb; // per mission editor
        public double MEFuelMassIntLb;
        public double MEFuelMassExtLb;
    }

    public TestResult Result = new();
    public class TestResult
    {
        public double MaxAltitudeFt;
        public double RawFuelAtStartInt, RawFuelAtStartExt, RawFuelAtEndInt, RawFuelAtEndExt;
        public double FuelUsedLb;
        public double ClimbDuration;
        public double ClimbDistance;

        public string FailReason; // or null for success
    }
}
