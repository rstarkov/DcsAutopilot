using System.Text;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public class Aircraft
{
    public virtual string DcsId => "generic";
    public virtual bool SupportsSetSpeedBrakeRate => false;
    public virtual bool SupportsSetTrim => false;
    public virtual bool SupportsSetTrimRate => false;
    public virtual IEnumerable<(string key, string[] req)> DataRequests => [];

    private IFilter _bankRateFilter = Filters.BesselD5;

    public virtual void ProcessBulk(DataPacket pkt, BulkData bulk)
    {
    }

    /// <param name="pkt">
    ///     Data packet to process.</param>
    /// <param name="frame">
    ///     Frame to populate with data received in this packet. For first frame, <c>dT</c> is zero.</param>
    /// <param name="prevFrame">
    ///     Previous frame. For first frame in a session it's <c>null</c>.</param>
    public virtual void ProcessFrame(DataPacket pkt, FrameData frame, FrameData prevFrame)
    {
        frame.Pitch = double.Parse(pkt.Entries["pitch"][0]).ToDeg();
        frame.Bank = double.Parse(pkt.Entries["bank"][0]).ToDeg();
        frame.Heading = double.Parse(pkt.Entries["hdg"][0]).ToDeg();
        frame.GyroRoll = double.Parse(pkt.Entries["ang"][0]).ToDeg(); frame.GyroYaw = -double.Parse(pkt.Entries["ang"][1]).ToDeg(); frame.GyroPitch = double.Parse(pkt.Entries["ang"][2]).ToDeg();
        frame.PosX = double.Parse(pkt.Entries["pos"][0]); frame.PosY = double.Parse(pkt.Entries["pos"][1]); frame.PosZ = double.Parse(pkt.Entries["pos"][2]);
        frame.VelX = double.Parse(pkt.Entries["vel"][0]); frame.VelY = double.Parse(pkt.Entries["vel"][1]); frame.VelZ = double.Parse(pkt.Entries["vel"][2]);
        frame.AccX = double.Parse(pkt.Entries["acc"][0]); frame.AccY = double.Parse(pkt.Entries["acc"][1]); frame.AccZ = double.Parse(pkt.Entries["acc"][2]);
        frame.AltitudeAsl = double.Parse(pkt.Entries["asl"][0]);
        frame.AltitudeAgl = double.Parse(pkt.Entries["agl"][0]);
        frame.AltitudeBaro = double.Parse(pkt.Entries["balt"][0]);
        frame.AltitudeRadar = double.Parse(pkt.Entries["ralt"][0]);
        frame.SpeedVertical = double.Parse(pkt.Entries["vspd"][0]);
        frame.SpeedTrue = double.Parse(pkt.Entries["tas"][0]);
        frame.SpeedIndicatedBad = double.Parse(pkt.Entries["ias"][0]);
        frame.SpeedMachBad = double.Parse(pkt.Entries["mach"][0]);
        frame.AngleOfAttack = double.Parse(pkt.Entries["aoa"][0]);
        frame.AngleOfSideSlip = -double.Parse(pkt.Entries["aoss"][0]);
        frame.FuelInternal = double.Parse(pkt.Entries["fuint"][0]);
        frame.FuelExternal = double.Parse(pkt.Entries["fuext"][0]);
        frame.AileronL = double.Parse(pkt.Entries["surf"][0]); frame.AileronR = double.Parse(pkt.Entries["surf"][1]);
        frame.ElevatorL = double.Parse(pkt.Entries["surf"][2]); frame.ElevatorR = double.Parse(pkt.Entries["surf"][3]);
        frame.RudderL = double.Parse(pkt.Entries["surf"][4]); frame.RudderR = double.Parse(pkt.Entries["surf"][5]);
        frame.Flaps = double.Parse(pkt.Entries["flap"][0]);
        frame.Airbrakes = double.Parse(pkt.Entries["airbrk"][0]);
        frame.WindX = double.Parse(pkt.Entries["wind"][0]); frame.WindY = double.Parse(pkt.Entries["wind"][1]); frame.WindZ = double.Parse(pkt.Entries["wind"][2]);
        if (pkt.Entries.TryGetValue("test1", out var e))
            frame.Test1 = double.Parse(e[0]);
        if (pkt.Entries.TryGetValue("test2", out e))
            frame.Test2 = double.Parse(e[0]);
        if (pkt.Entries.TryGetValue("test3", out e))
            frame.Test3 = double.Parse(e[0]);
        if (pkt.Entries.TryGetValue("test4", out e))
            frame.Test4 = double.Parse(e[0]);

        if (prevFrame != null)
        {
            frame.BankRate = _bankRateFilter.Step((frame.Bank - prevFrame.Bank) / frame.dT);
        }
    }

    public virtual void BuildControlPacket(ControlData ctrl, StringBuilder pkt)
    {
        if (ctrl.PitchAxis != null)
            pkt.Append($"2;sc;2001;{ctrl.PitchAxis.Value};");
        if (ctrl.RollAxis != null)
            pkt.Append($"2;sc;2002;{ctrl.RollAxis.Value};");
        if (ctrl.YawAxis != null)
            pkt.Append($"2;sc;2003;{ctrl.YawAxis.Value};");
        if (ctrl.ThrottleAxis != null)
            pkt.Append($"2;sc;2004;{1 - ctrl.ThrottleAxis.Value};");
    }
}



