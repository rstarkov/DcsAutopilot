﻿using System.Collections.Concurrent;
using DcsAutopilot;
using RT.Serialization;
using RT.Util;
using RT.Util.Consoles;
using RT.Util.ExtensionMethods;

namespace DcsExperiments;

static class ClimbPerfTests
{
    private static string LogPath;
    public static ConcurrentQueue<string> Log = new();
    private static List<StraightClimbTest> TestLogs = new();
    public static double FinalAltitudeRange = 10; // can edit; reducing will automatically retest only those scenarios that lie outside this range

    // this code is restartable; a test run can be interrupted at any point and resumed with no penalty
    // test ranges, scenarios, and acceptance criteria can be adjusted in the middle of a run; any reusable old test results will be reused automatically,
    // and only the minimum necessary additional tests will be executed

    public static void Run(string[] args)
    {
        LogPath = args[0];
        Console.CursorVisible = false;
        if (File.Exists(Path.Combine(LogPath, "tests.xml")))
            TestLogs = ClassifyXml.DeserializeFile<List<StraightClimbTest>>(Path.Combine(LogPath, "tests.xml"));
        GenResults();
        DeleteUnusedLogs();

        var scenarios = new[] { 1.5, 2.0, 1.8, 1.6, 1.9, 1.7 }.SelectMany(throttle => new[] { 300, 400, 200, 500, 250, 350, 450 }, (throttle, speed) => (throttle, speed)).ToList();
        foreach (var scenario in scenarios)
        {
            while (true)
            {
                // The aircraft and the weather are not recorded; different aircraft / weather conditions must be tested with different target folders. This includes QNH (which impacts performance)
                // The Mission Editor mass parameters MUST be entered (DCS won't tell us what these are, but ME can show them).
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
                    var lowestFailed = byAngle.Values.Where(g => g.IsFailed).MinOrDefault(g => g.Angle, 99);
                    var sweep = Enumerable.Range(1, 99).Where(a => a % 8 == 0 && a < lowestFailed);
                    config.ClimbAngle = sweep.FirstOrDefault(a => !byAngle.ContainsKey(a));
                    if (config.ClimbAngle == default)
                    {
                        // The sweep is done. Next we repeatedly subdivide the ranges on both sides of the optimum until there's a data point 1 degree either side of the optimum
                        double subdivAngle(double ang)
                        {
                            var doneLo = byAngle.Keys.Where(a => a < ang).MaxOrDefault(ang);
                            var doneHi = byAngle.Keys.Where(a => a > ang && a <= lowestFailed).MinOrDefault(ang);
                            var nextLo = Math.Ceiling((ang + doneLo) / 2);
                            var nextHi = Math.Floor((doneHi + ang) / 2);
                            if (nextHi - ang > ang - nextLo)
                                return nextHi;
                            if (nextLo < ang)
                                return nextLo;
                            if (nextHi != ang || nextLo != ang)
                                throw new Exception("wnacxm"); // just an assertion; can't happen?
                            return default;
                        }
                        var best = byAngle.Values.Where(g => g.IsCompleted).MinElement(g => g.FuelUsedLb).Angle;
                        config.ClimbAngle = subdivAngle(best);
                        if (config.ClimbAngle == default)
                        {
                            // The tests around the best value are done. Finally we subdivide the end of the range to find the actual max angle (this adds a lot of extra tests though)
                            if (lowestFailed == 99)
                                throw new Exception("afnlzn"); // later: we'll deal with planes that can climb to tgt altitude straight up when we run into this
                            //config.ClimbAngle = subdivAngle(lowestFailed);
                            //if (config.ClimbAngle == default)
                            {
                                // All done for this scenario!
                                break;
                            }
                        }
                    }

                    // Our initial guesstimate of the level off altitude for this setup, given a complete lack of any better information
                    levelOffAltFt = config.FinalTargetAltitudeFt - 3000; // it should be low enough that we're not risking the first try being too high while risking being wrongly marked as impossible
                    // later: we could actually interpolate based on nearby tests
                }

                // Print a few recent tests
                Console.Clear();
                Console.WriteLine($"Straight line climb to M{config.FinalTargetMach:0.0} @ {config.FinalTargetAltitudeFt:0}");
                Console.WriteLine();
                foreach (var (t, i) in TestLogs.Select((t, i) => (t, i)).TakeLast(9))
                {
                    var str = $"#{i + 1}: throttle={t.Config.Throttle:0.0} speed={t.Config.PreClimbSpeedKts:0} angle={t.Config.ClimbAngle,2:0} lvloff={t.LevelOffAltFt:0} --- ".Color(ConsoleColor.Gray);
                    if (t.Result.FailReason == null)
                        str += $"alt=" + $"{t.Result.MaxAltitudeFt:0}".Color(Math.Abs(t.Result.MaxAltitudeFt - t.Config.FinalTargetAltitudeFt) <= FinalAltitudeRange ? ConsoleColor.Green : ConsoleColor.White) + $" fuel={t.Result.FuelUsedLb:0} dur={t.Result.ClimbDuration:0} dist={t.Result.ClimbDistance:#,0}";
                    else
                        str += $"failed because: {t.Result.FailReason}".Color(ConsoleColor.Red);
                    ConsoleUtil.WriteLine(str);
                }

                // Run the flight test!
                DcsWindow.RestartMission(); // this returns right after the mission starts loading, and we then have a few seconds to initialise and be ready for the first frame's UDP data
                var testlog = DoFlightTest(config, levelOffAltFt);
                TestLogs.Add(testlog);
                ClassifyXml.SerializeToFile(TestLogs, Path.Combine(LogPath, "tests.xml"));
                GenResults();
            }
        }
    }

    static void GenResults()
    {
        var biggroups = TestLogs.GroupBy(t => (t.Config.FinalTargetAltitudeFt, t.Config.FinalTargetMach, t.Config.METotalMassLb));
        foreach (var bg in biggroups)
        {
            // all results unprocessed
            var allres = bg.GroupBy(t => t.Config).Select(g => (c: g.Key, r: new StraightClimbTestGroup(g.Key.ClimbAngle, g))).ToList();
            File.WriteAllLines(Path.Combine(LogPath, $"all-{bg.Key.FinalTargetAltitudeFt:0}-{bg.Key.FinalTargetMach}-{bg.Key.METotalMassLb}.csv"),
                allres.Select(x => Ut.FormatCsvRow(x.c.Throttle, x.c.PreClimbSpeedKts, x.c.ClimbAngle, x.r.LevelOffAlt, x.r.ClimbDuration, x.r.ClimbDistance, x.r.FuelUsedLb, x.r.IsFailed, x.r.IsCompleted)));

            string filename;
            void printCsvTableToFile<T>(IEnumerable<T> data, Func<T, double> getX, Func<T, double> getY, Func<T, double> getVal)
            {
                var xs = data.Select(getX).Distinct().Order();
                var ys = data.Select(getY).Distinct().Order();
                File.AppendAllLines(filename, new[] { Ut.FormatCsvRow("".Concat(xs.Select(s => $"{s:0.0}"))) });
                foreach (var y in ys)
                {
                    var row = new List<string>();
                    row.Add($"{y:0}");
                    foreach (var x in xs)
                    {
                        var match = data.Where(d => getX(d) == x && getY(d) == y);
                        row.Add(!match.Any() ? "" : $"{getVal(match.First()):0}");
                    }
                    File.AppendAllLines(filename, new[] { Ut.FormatCsvRow(row) });
                }
            }
            void makeOptBy(string filenamePrefix, Func<StraightClimbTestGroup, double> optimalBy, string byText)
            {
                filename = Path.Combine(LogPath, $"{filenamePrefix}-{bg.Key.FinalTargetAltitudeFt:0}-{bg.Key.FinalTargetMach}-{bg.Key.METotalMassLb}.csv");
                File.Delete(filename);
                var sg = allres.Where(x => x.r.IsCompleted).GroupBy(x => (x.c.Throttle, x.c.PreClimbSpeedKts)).Select(g => (g.Key.Throttle, g.Key.PreClimbSpeedKts, r: g.MinElement(x => optimalBy(x.r)))).ToList();
                File.AppendAllLines(filename, new[] { $"=== Optimal climb angle by {byText} ===" });
                printCsvTableToFile(sg, d => d.Throttle, d => d.PreClimbSpeedKts, d => d.r.c.ClimbAngle);
                File.AppendAllLines(filename, new[] { $"=== Fuel used ===" });
                printCsvTableToFile(sg, d => d.Throttle, d => d.PreClimbSpeedKts, d => d.r.r.FuelUsedLb);
                File.AppendAllLines(filename, new[] { $"=== Duration ===" });
                printCsvTableToFile(sg, d => d.Throttle, d => d.PreClimbSpeedKts, d => d.r.r.ClimbDuration);
                File.AppendAllLines(filename, new[] { $"=== Distance ===" });
                printCsvTableToFile(sg, d => d.Throttle, d => d.PreClimbSpeedKts, d => d.r.r.ClimbDistance);
            }

            // optimal climb angle by fuel usage
            makeOptBy("results-opt-fuel", g => g.FuelUsedLb, "fuel usage");
            makeOptBy("results-opt-dur", g => g.ClimbDuration, "climb duration");
            makeOptBy("results-opt-dist", g => g.ClimbDistance, "climb distance");

            // grouped by throttle: speed vs angle table of fuel used
            filename = Path.Combine(LogPath, $"results-{bg.Key.FinalTargetAltitudeFt:0}-{bg.Key.FinalTargetMach}-{bg.Key.METotalMassLb}.csv");
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
        bool seenFirstFrame = false;
        try
        {
            var prevTime = DateTime.UtcNow;
            var prevFrames = 0;
            while (true)
            {
                if (dcs.LastFrame?.FrameNum > 0 && !seenFirstFrame)
                {
                    seenFirstFrame = true;
                    DcsWindow.SpeedUp(); // this can Thread.Sleep but it's okay; nothing time-sensitive happens in this loop anyway
                }

                var overhead = DateTime.UtcNow - prevTime;
                Thread.Sleep(100);
                var lf = dcs.LastFrame;
                var lc = dcs.LastControl;
                var fps = ((lf?.FrameNum ?? 0) - prevFrames) / (DateTime.UtcNow - prevTime).TotalSeconds;
                prevTime = DateTime.UtcNow;
                prevFrames = lf?.FrameNum ?? 0;

                var fuelSoFar = ctrl.Test.Result.RawFuelAtStartInt == 0 ? 0 : (ctrl.Test.Result.RawFuelAtStartInt - lf?.FuelInternal) * ctrl.Test.Config.MEFuelMassIntLb + (ctrl.Test.Result.RawFuelAtStartExt - lf?.FuelExternal) * ctrl.Test.Config.MEFuelMassExtLb;
                ConsoleColoredString cyan(string v) => v.Color(ConsoleColor.Cyan);
                PrintLine(11, $"#{TestLogs.Count + 1}: throttle={cfg.Throttle:0.0} speed={cfg.PreClimbSpeedKts:0} angle={cfg.ClimbAngle,2:0} lvloff={levelOffAltFt:0} --- alt=" + cyan($"{ctrl.Test.Result.MaxAltitudeFt:0}") + " fuel=" + cyan($"{fuelSoFar:0}") + " dur=" + cyan($"{ctrl.Test.Result.ClimbDuration:0}") + " dist=" + cyan($"{ctrl.Test.Result.ClimbDistance:#,0}"));

                var sep = " --- ".Color(ConsoleColor.DarkGray);
                PrintLine(14, $"    {lf?.SimTime ?? 0:0.0}s" + sep + $"{overhead.TotalMilliseconds:00}ms overhead" + sep + $"{fps:0} ({ctrl.Test.EffectiveFps:0}) FPS" + sep + $"{lf?.Underflows ?? 0} u/f {lf?.Overflows ?? 0} o/f" + sep + $"{dcs.Status}");
                PrintLine(15, $"    Stage: {ctrl.Stage}   tgtpitch: {ctrl.TgtPitch:0.0}");

                ConsoleColoredString fmt(string num, string suff, int len, ConsoleColor numClr = ConsoleColor.White) => (num.Color(numClr) + suff.Color(ConsoleColor.DarkGray)).PadRight(len + suff.Length);
                PrintLine(18, "    Altitude         Speed           Pitch             Bank".Color(ConsoleColor.DarkGray));
                PrintLine(19, "    " + fmt($"{lf?.AltitudeAsl.MetersToFeet() ?? 0:#,0}", " ft", 6) + "        " + fmt($"{lf?.SpeedCalibrated.MsToKts() ?? 0:0.00}", " IAS", 6) + "      " + fmt($"{lf?.Pitch ?? 0:0.00}", "° bore", 6) + "      " + fmt($"{lf?.Bank ?? 0:0.00}", "°", 5));
                PrintLine(20, "    " + fmt($"{(lf?.SpeedVertical.MetersToFeet() ?? 0) * 60:#,0}", " ft/min", 6) + "    " + fmt($"{lf?.SpeedMach ?? 0:0.0000}", " Mach", 6) + "     " + fmt($"{lf?.VelPitch ?? 0:0.00}", "° vector", 5, ConsoleColor.Yellow) + "     " + fmt($"{lf?.Heading ?? 0:0.00}", "° hdg", 6));
                PrintLine(21, "                                     " + fmt($"{lf?.AngleOfAttack ?? 0:0.00}", "° AoA", 5));

                PrintLine(23, "    G-force          Throttle        Elevator          Aileron".Color(ConsoleColor.DarkGray));
                PrintLine(24, "    " + fmt($"{lf?.AccY ?? 0:0.00}", " vert", 5) + "       " + fmt($"{lc?.ThrottleAxis ?? 0:0.0000}", "", 6) + "          " + fmt($"{lc?.PitchAxis ?? 0:0.0000}", "", 7) + "           " + fmt($"{lc?.RollAxis ?? 0:0.0000}", "", 6));
                PrintLine(25, "    " + fmt($"{lf?.AccX ?? 0:0.00}", " horz", 5));

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

    static void PrintLine(int row, ConsoleColoredString line)
    {
        Console.SetCursorPosition(0, row);
        ConsoleUtil.Write(line.PadRight(Console.WindowWidth));
    }

    private static void DeleteUnusedLogs()
    {
        var logFiles = new DirectoryInfo(Path.Combine(LogPath, "csv")).GetFiles("*.csv");
        var unlinkedFiles = logFiles.Where(f => !TestLogs.Any(t => t.LogName == f.Name)).ToArray();
        if (unlinkedFiles.Length == 0)
            return;
        foreach (var f in unlinkedFiles)
            Console.WriteLine(f.Name);
        Console.WriteLine();
        Console.WriteLine($"Found {unlinkedFiles.Length} unused log files (above) out of {logFiles.Length} total files. Press Y to delete them now.");
        bool delete = Console.ReadKey().Key == ConsoleKey.Y;
        Console.WriteLine();
        if (delete)
        {
            foreach (var f in unlinkedFiles)
                f.Delete();
            Console.WriteLine("Deleted.");
        }
        Console.WriteLine();
    }
}

class StraightClimbTest
{
    public DateTime StartedUtc;
    public string LogName;
    public string DcsVersion;
    public int Underflows, Overflows;
    public double EffectiveFps;

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
        if (nearest != null && Math.Abs(nearest.Result.MaxAltitudeFt - cfg.FinalTargetAltitudeFt) <= ClimbPerfTests.FinalAltitudeRange)
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
            if (LevelOffAlt > cfg.FinalTargetAltitudeFt)
                LevelOffAlt = cfg.FinalTargetAltitudeFt - (tests.First().Result.MaxAltitudeFt - tests.First().LevelOffAltFt);
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
