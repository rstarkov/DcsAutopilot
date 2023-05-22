using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using DcsAutopilot;
using RT.Serialization;
using RT.Util;
using RT.Util.ExtensionMethods;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ClimbPerf;

internal class Program_ClimbPerf
{
    private static string LogPath;
    public static ConcurrentQueue<string> Log = new();
    private static List<StraightClimbTest> TestLogs = new();

    static void Main(string[] args)
    {
        LogPath = args[0];
        Console.CursorVisible = false;
        if (File.Exists(Path.Combine(LogPath, "tests.xml")))
            TestLogs = ClassifyXml.DeserializeFile<List<StraightClimbTest>>(Path.Combine(LogPath, "tests.xml"));
        GenResults();

        Console.WriteLine($"Start the mission in DCS but don't press FLY on the final Briefing screen.");
        Console.WriteLine($"Press ENTER when ready, then switch to DCS within 5 seconds.");
        Console.ReadLine();
        Console.WriteLine($"SWITCH TO DCS NOW!");
        Thread.Sleep(5000);

        var scenarios = new[] { 2.0, 1.8, 1.6, 1.9, 1.7, 1.5 }.SelectMany(throttle => new[] { 300, 400, 200, 250, 350, 450 }, (throttle, speed) => (throttle, speed)).ToList();
        foreach (var scenario in scenarios)
        {
            while (true)
            {
                var config = new StraightClimbTest.TestConfig // must be a new instance every time!
                {
                    FinalTargetAltitudeFt = 35000,
                    FinalTargetMach = 0.90,
                    Throttle = scenario.throttle,
                    PreClimbSpeedKts = scenario.speed,
                    METotalMassLb = 37737,
                    MEFuelMassIntLb = 10803,
                    MEFuelMassExtLb = 0,
                };

                var byAngle = TestLogs.Where(t => SameConfigExceptAngle(t.Config, config)).GroupBy(t => t.Config.ClimbAngle).ToDictionary(g => g.Key, g => new StraightClimbTestGroup(g.Key, g));
                // the tests in each group now differ only by level off altitude

                // First priority is to continue working on any incomplete angles
                double levelOffAltFt;
                var incomplete = byAngle.Values.FirstOrDefault(g => !g.IsCompleted && !g.IsFailed);
                if (incomplete != null)
                {
                    config.ClimbAngle = incomplete.Angle;
                    levelOffAltFt = incomplete.LevelOffAlt;
                }
                else
                {
                    // Pick a new climb angle to test. Every angle is either complete or failed (there could be none)
                    // First sweep with 8 degree steps until failure
                    var lowestFailed = byAngle.Values.Where(g => g.IsFailed).MinElementOrDefault(g => g.Angle)?.Angle ?? 99;
                    var sweep = Enumerable.Range(1, 99).Where(a => a % 8 == 0 && a < lowestFailed);
                    var untestedSweep = sweep.FirstOrDefault(a => !byAngle.ContainsKey(a));
                    if (untestedSweep != default)
                        config.ClimbAngle = untestedSweep;
                    else
                    {
                        // TODO: then subdivide every range by 2 if it looks like it might contain the optimum
                        // what if the optimum is right near the fail limit or at 8?
                        // Otherwise we have nothing left to test for this scenario!
                        break;
                    }

                    // Our initial guesstimate of the level off altitude for this setup, given a complete lack of any better information
                    levelOffAltFt = config.FinalTargetAltitudeFt - 3000; // it should be low enough that we're not risking the first try being too high while risking being wrongly marked as impossible
                    // later: we could actually interpolate based on nearby tests
                }

                // Print a few recent tests
                Console.SetCursorPosition(0, 10);
                foreach (var t in TestLogs.TakeLast(5))
                    Console.WriteLine($"throttle={t.Config.Throttle:0.0} speed={t.Config.PreClimbSpeedKts:0} angle={t.Config.ClimbAngle:0} lvloff={t.LevelOffAltFt:0} --- alt={t.Result.MaxAltitudeFt:0} fuel={t.Result.FuelUsedLb:0} dur={t.Result.ClimbDuration:0} dist={t.Result.ClimbDistance:#,0} {t.Result.FailReason}");

                // Run the flight test!
                DcsRestartMission(); // this returns right after the mission starts loading, and we then have a few seconds to initialise and be ready for the first frame's UDP data
                var testlog = DoFlightTest(config, levelOffAltFt);
                TestLogs.Add(testlog);
                ClassifyXml.SerializeToFile(TestLogs, Path.Combine(LogPath, "tests.xml"));
                GenResults();
            }
        }
    }