public class ViperAircraft : Aircraft
{
    public override string DcsId => "F-16C_50";
    public override bool SupportsSetSpeedBrakeRate => true;
    public override bool SupportsSetTrim => true;
    public override bool SupportsSetTrimRate => true;

    public static Curve DialAirspeedCalibration;
    public AtmosphericHelper AtmoHelper = new() { MinQual = 0.15, KeepMin = 0.60, KeepSteep = 4300, CasDelay = 0.28, MachDelay = 0.34, AltDelay = 0.36 };

    static ViperAircraft()
    {
        DialAirspeedCalibration = new Curve();
        DialAirspeedCalibration.Add(new SineCurveSeg(0.5, 34.8, -35.6, 12.37, -0.06033, -35.6));
        DialAirspeedCalibration.AddPolyline((34.8, -35.61), (42, -41.46), (95.2, -7.87), (152, -0.55), (182, 9.65), (199, -3.28), (255, 2.71), (303, 0.77), (355, 2.86), (402, 0.09), (455, 3.39), (504.8, 3.34), (553.8, 3.35),
            (573.3, 3.27), (592.7, 3.11), (612.7, 2.74), (622, 2.52), (632.5, 2.08), (641.8, 1.52), (651, 0.14), (670.6, -9.91), (695.5, 0.69), (707.4, 6.27), (716.4, 4.97), (725.8, 4.3), (750, 3.13), (796, -4.93), (820, -6.52));
    }

    public override IEnumerable<(string key, string[] req)> DataRequests => [
        ("fuel_flow", ["deva;0;88;", "deva;0;89;", "deva;0;90;"]),
        ("pitch_trim_pos", ["deva;0;562;"]),
        ("roll_trim_pos", ["deva;0;560;"]),
        ("yaw_trim_pos", ["deva;0;565;"]),
        ("landing_gear_lever", ["deva;0;362;"]),
        ("spd_dial_ias", ["deva;0;48;"]),
        ("spd_dial_mach", ["deva;0;49;"]),
        ("baro_alt", ["deva;0;52;", "deva;0;53;", "deva;0;54;"]), // also "deva;0;51;" is the big dial but it matches "deva;0;54;" to like 8 d.p.
        ("baro_qnh", ["deva;0;56;", "deva;0;57;", "deva;0;58;", "deva;0;59;"]),
    ];

