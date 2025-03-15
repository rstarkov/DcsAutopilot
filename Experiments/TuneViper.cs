using System.Collections.Concurrent;
using DcsAutopilot;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace DcsExperiments;

public static class TuneViper
{
    static string loadout = "medium"; // meaning: 6x AIM120C + 2x HARM, 100% fuel internal. No pods for absolute symmetry.
    static string mass = "32487";

    class TuneController : FlightControllerBase
    {
        public override string Name { get; set; } = "Viper Tune";

        public double? Throttle, SpeedBrake, Pitch;

        public override ControlData ProcessFrame(FrameData frame)
        {
            var ctrl = new ControlData();
            ctrl.ThrottleAxis = Throttle;
            ctrl.SpeedBrakeRate = SpeedBrake;
            ctrl.PitchAxis = Pitch;
            return ctrl;
        }
    }

    public static void CharacteriseStraightAndLevel()
    {
        DcsController dcs = null;
        TuneController ctrl = null;

        var filename = $"viper-tune-straight-level.csv";

        tuneAlt(300, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5, 1.00, 0.95, 0.90, 0.85, 0.80, 0.75, 0.70, 0.65, 0.60, 0.55, 0.50, 0.45, 0.40, 0.35, 0.30, 0.29, 0.28, 0.27 });
        tuneAlt(5_000, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5, 1.00, 0.95, 0.90, 0.85, 0.80, 0.75, 0.70, 0.65, 0.60, 0.55, 0.50, 0.45, 0.40, 0.35/*, ?*/ });
        tuneAlt(10_000, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5, 1.00, 0.95, 0.90, 0.85, 0.80, 0.75, 0.70, 0.65, 0.60, 0.55, 0.50, 0.45/*, ?*/ });
        tuneAlt(15_000, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5, 1.00, 0.95, 0.90, 0.85, 0.80, 0.75, 0.70, 0.65, 0.60, 0.55/*, ?*/ });
        tuneAlt(20_000, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5, 1.00, 0.95, 0.90, 0.85, 0.80, 0.75, 0.70, 0.65/*, ?*/ });
        tuneAlt(22_500, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5, 1.00, 0.95, 0.90, 0.85, 0.80, 0.75/*, ?*/ });
        tuneAlt(25_000, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5, 1.00, 0.95, 0.90, 0.85/*, ?*/ });
        tuneAlt(27_500, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5, 1.00, 0.95/*, ?*/ });
        tuneAlt(30_000, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5 });
        tuneAlt(35_000, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5 });
        tuneAlt(40_000, new[] { 2.0, 1.9, 1.8, 1.7, 1.6 });
        tuneAlt(45_000, new[] { 2.0, 1.9, 1.8, 1.7 });
        // tuneAlt(?, new[] { 2.0, 1.9, 1.8, 1.7, 1.6, 1.5, 1.00, 0.95, 0.90, 0.85, 0.80, 0.75, 0.70, 0.65, 0.60, 0.55, 0.50, 0.45, 0.40, 0.35, 0.30, 0.25, 0.20, 0.15, 0.10, 0.05 });

        void tuneAlt(double altitude, IEnumerable<double> throttles)
        {
            foreach (var throttle in throttles)
            {
                if (File.Exists(filename) && Ut.ParseCsvFile(filename).Select(r => (ld: r[0], alt: r[2].ParseDouble(), thr: r[3].ParseDouble())).Any(x => x.ld == loadout && x.thr == throttle && Math.Abs(x.alt - altitude) < 5))
                    continue;
                again:;
                try
                {
                    if (dcs == null) throw new RestartException();
                    //wait(600_000);
                    Console.WriteLine($"Altitude {altitude:#,0} ft, throttle = {throttle:0.00}");
                    ctrl.Throttle = throttle;
                    ctrl.SpeedBrake = -1;
                    ctrl.Enabled = true;
                    wait(5000);

                    var stable1 = waitStable(altitude);
                    var result1 = dcs.LastFrame;
                    Console.WriteLine($"   from {(stable1.upward ? "below" : "above")}: {stable1.asymptote:0.000} @ {stable1.fit:0.00}, speed={result1.SpeedCalibrated.MsToKts():0.000}");

                    var stable2 = stable1;
                    var result2 = result1;
                    var spd = dcs.LastFrame.SpeedCalibrated.MsToKts();
                    if (!stable1.upward)
                    {
                        int waitOnSpeedbrake = 200;
                        while (dcs.LastFrame.SpeedCalibrated.MsToKts() > spd - 5)
                        {
                            wait(2500);
                            ctrl.SpeedBrake = 1;
                            wait(waitOnSpeedbrake);
                            ctrl.SpeedBrake = -1;
                            waitOnSpeedbrake += 100;
                        }
                        wait(5000);
                        stable2 = waitStable(altitude);
                        result2 = dcs.LastFrame;
                        if (!stable2.upward) throw new Exception(); // expect this to be a "from below" fit
                        ctrl.SpeedBrake = 0;
                    }
                    else if (stable1.upward && throttle < 2.0)
                    {
                        while (dcs.LastFrame.SpeedCalibrated.MsToKts() < spd + 5)
                        {
                            ctrl.Throttle += 0.02;
                            wait(100);
                        }
                        ctrl.Throttle = throttle;
                        stable2 = waitStable(altitude);
                        result2 = dcs.LastFrame;
                        if (stable2.upward) throw new Exception(); // expect this to be a "from above" fit
                    }
                    if (result1 != result2)
                        Console.WriteLine($"   from {(stable2.upward ? "below" : "above")}: {stable2.asymptote:0.000} @ {stable2.fit:0.00}, speed={result2.SpeedCalibrated.MsToKts():0.000}");

                    double avg(Func<FrameData, double> sel) => (sel(result1) + sel(result2)) / 2;
                    File.AppendAllLines(filename, new[] {
                        Ut.FormatCsvRow(loadout, mass, $"{avg(x => x.AltitudeAsl).MetersToFeet():0}", $"{throttle:0.000}", $"{avg(x => x.Pitch):0.000}", $"{avg(x => x.VelPitch):0.000}",
                        $"{(stable1.asymptote + stable2.asymptote) / 2:0.000}", $"{(stable1.fit + stable2.fit) / 2:0.00}", $"{avg(x => x.SpeedCalibrated).MsToKts():0.0}", $"{avg(x => x.SpeedTrue).MsToKts():0.0}", $"{avg(x => x.SpeedMach):0.00000}", $"{avg(x => x.FuelFlow):0}", $"{avg(x => x.FuelInternal):0.0000}", $"{avg(x => x.FuelExternal):0.0000}", $"{DateTime.Now:HH:mm:ss.fff}", $"{Math.Abs(stable1.asymptote - stable2.asymptote):0.000}")
                    });

                    File.AppendAllLines($"viper-tune-straight-level-x2.csv", new[] {
                        Ut.FormatCsvRow(loadout, mass, $"{result1.AltitudeAsl.MetersToFeet():0}", $"{throttle:0.000}", $"{result1.Pitch:0.000}", $"{result1.VelPitch:0.000}",
                        $"{stable1.asymptote:0.000}", $"{stable1.fit:0.00}", $"{result1.SpeedCalibrated.MsToKts():0.0}", $"{result1.SpeedTrue.MsToKts():0.0}", $"{result1.SpeedMach:0.00000}", $"{result1.FuelFlow:0}", $"{result1.FuelInternal:0.0000}", $"{result1.FuelExternal:0.0000}")
                        ,
                        Ut.FormatCsvRow(loadout, mass, $"{result2.AltitudeAsl.MetersToFeet():0}", $"{throttle:0.000}", $"{result2.Pitch:0.000}", $"{result2.VelPitch:0.000}",
                        $"{stable2.asymptote:0.000}", $"{stable2.fit:0.00}", $"{result2.SpeedCalibrated.MsToKts():0.0}", $"{result2.SpeedTrue.MsToKts():0.0}", $"{result2.SpeedMach:0.00000}", $"{result2.FuelFlow:0}", $"{result2.FuelInternal:0.0000}", $"{result2.FuelExternal:0.0000}")
                    });
                    if (Math.Abs(stable1.asymptote - stable2.asymptote) > 0.5 /*kts*/)
                        throw new Exception();
                }
                catch (RestartException)
                {
                    if (dcs != null)
                        dcs.Stop();
                    //DcsWindow.RestartMission();
                    ctrl = new TuneController();
                    ctrl.Enabled = false;
                    dcs = new DcsController();
                    dcs.FlightControllers.Add(ctrl);
                    dcs.Start();
                    while (dcs.LastFrame == null)
                        Thread.Sleep(100);
                    //DcsWindow.SpeedUp();
                    goto again;
                }
            }
        }

        void wait(int ms, bool noconsole = false)
        {
            var start = dcs.LastFrame.SimTime;
            while (dcs.LastFrame.SimTime < start + ms / 1000.0)
            {
                Thread.Sleep(5);
                if (!noconsole)
                    Console.Title = $"spd = {dcs.LastFrame.SpeedCalibrated.MsToKts():0.00}, alt = {dcs.LastFrame.AltitudeAsl.MetersToFeet():#,0.000}";
            }
        }

        (double asymptote, double fit, bool upward) waitStable(double targetAltitude)
        {
            double prevSpeed = 0, prevSpeedAt = 0;
            var ratefilter = Filters.BesselD5;
            var fpsfilter = Filters.BesselD20;
            var log = new Queue<(double t, double s)>();
            var dbgat = dcs.LastFrame.SimTime;
            while (true)
            {
                wait(100, true);
                log.Enqueue((dcs.LastFrame.SimTime, dcs.LastFrame.SpeedCalibrated.MsToKts()));
                while (log.Count > 150)
                    log.Dequeue();

                var speed = dcs.LastFrame.SpeedCalibrated.MsToKts();
                var speedRate = ratefilter.Step((speed - prevSpeed) / (dcs.LastFrame.SimTime - prevSpeedAt));
                prevSpeed = speed;
                prevSpeedAt = dcs.LastFrame.SimTime;
                var alt = dcs.LastFrame.AltitudeAsl.MetersToFeet();
                var altRate = Math.Abs(dcs.LastFrame.SpeedVertical).MetersToFeet(); // feet/sec?
                var pitchRate = Math.Abs(dcs.LastFrame.GyroPitch);
                var fit = log.Count >= 150 ? FitDecay(log.ToArray()) : (0, 0, false);
                var dt = dcs.LastFrame.dT;
                var fps = dt <= 0 ? 0 : fpsfilter.Step(1 / dt);

                var spdiff = Math.Abs(fit.asymptote - speed);
                if (altRate < 0.05 && pitchRate < 0.005 && Math.Abs(dcs.LastFrame.VelPitch) < 0.005 && Math.Abs(alt - targetAltitude) < 5)
                {
                    if (fit.fit > 7 && spdiff < 2.5) return fit;
                    if (fit.fit > 6 && spdiff < 1.5) return fit;
                    if (fit.fit > 5 && spdiff < 0.5) return fit;
                    if (fit.fit > 3 && spdiff < 0.1 && Math.Abs(speedRate) < 0.02) return fit;
                    if (fit.fit > 2 && spdiff < 0.01) return fit;
                    //if (Math.Abs(speedRate) < 0.00005) return (speed, 0, speedRate > 0);
                }
                if (dcs.LastFrame.SimTime - dbgat >= 1.0)
                {
                    //Console.WriteLine($"fit={fit.fit:0.00000}, asym={fit.asymptote:0.000}, spd={speed:0.00}, sprate={speedRate:0.0000}, spdiff={spdiff:0.00}");
                    dbgat = dcs.LastFrame.SimTime;
                }

                Console.Title = $"fuel = {dcs.LastFrame.FuelInternal:0.000}/{dcs.LastFrame.FuelFlow:#,0}, spd = {speed:0.00}/{speedRate:0.0000}/{fit.asymptote:0.000}/{fit.fit:0.00000}, alt = {altRate:0.000}, vp = {dcs.LastFrame.VelPitch:0.00000}/{pitchRate:0.00000}, pch = {dcs.LastFrame.Pitch:0.000}, tgta = {alt - targetAltitude:0}, {(ctrl.Enabled ? "enabled" : "DISABLED")}, fps={fps:0.0}";

                if (dcs.LastFrame.FuelInternal < 0.6 || (dcs.LastFrame.FuelExternal > 0 && dcs.LastFrame.FuelExternal < 0.5))
                    throw new RestartException();
            }
        }
    }

    class RestartException : Exception { }

    public static (double asymptote, double fit, bool upward) FitDecay((double x, double y)[] data)
    {
        // we fit log(y - offset) to a straight line. When the offset matches the constant that to which the exponential decay tends asymptotically, we expect a good linear fit on log(y)
        // other offset values result in a non-linear log(y - offset)
        bool upward = data[^1].y > data[0].y;
        double minY = data.Min(pt => pt.y);
        double maxY = data.Max(pt => pt.y);
        double cur = upward ? maxY : minY;
        var logs = data.Select(pt => (x: pt.x - data[0].x, y: pt.y == cur ? -100 : Math.Log(Math.Abs(pt.y - cur)))).ToArray();
        double prevEval = LinearFitQuality(logs);

        var dir = upward ? 1 : -1;
        var step = (maxY - minY) / 1e5;
        double min = 0, mid = 0, max = 0, minEval = 0, midEval = 0, maxEval = 0;
        while (true)
        {
            min = mid; mid = cur;
            minEval = midEval; midEval = prevEval;
            cur += step * dir;
            if (cur > maxY + 100 * (maxY - minY) || cur < minY - 100 * (maxY - minY))
                return (0, 0, false); // refuse to attempt to fit something that looks very far off
            for (int i = 0; i < logs.Length; i++)
                logs[i].y = Math.Log(Math.Abs(data[i].y - cur));
            var curEval = LinearFitQuality(logs);
            if (curEval < prevEval)
            {
                max = cur;
                maxEval = curEval;
                break;
            }
            step *= 2;
            prevEval = curEval;
        }
        if (min == 0)
            return (0, 0, false);
        while (max - min > 0.0001)
        {
            if (midEval == minEval || midEval == maxEval) break;
            if (midEval < minEval || midEval < maxEval || mid <= min || mid >= max) throw new Exception();
            if (mid - min > max - mid)
            {
                cur = (min + mid) / 2;
                for (int i = 0; i < logs.Length; i++)
                    logs[i].y = Math.Log(Math.Abs(data[i].y - cur));
                var curEval = LinearFitQuality(logs);
                if (curEval < midEval)
                {
                    min = cur;
                    minEval = curEval;
                }
                else
                {
                    max = mid;
                    maxEval = midEval;
                    mid = cur;
                    midEval = curEval;
                }
            }
            else
            {
                cur = (mid + max) / 2;
                for (int i = 0; i < logs.Length; i++)
                    logs[i].y = Math.Log(Math.Abs(data[i].y - cur));
                var curEval = LinearFitQuality(logs);
                if (curEval < midEval)
                {
                    max = cur;
                    maxEval = curEval;
                }
                else
                {
                    min = mid;
                    minEval = midEval;
                    mid = cur;
                    midEval = curEval;
                }
            }
        }
        return (mid, midEval, upward);

        static double LinearFitQuality((double x, double y)[] data)
        {
            double Sx = 0;
            double Sy = 0;
            double Sxx = 0;
            double Sxy = 0;
            foreach (var pt in data)
            {
                Sx += pt.x;
                Sy += pt.y;
                Sxx += pt.x * pt.x;
                Sxy += pt.x * pt.y;
            }
            var m = (Sxy * data.Length - Sx * Sy) / (Sxx * data.Length - Sx * Sx);
            var b = (Sxy * Sx - Sy * Sxx) / (Sx * Sx - data.Length * Sxx);
            var rss = data.Sum(pt => { var v = (m * pt.x + b) - pt.y; return v * v; });
            var mean = data.Average(pt => pt.y);
            var tss = data.Sum(pt => { var v = mean - pt.y; return v * v; });
            return Math.Log10(tss) - Math.Log10(rss); // r_squared = 1 - rss / tss; but it goes 0.9999912341 so use a log10 instead
        }
    }

    class CharacterisePitchAxisGyroController : FlightControllerBase
    {
        public override string Name { get; set; } = "CharacterisePitchAxisGyro";
        private ViperControl _control = new();
        public bool Logging;
        public ConcurrentQueue<string> Log = new();
        public double SpeedIas;
        public double PitchAxis;

        public override ControlData ProcessFrame(FrameData frame)
        {
            if (Logging)
                Log.Enqueue(Ut.FormatCsvRow(loadout, mass, PitchAxis, frame.GyroPitch, frame.SpeedCalibrated.MsToKts(), frame.AltitudeAsl, frame.AngleOfAttack, frame.Pitch, frame.Bank));
            var ctrl = new ControlData();
            _control.ControlSpeedIAS(frame, ctrl, SpeedIas);
            ctrl.PitchAxis = PitchAxis;
            return ctrl;
        }
    }

    public static void CharacterisePitchAxisGyro()
    {
        // TODO: log IAS and ALT from cockpit instruments
        // TODO: write code to extract datapoints
        // TODO: write interpolator which can apply this pitch axis in the ViperControl
        RunOne(500, 1.75, 0.06, 6, 2);
        RunOne(500, 1.75, 0.10, 6, 3);
        RunOne(500, 1.75, 0.15, 7, 5);
        RunOne(500, 1.75, 0.20, 8, 6);
        RunOne(500, 1.75, 0.25, 10, 8);
        RunOne(500, 1.75, 0.30, 12, 10);
    }

    public static void RunOne(double speed, double vpitchNeutral, double pitchAxis, double pitchLimit, int iterations)
    {
        DcsWindow.RestartMission();
        DcsWindow.SpeedUp();
        var ctrl = new CharacterisePitchAxisGyroController();
        ctrl.Enabled = true;
        ctrl.SpeedIas = speed;
        var dcs = new DcsController();
        dcs.FlightControllers.Add(ctrl);
        dcs.Start();
        while (dcs.LastFrame == null)
            Thread.Sleep(100);
        for (int iter = 0; iter < iterations; iter++)
        {
            // set a fixed pitch axis input
            ctrl.PitchAxis = pitchAxis;
            wait(2000);
            ctrl.Logging = true;
            while (dcs.LastFrame.Pitch < pitchLimit + vpitchNeutral && dcs.LastFrame.SpeedCalibrated.MsToKts() > 250)
            {
                wait(50);
                dumpLog();
            }
            ctrl.Logging = false;
            // set a reverse input and do it again
            ctrl.PitchAxis = -pitchAxis;
            wait(2000);
            ctrl.Logging = true;
            while (dcs.LastFrame.Pitch > -pitchLimit + vpitchNeutral)
            {
                wait(50);
                dumpLog();
            }
            ctrl.Logging = false;
        }
        dumpLog();
        dcs.Stop();

        void wait(int ms, bool noconsole = false)
        {
            var start = dcs.LastFrame.SimTime;
            while (dcs.LastFrame.SimTime < start + ms / 1000.0)
            {
                Thread.Sleep(5);
                if (!noconsole)
                    Console.Title = $"spd = {dcs.LastFrame.SpeedCalibrated.MsToKts():0.00}, alt = {dcs.LastFrame.AltitudeAsl.MetersToFeet():#,0.000}";
            }
        }

        void dumpLog()
        {
            if (ctrl.Log.Count == 0)
                return;
            var lines = new List<string>();
            while (ctrl.Log.TryDequeue(out var line))
                lines.Add(line);
            File.AppendAllLines("viper-tune-pitchgyro.csv", lines);
        }
    }
}
