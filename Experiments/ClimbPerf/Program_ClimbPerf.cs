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

        DoFlightTest(2.0, 350, 30, loadLeveloffTgt("lvloff.txt"));
    }

    static (double maxAltFt, bool completed) DoFlightTest(double throttle, double preClimbSpeedKts, double climbAngleDeg, double leveloffAltFt)
    {
        Log.Clear();
        var ctrl = new ClimbPerfStraightController();
        ctrl.TestThrottle = throttle;
        ctrl.TestPreClimbSpeedKts = preClimbSpeedKts;
        ctrl.TestClimbAngle = climbAngleDeg;
        ctrl.TestLevelOffAltFt = leveloffAltFt;

        var dcs = new DcsController();
        dcs.FlightControllers.Add(ctrl);
        dcs.Start();
        try
        {
            while (true)
            {
                Thread.Sleep(100);
                PrintLine(0, $"{dcs.LastFrame?.SimTime:0.0} - {dcs.Status}");
                PrintLine(1, $"{ctrl.Stage}; tgtpitch={ctrl.TgtPitch:0.0}; lvloff={ctrl.TestLevelOffAltFt:#,0}; max={ctrl.MaxAltFt:#,0}");
                if (Log.Count > 0)
                {
                    var lines = new List<string>();
                    while (Log.TryDequeue(out var line))
                        lines.Add(line);
                    File.AppendAllLines("log.csv", lines);
                }
                if (ctrl.Status == "done")
                {
                    File.AppendAllLines("lvloff.txt", new[] { Ut.FormatCsvRow(ctrl.TestLevelOffAltFt, ctrl.MaxAltFt, dcs.LastFrame.Skips, ctrl.FuelAtDone * 10803) });
                    return (ctrl.MaxAltFt, true);
                }
                if (ctrl.Status == "failed")
                {
                    return (ctrl.MaxAltFt, false);
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