    public override void ProcessFrame(DataPacket pkt, FrameData frame, FrameData prevFrame)
    {
        base.ProcessFrame(pkt, frame, prevFrame);
        var fuelflow = pkt.Entries["fuel_flow"];
        frame.FuelFlow = 100 * Util.ReadDrums(10 * fuelflow[2].ParseDouble(), 10 * fuelflow[1].ParseDouble(), 10 * fuelflow[0].ParseDouble());
        frame.TrimPitch = pkt.Entries["pitch_trim_pos"][0].ParseDouble();
        frame.TrimRoll = -pkt.Entries["roll_trim_pos"][0].ParseDouble();
        frame.TrimYaw = pkt.Entries["yaw_trim_pos"][0].ParseDouble();
        frame.LandingGear = 1 - pkt.Entries["landing_gear_lever"][0].ParseDouble();
        var dialAirspeed = 1000 * pkt.Entries["spd_dial_ias"][0].ParseDouble();
        frame.DialSpeedIndicated = dialAirspeed.KtsToMs();
        frame.DialSpeedCalibrated = frame.DialSpeedIndicated < 0.01 ? 0 : (dialAirspeed - DialAirspeedCalibration.Calc(dialAirspeed)).KtsToMs();
        var dialMach = pkt.Entries["spd_dial_mach"][0].ParseDouble();
        frame.DialSpeedMach = dialMach > 0.95792 ? 0.50 : dialMach > 0.9215 ? (13.4342 - 13.4976 * dialMach) : dialMach > 0.889 ? (1.88876 - 0.93113 * dialMach) : (3.7 - 2.95784 * dialMach);
        var baroQnh = pkt.Entries["baro_qnh"];
        var qnhHg = 0.01 * Util.ReadDrums(baroQnh[3].ParseDouble() * 10, baroQnh[2].ParseDouble() * 10, baroQnh[1].ParseDouble() * 10, baroQnh[0].ParseDouble() * 10);
        frame.DialQnh = qnhHg.InHgToPa();
        var baroAlt = pkt.Entries["baro_alt"];
        var altFt = 100.0 * Util.ReadDrums(baroAlt[2].ParseDouble() * 10, baroAlt[1].ParseDouble() * 10, baroAlt[0].ParseDouble() * 10);
        frame.DialAltitudeBaro = altFt.FeetToMeters();

        // Notes on Viper airspeed and altitude:
        // DCS gives us SpeedIndicated and SpeedMach, but they are computed for ISA conditions and do not match the dial readings.
        // The theory is that DCS Viper FM computes its own, accurate airspeed with true atmospheric conditions, and its flight characteristics are based on that airspeed.
        // Lua gets no access to that airspeed. Lua can read the IAS dial, but that differs to the CAS displayed in the HUD. To get the HUD indication, we read the IAS dial
        // and apply a calibration curve. This gives us a value that is "realistic", but it is delayed and its resolution is limited. We can then obtain a cheaty real-time high-res
        // CAS value by estimating atmospheric parameters and calculating a CAS from the cheaty true airspeed (which is absolutely exact).
        // The situation is similar with altitude, except Lua doesn't even give us a "bad" barometric altitude, just a zero, but we can still read the cockpit dials.
        // That value, along with QNH setting and a cheaty true exact altitude, give us another data point for atmospheric conditions. This dial also appears to be delayed.
        AtmoHelper.Update(new()
        {
            Time = frame.SimTime, SpeedTrue = frame.SpeedTrue, AltTrue = frame.AltitudeAsl,
            DialCas = frame.DialSpeedCalibrated, DialMach = frame.DialSpeedMach, DialAlt = frame.DialAltitudeBaro, DialQnh = frame.DialQnh,
        });
        frame.SeaLevelTemp = AtmoHelper.EstSeaLevelTemp - 273.15;
        frame.SeaLevelPress = AtmoHelper.EstSeaLevelPress;
    }

    private ThreeWayButtonRateHelper _pitchTrimRateCtrl = new("16;3002;3003");
    private ThreeWayButtonRateHelper _rollTrimRateCtrl = new("16;3004;3005");

    public override void BuildControlPacket(ControlData ctrl, StringBuilder pkt)
    {
        base.BuildControlPacket(ctrl, pkt);
        if (ctrl.SpeedBrakeRate != null)
            pkt.Append($"3;pca;16;3031;{-ctrl.SpeedBrakeRate};"); // 1=retract, -1=extend; 16 from devices.lua, 3031 from command_defs.lua
        if (ctrl.PitchTrim != null)
            pkt.Append($"3;pca;2;3008;{ctrl.PitchTrim.Value};");
        if (ctrl.RollTrim != null)
            pkt.Append($"3;pca;2;3007;{-ctrl.RollTrim.Value};");
        if (ctrl.YawTrim != null)
            pkt.Append($"3;pca;2;3009;{ctrl.YawTrim.Value};");
        _pitchTrimRateCtrl.AppendControl(ctrl.PitchTrimRate, pkt);
        _rollTrimRateCtrl.AppendControl(ctrl.RollTrimRate, pkt);
    }
}



