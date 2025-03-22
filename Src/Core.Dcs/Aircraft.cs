using System.Text;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public class Aircraft
{
    public virtual string DcsId => "generic";
    public virtual bool SupportsSetSpeedBrakeRate => false;
    public virtual bool SupportsSetTrim => false;
    public virtual bool SupportsSetTrimRate => false;

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
        frame.FuelFlow = double.Parse(pkt.Entries["fufl"][0]);
        frame.AileronL = double.Parse(pkt.Entries["surf"][0]); frame.AileronR = double.Parse(pkt.Entries["surf"][1]);
        frame.ElevatorL = double.Parse(pkt.Entries["surf"][2]); frame.ElevatorR = double.Parse(pkt.Entries["surf"][3]);
        frame.RudderL = double.Parse(pkt.Entries["surf"][4]); frame.RudderR = double.Parse(pkt.Entries["surf"][5]);
        frame.Flaps = double.Parse(pkt.Entries["flap"][0]);
        frame.Airbrakes = double.Parse(pkt.Entries["airbrk"][0]);
        frame.LandingGear = double.Parse(pkt.Entries["lg"][0]);
        frame.WindX = double.Parse(pkt.Entries["wind"][0]); frame.WindY = double.Parse(pkt.Entries["wind"][1]); frame.WindZ = double.Parse(pkt.Entries["wind"][2]);
        //frame.JoyPitch = double.Parse(pkt.Entries["joyp"][0]); // WIP
        //frame.JoyRoll = double.Parse(pkt.Entries["joyr"][0]);
        //frame.JoyYaw = double.Parse(pkt.Entries["joyy"][0]);
        //frame.JoyThrottle1 = double.Parse(pkt.Entries["joyt1"][0]);
        //frame.JoyThrottle2 = double.Parse(pkt.Entries["joyt2"][0]);
        frame.TrimPitch = double.Parse(pkt.Entries["ptrm"][0]);
        frame.TrimRoll = double.Parse(pkt.Entries["rtrm"][0]);
        frame.TrimYaw = double.Parse(pkt.Entries["ytrm"][0]);
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
