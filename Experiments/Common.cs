using DcsAutopilot;
using RT.Util.ExtensionMethods;

namespace DcsExperiments;

abstract class MultiTester
{
    protected DcsController _dcs;
    protected TunePidController _ctrl;

    protected void DefaultPids()
    {
        _ctrl.PidSpeedIndicated = new BasicPid { MinControl = 0, MaxControl = 1.6, IntegrationLimit = 1 /*m/s / sec*/ }.SetZiNiNone(2.0, 2.1);
        _ctrl.PidBank = new BasicPid { MinControl = -1, MaxControl = 1, IntegrationLimit = 5 /*deg/sec*/, DerivativeSmoothing = 0 }.SetZiNiNone(0.05, 3);
        _ctrl.PidVelPitch = new BasicPid { MinControl = -0.5, MaxControl = 0.3, IntegrationLimit = 0.1 /*deg/sec*/, DerivativeSmoothing = 0 }.SetZiNiNone(0.20, 2.45);
        _ctrl.PidYawSideslip = new BasicPid { MinControl = -1, MaxControl = 1, IntegrationLimit = 0.1 /*deg/sec*/ }.SetZiNiNone(0.7, 1.28);
        _ctrl.FilterDt = Filters.BesselD20;
        _ctrl.FilterPitch = Filters.BesselD5;
        _ctrl.FilterBank = Filters.BesselD5;
        _ctrl.FilterYaw = Filters.BesselD5;
        _ctrl.FilterSpeed = Filters.BesselD5;
    }

    protected void Restart()
    {
        _dcs?.Stop();
        DcsWindow.RestartMission();
        _ctrl = new();
        DefaultPids();
        _dcs = new();
        _dcs.FlightControllers.Add(_ctrl);
        _dcs.Start();
        while (_dcs.Status != "Active control" || _dcs.LastFrameUtc < DateTime.UtcNow.AddMilliseconds(-50))
            Thread.Sleep(100);
        for (int i = 1; i < SpeedUp; i++)
            DcsWindow.SpeedUp();
    }

    protected int SpeedUp = 2;
    protected double InitialMinFuel = 0.8;
    protected double InitialAltitude = 10_000; // feet ASL
    protected double InitialAltitudeError = 200; // feet
    protected double InitialSpeed = 300; // kts IAS
    protected double InitialSpeedError = 1; // kts IAS
    protected double InitialSpeedErrorRate = 0.1;
    protected double InitialPitchError = 0.1;
    protected double InitialPitchErrorRate = 0.05;
    protected double InitialRollError = 0.1;
    protected double InitialRollErrorRate = 0.05;
    protected double InitialYawError = 0.1;
    protected double InitialYawErrorRate = 0.05;

    protected void InitialConditions()
    {
        Console.CursorVisible = false;
        Console.Write("Initial conditions... ");
        var w = 22;
        if (_dcs == null || _dcs.LastFrame == null || _dcs.LastFrame.FuelInternal < InitialMinFuel || (DateTime.UtcNow - _dcs.LastFrameUtc).TotalSeconds > 5)
            Restart();
        again:;
        DefaultPids();
        _ctrl.TgtPitch = 0;
        _ctrl.TgtRoll = 0;
        _ctrl.TgtSpeed = InitialSpeed.KtsToMs();
        var unstable = new List<string>();
        while (true)
        {
            Thread.Sleep(100);
            if (_dcs.LastFrame.FuelInternal < InitialMinFuel)
            {
                Restart();
                goto again;
            }
            var altError = InitialAltitude - _dcs.LastFrame.AltitudeAsl.MetersToFeet();
            _ctrl.TgtPitch = Math.Abs(altError) < InitialAltitudeError ? 0 : (0.01 * altError).Clip(-20, 20);
            int s(double error, double limit, string name)
            {
                if (Math.Abs(error) <= limit) return 0;
                unstable.Add(name);
                return 1;
            }
            unstable.Clear();
            if (0 == s(altError, InitialAltitudeError, "altitude")
                + s(_ctrl.ErrSpeed, InitialSpeedError.KtsToMs(), "speed") + s(_ctrl.ErrRateSpeed, InitialSpeedErrorRate.KtsToMs(), "speed-rate")
                + s(_ctrl.ErrPitch, InitialPitchError, "pitch") + s(_ctrl.ErrRatePitch, InitialPitchErrorRate, "pitch-rate")
                + s(_ctrl.ErrRoll, InitialRollError, "roll") + s(_ctrl.ErrRateRoll, InitialRollErrorRate, "roll-rate")
                + s(_ctrl.ErrYaw, InitialYawError, "yaw") + s(_ctrl.ErrRateYaw, InitialYawErrorRate, "yaw-rate"))
                break;
            Console.CursorLeft = w;
            Console.Write(unstable.JoinString(", ").PadRight(Console.WindowWidth - w));
        }
        Console.CursorLeft = 0;
        string n(double v) => Math.Abs(v).Rounded(3);
        Console.WriteLine($"Initial conditions done: speed={n(_ctrl.ErrSpeed)}/{n(_ctrl.ErrRateSpeed)}, roll={n(_ctrl.ErrRoll)}/{n(_ctrl.ErrRateRoll)}, pitch={n(_ctrl.ErrPitch)}/{n(_ctrl.ErrRatePitch)}".PadRight(Console.WindowWidth));
        Console.CursorVisible = true;
    }
}
