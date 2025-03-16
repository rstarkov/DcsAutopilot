using System.Collections.Concurrent;
using System.Text;
using DcsAutopilot;
using RT.Util.ExtensionMethods;
using RT.Util;

namespace DcsExperiments.ClimbFuelTest;

class ClimbFuelTest
{
    const int colT = 0;
    const int colCAS = 1;
    const int colASL = 2;
    const int colVertSpd = 3;
    const int colVelPitch = 4;
    const int colPitch = 5;
    const int colGyroPitch = 6;
    const int colAngleOfAttack = 7;
    const int colThrottleAxis = 8;
    const int colPitchAxis = 9;
    const int colFuelFlow = 10;
    const int colFuelInternal = 11;
    const int colFuelExternal = 12;
    const int colPosX = 13;
    const int colPosY = 14;
    const int colPosZ = 15;

    const int colDistTick = 16;
    const int colDistTotal = 17;
    const int colGS = 18;
    const int colLbNmi = 19;
    const int colFuelLb = 20;
    const int colFuelLbUsed = 21;

    const double fullFuelLb = 7189.262;

    public static void AnalyseDataLog()
    {
        var csvfull = loadFile(@"P:\DcsAutopilot\#Archive\ClimberFuelCompare\datalog-climb-full.csv");
        var csv21k = loadFile(@"P:\DcsAutopilot\#Archive\ClimberFuelCompare\datalog-climb-21k.csv");

        resetStart(csvfull, 17.098); // at 851asl; 2deg at 18.215/524asl = 6.7sec; enabled at 17.098/505asl; fuel at start = 1.2659674882889, or approx 12lb less
        resetStart(csv21k, 10.151); // at 820asl; 2deg at 11.595/501asl = 6.4sec; enabled at 9.935/477asl; fuel at start = 1.2675296068192

        resampleAndJoinUp(csv21k, csvfull, @"P:\DcsAutopilot\#Archive\ClimberFuelCompare\out-bytime.csv", colT, 1.0);
        resampleAndJoinUp(csv21k, csvfull, @"P:\DcsAutopilot\#Archive\ClimberFuelCompare\out-bydist.csv", colDistTotal, 500.0);
        resampleAndJoinUp(csv21k, csvfull, @"P:\DcsAutopilot\#Archive\ClimberFuelCompare\out-byfuel.csv", colFuelLbUsed, 2.0);
    }

    private static void resampleAndJoinUp(double[][] csv1, double[][] csv2, string outname, int colX, double xStep)
    {
        var csv1r = resample(csv1, colX, xStep);
        var csv2r = resample(csv2, colX, xStep);
        var blank = new double[csv1r[0].Length].Select(v => double.NaN).ToArray();
        var output = new List<string>();
        var hdr = new StringBuilder();
        foreach (var col in new string[] { "T", "CAS", "Alt", "VertSpd", "VelPitch", "Pitch", "GyroPitch", "AoA", "ThrottleAxis", "PitchAxis", "FuelFlow", "FuelInt", "FuelExt", "PosX", "PosY", "PosZ", "DistTick", "DistTotal", "GS", "LbNmi", "FuelLb", "FuelLbUsed" })
            hdr.Append(col + "-21k," + col + "-full,");
        output.Add(hdr.ToString());
        for (int r = 0; r < Math.Max(csv1r.Length, csv2r.Length); r++)
        {
            var r1 = r < csv1r.Length ? csv1r[r] : blank;
            var r2 = r < csv2r.Length ? csv2r[r] : blank;
            var row = r1.Zip(r2, (a, b) => new double[] { a, b }).SelectMany(x => x).ToArray();
            output.Add(row.Select(d => double.IsNaN(d) ? "" : d.ToString()).JoinString(","));
        }
        File.WriteAllLines(outname, output);
    }

    private static double[][] resample(double[][] csv, int colX, double xStep)
    {
        var result = new List<double[]>();
        double tgtX = 0;
        for (int r = 1; r < csv.Length; r++)
        {
            var cur = csv[r];
            var prev = csv[r - 1];
            // skip until start time
            if (cur[colT] < 0)
                continue;
            // end
            if (cur[colFuelLb] <= 0.95 * fullFuelLb)
                break;
            // initialise
            if (prev[colT] < 0)
                tgtX = cur[colX];
            // take a sample
            if (tgtX > prev[colX] && tgtX <= cur[colX])
            {
                result.Add(cur);
                tgtX += xStep;
            }
        }
        return result.ToArray();
    }

    private static void resetStart(double[][] csv, double tStart)
    {
        var startIndex = csv.IndexOf(r => r[colT] == tStart);
        var startTime = csv[startIndex][colT];
        var startDist = csv[startIndex][colDistTotal];
        var startFuel = csv[startIndex][colFuelLb];
        foreach (var row in csv)
        {
            row[colT] -= startTime;
            row[colDistTotal] -= startDist;
            row[colFuelLbUsed] = startFuel - row[colFuelLb];
        }
    }

    static double[][] loadFile(string name)
    {
        var csv = Ut.ParseCsvFile(name).Skip(1).Select(r => r.Select(c => c == "" ? double.NaN : double.Parse(c)).ToList()).ToList();
        foreach (var row in csv)
            while (row.Count < colFuelLbUsed + 1)
                row.Add(0);
        csv[0][colFuelLb] = csv[0][colFuelInternal] * fullFuelLb;
        for (int r = 1; r < csv.Count; r++)
        {
            var cur = csv[r];
            var prev = csv[r - 1];
            cur[colASL] = cur[colASL] / 21301.0 * 21000.0;
            cur[colVertSpd] = cur[colVertSpd] * 60;
            cur[colDistTick] = Math.Sqrt(Math.Pow(cur[colPosX] - prev[colPosX], 2) + Math.Pow(cur[colPosZ] - prev[colPosZ], 2));
            cur[colDistTotal] = prev[colDistTotal] + cur[colDistTick]; // meters
            cur[colGS] = cur[colDistTick] / (cur[colT] - prev[colT]); // m/s
            cur[colLbNmi] = cur[colFuelFlow] / 3600 / cur[colGS] * 1852;
            var fuelTrueButNoisy = cur[colFuelInternal] * fullFuelLb;
            var fuelTotalised = prev[colFuelLb] - cur[colFuelFlow] / 3600 * (cur[colT] - prev[colT]);
            cur[colFuelLb] = fuelTotalised * 0.99 + 0.01 * fuelTrueButNoisy;
        }
        return csv.Select(r => r.ToArray()).ToArray();
    }
}

public class DataLogger : FlightControllerBase
{
    public override string Name { get; set; } = "Data Logger";
    private ConcurrentQueue<string> _datalog = new();

    public override ControlData ProcessFrame(FrameData frame)
    {
        var datalog = $"{frame.SimTime},{frame.SpeedCalibrated.MsToKts()},{frame.AltitudeAsl.MetersToFeet()},{frame.SpeedVertical.MetersToFeet()},{frame.VelPitch},{frame.Pitch},{frame.GyroPitch},{frame.AngleOfAttack},{Dcs.LastControl?.ThrottleAxis},{Dcs.LastControl?.PitchAxis},{frame.FuelFlow},{frame.FuelInternal},{frame.FuelExternal},{frame.PosX},{frame.PosY},{frame.PosZ}";
        _datalog.Enqueue(datalog);
        if (Random.Shared.NextDouble() < 0.01)
            Task.Run(() =>
            {
                var lines = new List<string>();
                while (_datalog.TryDequeue(out var line))
                    lines.Add(line);
                File.AppendAllLines("datalog.csv", lines);
            });
        return null;
    }
}