    private static void DcsRestartMission()
    {
        // Restart mission
        SendScancode(42, true); // LShift
        Thread.Sleep(100);
        SendScancode(19, true); // R
        Thread.Sleep(100);
        SendScancode(19, false); // R
        Thread.Sleep(100);
        SendScancode(42, false); // LShift
        // Wait for restart to begin
        Thread.Sleep(1000);
        // Post Escape and call it a day; DCS buffers that and processes it the moment the mission is ready.
        SendScancode(1, true); // Esc
        Thread.Sleep(100);
        SendScancode(1, false); // Esc
    }

    static void SendScancode(ushort scan, bool down)
    {
        // Sending key combos like Shift+R doesn't work if it's one big array of down/up events. We must make multiple calls to SendInput.
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[0].Anonymous.ki.wVk = 0;
        inputs[0].Anonymous.ki.wScan = scan;
        inputs[0].Anonymous.ki.dwFlags = down ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>());
    }

    static void GenResults()
    {
        var biggroups = TestLogs.GroupBy(t => (t.Config.FinalTargetAltitudeFt, t.Config.FinalTargetMach, t.Config.METotalMassLb));
        foreach (var bg in biggroups)
        {
            var filename = Path.Combine(LogPath, $"results-{bg.Key.FinalTargetAltitudeFt:0}-{bg.Key.FinalTargetMach}-{bg.Key.METotalMassLb}.csv");
            File.Delete(filename);
            foreach (var throttle in bg.Select(t => t.Config.Throttle).Distinct().Order())
            {
                File.AppendAllLines(filename, new[] { $"=== Throttle {throttle:0.0} ===" });
                var sg = bg.Where(t => t.Config.Throttle == throttle);
                string findResult(double a, double s)
                {
                    var r = sg.Where(t => t.Config.ClimbAngle == a && t.Config.PreClimbSpeedKts == s);
                    if (!r.Any())
                        return "";
                    var grp = new StraightClimbTestGroup(a, r);
                    return grp.IsFailed ? "x" : !grp.IsCompleted ? "?" : $"{grp.FuelUsedLb:0}";
                }
                var speeds = sg.Select(t => t.Config.PreClimbSpeedKts).Distinct().Order();
                var angles = sg.Select(t => t.Config.ClimbAngle).Distinct().Order();
                File.AppendAllLines(filename, new[] { Ut.FormatCsvRow("".Concat(speeds.Select(s => $"{s:0}"))) });
                foreach (var angle in angles)
                    File.AppendAllLines(filename, new[] { Ut.FormatCsvRow($"{angle:0}".Concat(speeds.Select(speed => findResult(angle, speed)))) });
            }
        }
    }

    static StraightClimbTest DoFlightTest(StraightClimbTest.TestConfig cfg, double levelOffAltFt)
    {
        Log.Clear();
        var logRand = Random.Shared.NextString(6, "abcdefghijklmnopqrstuvwxyz0123456789");
        var ctrl = new ClimbPerfStraightController();
        ctrl.Test = new();
        ctrl.Test.StartedUtc = DateTime.UtcNow;
        ctrl.Test.LogName = $"straightclimb--{cfg.FinalTargetAltitudeFt:0}-{cfg.FinalTargetMach:0.00}--{cfg.Throttle:0.0}-{cfg.PreClimbSpeedKts:0}-{cfg.ClimbAngle:0}--{logRand}.csv";
        ctrl.Test.Config = cfg;
        ctrl.Test.LevelOffAltFt = levelOffAltFt;
        var logFilename = Path.Combine(LogPath, "csv", ctrl.Test.LogName);
        Directory.CreateDirectory(Path.GetDirectoryName(logFilename));

        var dcs = new DcsController();
        dcs.FlightControllers.Add(ctrl);
        dcs.Start();
        try
        {
            while (true)
            {
                Thread.Sleep(100);
                PrintLine(0, $"Straight line climb to {cfg.FinalTargetAltitudeFt:0} @ M{cfg.FinalTargetMach:0.0}");
                PrintLine(1, $"Testing throttle={cfg.Throttle:0.0}, speed={cfg.PreClimbSpeedKts:0}, angle={cfg.ClimbAngle:0}, lvloff={levelOffAltFt:0}");
                PrintLine(3, $"{dcs.LastFrame?.SimTime ?? 0:0.0} - {dcs.Status}");
                PrintLine(4, $"{ctrl.Stage}; tgtpitch={ctrl.TgtPitch:0.0}; lvloff={ctrl.Test.LevelOffAltFt:#,0}; max={ctrl.Test.Result.MaxAltitudeFt:#,0}");
                if (Log.Count > 0)
                {
                    var lines = new List<string>();
                    while (Log.TryDequeue(out var line))
                        lines.Add(line);
                    File.AppendAllLines(logFilename, lines);
                }
                if (ctrl.Stage == "done" || ctrl.Stage == "failed")
                {
                    var t = ctrl.Test;
                    t.Result.FuelUsedLb = (t.Result.RawFuelAtStartInt - t.Result.RawFuelAtEndInt) * t.Config.MEFuelMassIntLb + (t.Result.RawFuelAtStartExt - t.Result.RawFuelAtEndExt) * t.Config.MEFuelMassExtLb;
                    return t;
                }
            }
        }
        finally
        {
            dcs.Stop();
        }
    }

    // todo: this is the same as the record type == and is not used directly
    static bool SameConfig(StraightClimbTest.TestConfig cfg1, StraightClimbTest.TestConfig cfg2) =>
        cfg1.FinalTargetAltitudeFt == cfg2.FinalTargetAltitudeFt && cfg1.FinalTargetMach == cfg2.FinalTargetMach
        && cfg1.Throttle == cfg2.Throttle && cfg1.PreClimbSpeedKts == cfg2.PreClimbSpeedKts && cfg1.ClimbAngle == cfg2.ClimbAngle
        && cfg1.METotalMassLb == cfg2.METotalMassLb && cfg1.MEFuelMassIntLb == cfg2.MEFuelMassIntLb && cfg1.MEFuelMassExtLb == cfg2.MEFuelMassExtLb;

    static bool SameConfigExceptAngle(StraightClimbTest.TestConfig cfg1, StraightClimbTest.TestConfig cfg2) =>
        cfg1.FinalTargetAltitudeFt == cfg2.FinalTargetAltitudeFt && cfg1.FinalTargetMach == cfg2.FinalTargetMach
        && cfg1.Throttle == cfg2.Throttle && cfg1.PreClimbSpeedKts == cfg2.PreClimbSpeedKts
        && cfg1.METotalMassLb == cfg2.METotalMassLb && cfg1.MEFuelMassIntLb == cfg2.MEFuelMassIntLb && cfg1.MEFuelMassExtLb == cfg2.MEFuelMassExtLb;

    static void PrintLine(int row, string line)
    {
        Console.SetCursorPosition(0, row);
        Console.Write(line.PadRight(Console.WindowWidth));
    }
}

