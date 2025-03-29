using System.Text;
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
    ];

    public override void ProcessFrame(DataPacket pkt, FrameData frame, FrameData prevFrame)
    {
        base.ProcessFrame(pkt, frame, prevFrame);
        var fuelflow = pkt.Entries["fuel_flow"];
        frame.FuelFlow = 10000 * Math.Floor(10 * fuelflow[0].ParseDouble() + 0.5) + 1000 * Math.Floor(10 * fuelflow[1].ParseDouble() + 0.5) + 100 * 10 * fuelflow[2].ParseDouble();
        frame.TrimPitch = pkt.Entries["pitch_trim_pos"][0].ParseDouble();
        frame.TrimRoll = -pkt.Entries["roll_trim_pos"][0].ParseDouble();
        frame.TrimYaw = pkt.Entries["yaw_trim_pos"][0].ParseDouble();
        frame.LandingGear = 1 - pkt.Entries["landing_gear_lever"][0].ParseDouble();
        var dialAirspeed = 1000 * pkt.Entries["spd_dial_ias"][0].ParseDouble();
        frame.DialSpeedIndicated = dialAirspeed.KtsToMs();
        frame.DialSpeedCalibrated = frame.DialSpeedIndicated < 0.01 ? 0 : (dialAirspeed - DialAirspeedCalibration.Calc(dialAirspeed)).KtsToMs();
        var dialMach = pkt.Entries["spd_dial_mach"][0].ParseDouble();
        frame.DialSpeedMach = dialMach > 0.95792 ? 0.50 : dialMach > 0.9215 ? (13.4342 - 13.4976 * dialMach) : dialMach > 0.889 ? (1.88876 - 0.93113 * dialMach) : (3.7 - 2.95784 * dialMach);
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
