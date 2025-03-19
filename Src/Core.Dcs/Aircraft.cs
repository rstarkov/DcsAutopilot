namespace DcsAutopilot;

public class Aircraft
{
    public virtual string DcsId => "generic";

    private IFilter _bankRateFilter = Filters.BesselD5;

    public virtual bool ProcessFrameEntry(FrameData fd, DataPacket.Entry e)
    {
        switch (e.Key)
        {
            case "pitch": fd.Pitch = double.Parse(e[0]).ToDeg(); break;
            case "bank": fd.Bank = double.Parse(e[0]).ToDeg(); break;
            case "hdg": fd.Heading = double.Parse(e[0]).ToDeg(); break;
            case "ang": fd.GyroRoll = double.Parse(e[0]).ToDeg(); fd.GyroYaw = -double.Parse(e[1]).ToDeg(); fd.GyroPitch = double.Parse(e[2]).ToDeg(); break;
            case "pos": fd.PosX = double.Parse(e[0]); fd.PosY = double.Parse(e[1]); fd.PosZ = double.Parse(e[2]); break;
            case "vel": fd.VelX = double.Parse(e[0]); fd.VelY = double.Parse(e[1]); fd.VelZ = double.Parse(e[2]); break;
            case "acc": fd.AccX = double.Parse(e[0]); fd.AccY = double.Parse(e[1]); fd.AccZ = double.Parse(e[2]); break;
            case "asl": fd.AltitudeAsl = double.Parse(e[0]); break;
            case "agl": fd.AltitudeAgl = double.Parse(e[0]); break;
            case "balt": fd.AltitudeBaro = double.Parse(e[0]); break;
            case "ralt": fd.AltitudeRadar = double.Parse(e[0]); break;
            case "vspd": fd.SpeedVertical = double.Parse(e[0]); break;
            case "tas": fd.SpeedTrue = double.Parse(e[0]); break;
            case "ias": fd.SpeedIndicatedBad = double.Parse(e[0]); break;
            case "mach": fd.SpeedMachBad = double.Parse(e[0]); break;
            case "aoa": fd.AngleOfAttack = double.Parse(e[0]); break;
            case "aoss": fd.AngleOfSideSlip = -double.Parse(e[0]); break;
            case "fuint": fd.FuelInternal = double.Parse(e[0]); break;
            case "fuext": fd.FuelExternal = double.Parse(e[0]); break;
            case "fufl": fd.FuelFlow = double.Parse(e[0]); break;
            case "surf":
                fd.AileronL = double.Parse(e[0]); fd.AileronR = double.Parse(e[1]);
                fd.ElevatorL = double.Parse(e[2]); fd.ElevatorR = double.Parse(e[3]);
                fd.RudderL = double.Parse(e[4]); fd.RudderR = double.Parse(e[5]);
                break;
            case "flap": fd.Flaps = double.Parse(e[0]); break;
            case "airbrk": fd.Airbrakes = double.Parse(e[0]); break;
            case "lg": fd.LandingGear = double.Parse(e[0]); break;
            case "wind": fd.WindX = double.Parse(e[0]); fd.WindY = double.Parse(e[1]); fd.WindZ = double.Parse(e[2]); break;
            case "joyp": fd.JoyPitch = double.Parse(e[0]); break;
            case "joyr": fd.JoyRoll = double.Parse(e[0]); break;
            case "joyy": fd.JoyYaw = double.Parse(e[0]); break;
            case "joyt1": fd.JoyThrottle1 = double.Parse(e[0]); break;
            case "joyt2": fd.JoyThrottle2 = double.Parse(e[0]); break;
            case "ptrm": fd.TrimPitch = double.Parse(e[0]); break;
            case "rtrm": fd.TrimRoll = double.Parse(e[0]); break;
            case "ytrm": fd.TrimYaw = double.Parse(e[0]); break;
            case "test1": fd.Test1 = double.Parse(e[0]); break;
            case "test2": fd.Test2 = double.Parse(e[0]); break;
            case "test3": fd.Test3 = double.Parse(e[0]); break;
            case "test4": fd.Test4 = double.Parse(e[0]); break;
            default:
                return false;
        }
        return true;
    }

    public virtual void ProcessFrame(FrameData frame, FrameData prevFrame)
    {
        frame.BankRate = _bankRateFilter.Step((frame.Bank - prevFrame.Bank) / frame.dT);
    }
}

public class ViperAircraft : Aircraft
{
    public override string DcsId => "F-16C_50";
}

public class HornetAircraft : Aircraft
{
    public override string DcsId => "FA-18C_hornet";
}