public class HornetAircraft : Aircraft
{
    public override string DcsId => "FA-18C_hornet";
    public override bool SupportsSetSpeedBrakeRate => true;
    public override bool SupportsSetTrimRate => true;

    public override IEnumerable<(string key, string[] req)> DataRequests => [
        ("ctrl_pitch", ["deva;0;71;"]),
        ("ctrl_roll", ["deva;0;74;"]),
        ("ctrl_yaw", ["deva;0;500;"]),
        ("ctrl_throttle", ["deva;0;104;", "deva;0;105;"]),
        ("yaw_trim_pos", ["deva;0;345;"]),
        ("landing_gear_lever", ["deva;0;226;"]),
        ("landing_gear_ext", ["adrw;115;", "adrw;0;"]),
    ];

    public override void ProcessFrame(DataPacket pkt, FrameData frame, FrameData prevFrame)
    {
        base.ProcessFrame(pkt, frame, prevFrame);
        frame.JoyPitch = pkt.Entries["ctrl_pitch"][0].ParseDouble();
        frame.JoyRoll = pkt.Entries["ctrl_roll"][0].ParseDouble();
        frame.JoyYaw = pkt.Entries["ctrl_yaw"][0].ParseDouble();
        var ctrl_throttle = pkt.Entries["ctrl_throttle"];
        frame.JoyThrottle1 = ctrl_throttle[0].ParseDouble();
        frame.JoyThrottle2 = ctrl_throttle[1].ParseDouble();
        frame.TrimYaw = pkt.Entries["yaw_trim_pos"][0].ParseDouble();
        frame.LandingGear = 1 - pkt.Entries["landing_gear_lever"][0].ParseDouble();
        var landing_gear_ext = pkt.Entries["landing_gear_ext"];
        //frame.ExtLandingGear = (landing_gear_ext[0].ParseDouble() + landing_gear_ext[1].ParseDouble()) / 2;
    }

    private ThreeWayButtonRateHelper _pitchTrimRateCtrl = new("13;3015;3014");
    private ThreeWayButtonRateHelper _rollTrimRateCtrl = new("13;3016;3017");

    public override void BuildControlPacket(ControlData ctrl, StringBuilder pkt)
    {
        base.BuildControlPacket(ctrl, pkt);
        if (ctrl.SpeedBrakeRate != null)
            pkt.Append($"3;pca;13;3035;{-ctrl.SpeedBrakeRate};"); // 1=retract, -1=extend // 13 from devices.lua, 3035 from command_defs.lua
        _pitchTrimRateCtrl.AppendControl(ctrl.PitchTrimRate, pkt);
        _rollTrimRateCtrl.AppendControl(ctrl.RollTrimRate, pkt);
    }
}



public class ThreeWayButtonRateHelper(string _pca3wConfig)
{
    private double _counter = 0;

    public void AppendControl(double? rate, StringBuilder pkt)
    {
        _counter += (rate ?? 0).Clip(-0.95, 0.95); // 0.95 max to ensure that we occasionally release and press the button again (fixes it getting stuck occasionally...)
        if (_counter >= 0.5)
        {
            pkt.Append($"4;pca3w;{_pca3wConfig};1;");
            _counter -= 1.0;
        }
        else if (_counter < -0.5)
        {
            pkt.Append($"4;pca3w;{_pca3wConfig};-1;");
            _counter += 1.0;
        }
        else
            pkt.Append($"4;pca3w;{_pca3wConfig};0;");
    }
}



public class AtmosphericHelper
{
    public double MinQual;
    public double KeepMin;
    public double KeepSteep;
    public double CasDelay;
    public double MachDelay;
    public double AltDelay;

    // It is theoretically possible to just compute the sea level temperature from mach dial, and then sea level pressure from CAS or Alt (which should match)
    // However, our dial readings aren't super precise, so this code attempts to continually refine both estimates by finding a best fit for all three dials

    public const double IsaLapse = 0.0065;
    public const double IsaSeaTemp = 288.15;
    public const double IsaSeaPress = 101325;
    public const double IsaSGC = 287.05287;
    public const double IsaSeaSpeedOfSound = 340.294; // Math.Sqrt(isaSeaTemp * 1.4 * isaSGC);
    public const double IsaAltConst = IsaSeaTemp / IsaLapse;
    public const double IsaBaroPow = 9.80665 / (IsaLapse * IsaSGC);
    // We don't know what DCS uses for these, so just use the best reasonable precision

