using System.Collections.Concurrent;
using DcsAutopilot;

namespace ClimbPerf;

internal class Program_ClimbPerf
{
    public static ConcurrentQueue<string> Log = new();

    static void Main(string[] args)
    {
        CaptureLogs();
    }

    static void CaptureLogs()
    {
        Console.CursorVisible = false;
        var ctrl = new ClimbPerfStraightController();
        var dcs = new DcsController();
        dcs.FlightControllers.Add(ctrl);
        dcs.Start();
        while (true)
        {
            Thread.Sleep(100);
            PrintLine(0, $"{dcs.LastFrame?.SimTime:0.0} - {dcs.Status}");
            PrintLine(1, $"{ctrl.Status}");
            if (Log.Count > 0)
            {
                var lines = new List<string>();
                while (Log.TryDequeue(out var line))
                    lines.Add(line);
                File.AppendAllLines("log.csv", lines);
            }
        }
    }

    static void PrintLine(int row, string line)
    {
        Console.SetCursorPosition(0, row);
        Console.Write(line.PadRight(Console.WindowWidth));
    }
}