class StraightClimbTest
{
    public DateTime StartedUtc;
    public string LogName;

    public TestConfig Config = new();
    public record class TestConfig
    {
        public double FinalTargetMach;
        public double FinalTargetAltitudeFt;

        public double Throttle;
        public double PreClimbSpeedKts;
        public double ClimbAngle;

        public double METotalMassLb; // per mission editor
        public double MEFuelMassIntLb;
        public double MEFuelMassExtLb;
        // when adding properties note that this is a record type and we rely on == comparisons
    }
    public double LevelOffAltFt;

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

class StraightClimbTestGroup
{
    // computes things about a group of tests that differ only by level off altitude
    // things like whether we've found a good enough level off altitude, whether the test failed, and what the results are
    // this is necessary because instead of trying to somehow level off at exactly the target altitude, we search for where to end the climb by trial and error

    public double Angle { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool IsFailed { get; private set; }
    public double FuelUsedLb { get; private set; }
    public double ClimbDuration { get; private set; }
    public double ClimbDistance { get; private set; }
    public double LevelOffAlt { get; private set; }

    public StraightClimbTestGroup(double angle, IEnumerable<StraightClimbTest> tests)
    {
        Angle = angle;
        if (tests.Count() == 0) // we can't accept empty groups, hence we don't have to decide the best level off alt for an empty group here
            throw new InvalidOperationException();
        var cfg = tests.First().Config;
        var nearest = tests.Where(t => t.Result.FailReason == null).MinElementOrDefault(t => Math.Abs(t.Result.MaxAltitudeFt - cfg.FinalTargetAltitudeFt));
        if (nearest != null && Math.Abs(nearest.Result.MaxAltitudeFt - cfg.FinalTargetAltitudeFt) < 10)
        {
            IsCompleted = true;
            FuelUsedLb = nearest.Result.FuelUsedLb;
            ClimbDistance = nearest.Result.ClimbDistance;
            ClimbDuration = nearest.Result.ClimbDuration;
            // later: interpolate between two nearest? (but the accuracy improvement is minimal and we have to fly extra tests even when we have one really close result and also the logic for next level off altitude suggestion would have to be amended to aim slightly high or slightly low to ensure placing results both below and above the target)
            return;
        }

        if (tests.Any(t => t.Result.FailReason != null))
        {
            IsFailed = true;
            return;
        }

        // Otherwise it's neither completed nor failed, so just compute and set the suggested level-off altitude to try

        if (tests.Count() == 1)
        {
            LevelOffAlt = tests.First().LevelOffAltFt - (tests.First().Result.MaxAltitudeFt - cfg.FinalTargetAltitudeFt) * 1.2;
            return;
        }

        var nearestLo = tests.Where(t => t.Result.FailReason == null && t.Result.MaxAltitudeFt < cfg.FinalTargetAltitudeFt).MaxElementOrDefault(t => t.LevelOffAltFt);
        var nearestHi = tests.Where(t => t.Result.FailReason == null && t.Result.MaxAltitudeFt > cfg.FinalTargetAltitudeFt).MinElementOrDefault(t => t.LevelOffAltFt);

        // the level-offs could be really close but due to randomness still too far
        // we might have just one of the two
        if (nearestLo == null || nearestHi == null)
        {
            // can't interpolate but we have two points, so we'll have to extrapolate from the nearest two
            var nearest2 = tests.OrderBy(t => Math.Abs(t.Result.MaxAltitudeFt - cfg.FinalTargetAltitudeFt)).Take(2).ToList();
            nearestLo = nearest2[0]; // lo doesn't have to actually be less than hi, we don't care for the calc
            nearestHi = nearest2[1];
        }
        else if (Math.Abs(nearestHi.LevelOffAltFt - nearestLo.LevelOffAltFt) < 5)
        {
            // It could conceivably happen due to randomness such as the variable frame rate or unlucky lags that a really close result is not reproducible, thus breaking this logic below (which relies on the intermediate value theorem)
            // So just accept one of the results if our attempts are getting really close.
            Console.WriteLine($"Warning: level-off trials are really close without a successful result."); // we're not expecting this so let's see if it ever happens
            IsCompleted = true;
            nearest = tests.MinElement(t => Math.Abs(t.Result.MaxAltitudeFt - cfg.FinalTargetAltitudeFt)); // least code to see which of the two is nearer...
            FuelUsedLb = nearest.Result.FuelUsedLb;
            ClimbDistance = nearest.Result.ClimbDistance;
            ClimbDuration = nearest.Result.ClimbDuration;
            // later: could interpolate between the two
        }
        LevelOffAlt = nearestLo.LevelOffAltFt + (cfg.FinalTargetAltitudeFt - nearestLo.Result.MaxAltitudeFt) / (nearestHi.Result.MaxAltitudeFt - nearestLo.Result.MaxAltitudeFt) * (nearestHi.LevelOffAltFt - nearestLo.LevelOffAltFt);
    }
}