    public static (double seaLevelTemp, double seaLevelPress, double err, double steps)
        FitAtmoToDials(double speedTrue, double altitudeTrue, double qnhDialPa, double dialCAS, double dialMach, double dialAlt, double guessTemp, double guessPress)
    {
        // Most of the time we are near the solution, so that's the case we optimise for. If we're far, the steps grow exponentially well enough.
        // The error function is very smooth and looks to be well-behaved, easily suitable for binary-search-like on alternating axes.
        const double stopT = 0.01;
        const double stopP = 2;
        const double minT = 273.15 - 30;
        const double maxT = 273.15 + 50;
        const double minP = 96000 - 3000; // 28.35
        const double maxP = 106000 + 3000; // 31.30
        var dirT = 1;
        var dirP = 0;
        var curT = guessTemp;
        var curP = guessPress;
        var curErr = double.NaN;
        int steps = 0;
        // Find a starting point; our initial guess may give us NaN
        while (true)
        {
            steps++;
            var r = CalcDials(speedTrue, altitudeTrue, qnhDialPa, curT, curP);
            curErr = Math.Abs(r.cas - dialCAS) + 440 * Math.Abs(r.mach - dialMach) + 0.025 * Math.Abs(r.alt - dialAlt);
            if (!double.IsNaN(curErr) && !double.IsInfinity(curErr))
                break;
            if (steps > 20) // give up
                return (curT.Clip(minT, maxT), curP.Clip(minP, maxP), err: 99999, steps);
            curT = Random.Shared.NextDouble(minT, maxT);
            curP = Random.Shared.NextDouble(minP, maxP);
        }
        // Binary-ish search
        int noChange = 0;
        while (true)
        {
            // Search along current dirT/dirP
            var stepT = 0.05;
            var stepP = 25.0;
            var mul = 2.0;
            var wasT = curT;
            var wasP = curP;
            var wasErr = curErr;
            while (true)
            {
                curT += dirT * stepT;
                curP += dirP * stepP;
                steps++;
                var r = CalcDials(speedTrue, altitudeTrue, qnhDialPa, curT.Clip(minT, maxT), curP.Clip(minP, maxP));
                var newErr = Math.Abs(r.cas - dialCAS) + 440 * Math.Abs(r.mach - dialMach) + 0.025 * Math.Abs(r.alt - dialAlt); // approx equal scale for a given error in T+P
                if (double.IsNaN(newErr) || double.IsInfinity(newErr))
                    break;
                if (newErr > curErr)
                {
                    mul = 0.5;
                    stepT = -stepT;
                    stepP = -stepP;
                }
                curErr = newErr; // even if it's worse - normal seek relies on that. On final step we could step back once if this was worse, but not really worth
                if (Math.Abs(stepT) < stopT || Math.Abs(stepP) < stopP)
                    break;
                stepT *= mul;
                stepP *= mul;
            }
            // Did we make progress?
            if (curErr < wasErr)
                noChange = 0; // yes
            else
            {
                noChange++; // no
                curT = wasT;
                curP = wasP;
                curErr = wasErr;
                if (noChange >= 4) // we've tried all four directions and found nothing better
                    break;
            }
            // Next direction
            if (dirT == 1) { dirT = 0; dirP = 1; }
            else if (dirP == 1) { dirT = -1; dirP = 0; }
            else if (dirT == -1) { dirT = 0; dirP = -1; }
            else if (dirP == -1) { dirT = 1; dirP = 0; }
        }
        return (curT.Clip(minT, maxT), curP.Clip(minP, maxP), curErr, steps);
    }

    public static (double cas, double mach, double alt) CalcDials(double speedTrue, double altitudeTrue, double qnhDialPa, double seaLevelTemp, double seaLevelPress)
    {
        var outsideAirTemp = seaLevelTemp - IsaLapse * altitudeTrue;
        var outsideAirPress = seaLevelPress * Math.Pow(outsideAirTemp / seaLevelTemp, IsaBaroPow);
        var speedOfSound = Math.Sqrt(outsideAirTemp * 1.4 * IsaSGC);
        var mach = speedTrue / speedOfSound;
        var cas = IsaSeaSpeedOfSound * Math.Sqrt(5 * (Math.Pow(outsideAirPress * (Math.Pow(1 + 0.2 * mach * mach, 7.0 / 2.0) - 1) / IsaSeaPress + 1, 2.0 / 7.0) - 1));
        var alt = IsaAltConst * (1 - Math.Pow(outsideAirPress / qnhDialPa, 1 / IsaBaroPow));
        return (cas, mach, alt);
    }

    public class Pt
    {
        public double Time;
        public double SpeedTrue;
        public double AltTrue;
        public double DialQnh;
        public double DialCas;
        public double DialMach;
        public double DialAlt;
    }

    private QueueViewable<Pt> _recent = [];
    private double _lastUpdated;

    public double EstSeaLevelTemp = IsaSeaTemp;
    public double EstSeaLevelPress = IsaSeaPress;
    public double EstQuality = 1.0; // 0 is perfect, 1 is worst possible
    public double SinceLastUpdate = 99;

    public void Update(Pt pt)
    {
        _recent.Enqueue(pt);
        var trueDelay = Math.Max(CasDelay, Math.Max(MachDelay, AltDelay));
        while (_recent.Count >= 3 && _recent[1].Time < pt.Time - trueDelay)
            _recent.Dequeue();
        if (_recent.Count == 1)
            return;
        double speedTrue = double.NaN;
        double altTrue = double.NaN;
        double dialCas = double.NaN;
        double dialMach = double.NaN;
        double dialAlt = double.NaN;
        double dialQnh = double.NaN;
        double timeTrue = pt.Time - trueDelay;
        double timeCas = pt.Time - trueDelay + CasDelay;
        double timeMach = pt.Time - trueDelay + MachDelay;
        double timeAlt = pt.Time - trueDelay + AltDelay;
        for (int i = 1; i < _recent.Count; i++)
        {
            if (double.IsNaN(speedTrue) && timeTrue > _recent[i - 1].Time && timeTrue <= _recent[i].Time)
            {
                speedTrue = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].SpeedTrue, _recent[i].SpeedTrue, timeTrue);
                altTrue = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].AltTrue, _recent[i].AltTrue, timeTrue);
            }
            if (double.IsNaN(dialAlt) && timeAlt > _recent[i - 1].Time && timeAlt <= _recent[i].Time)
            {
                dialAlt = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].DialAlt, _recent[i].DialAlt, timeAlt);
                dialQnh = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].DialQnh, _recent[i].DialQnh, timeAlt);
            }
            if (double.IsNaN(dialCas) && timeCas > _recent[i - 1].Time && timeCas <= _recent[i].Time)
                dialCas = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].DialCas, _recent[i].DialCas, timeCas);
            if (double.IsNaN(dialMach) && timeMach > _recent[i - 1].Time && timeMach <= _recent[i].Time)
                dialMach = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].DialMach, _recent[i].DialMach, timeMach);
        }

        var fit = FitAtmoToDials(speedTrue, altTrue, dialQnh, dialCas, dialMach, dialAlt, EstSeaLevelTemp, EstSeaLevelPress);

        if (EstQuality > MinQual)
        {
            // above MinQual we just keep the best fit we've ever seen
            if (fit.err < EstQuality)
            {
                EstQuality = fit.err;
                EstSeaLevelTemp = fit.seaLevelTemp;
                EstSeaLevelPress = fit.seaLevelPress;
                _lastUpdated = pt.Time;
            }
        }
        else
        {
            // once that is achieved, we ignore all fits with worse fit than Min, and use better fits to update the estimate - more so the better the fit
            if (fit.err <= MinQual)
            {
                var c = 1 / (1 - 1 / (MinQual * KeepSteep + 1 / KeepMin));
                var keep = c - c / (KeepSteep * fit.err + 1 / KeepMin);
                EstSeaLevelTemp = keep * EstSeaLevelTemp + (1 - keep) * fit.seaLevelTemp;
                EstSeaLevelPress = keep * EstSeaLevelPress + (1 - keep) * fit.seaLevelPress;
                EstQuality = keep * EstQuality + (1 - keep) * fit.err;
                _lastUpdated = pt.Time;
            }
        }
        SinceLastUpdate = pt.Time - _lastUpdated;
    }
}
