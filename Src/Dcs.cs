using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public interface IFlightController
{
    bool Enabled { get; set; }
    void NewSession(BulkData bulk);
    ControlData ProcessFrame(FrameData frame); // can return null
    void ProcessBulkUpdate(BulkData bulk);
    string Status { get; }
}

public class DcsController
{
    private UdpClient _udp;
    private IPEndPoint _endpoint;
    private CancellationTokenSource _cts;
    private Thread _thread;
    private double _session;
    private ConcurrentQueue<double> _latencies = new();

    public List<IFlightController> FlightControllers { get; private set; } = new();
    public int Port { get; private set; }
    public ConcurrentBag<string> Warnings { get; private set; } = new(); // client can remove seen warnings but they may get re-added on next occurrence
    public byte[] LastReceiveWithWarnings { get; private set; }

    public bool IsRunning { get; private set; } = false;
    public string Status { get; private set; } = "Stopped";
    public int Frames { get; private set; } = 0;
    public int Skips { get; private set; } = 0;
    public int LastFrameBytes { get; private set; }
    public DateTime LastFrameUtc { get; private set; }
    public IEnumerable<double> Latencies => _latencies;
    public FrameData LastFrame { get; private set; }
    public BulkData LastBulk { get; private set; }
    public ControlData LastControl { get; private set; }

    public void Start(int udpPort = 9876)
    {
        if (IsRunning)
            throw new InvalidOperationException("Controller is already running.");
        Port = udpPort;
        _udp = new UdpClient(Port, AddressFamily.InterNetwork);
        _endpoint = new IPEndPoint(IPAddress.Any, 0);
        _cts = new CancellationTokenSource();
        Warnings.Clear();
        _thread = new Thread(() => thread(_cts.Token)) { IsBackground = true };
        _thread.Start();
        IsRunning = true;
        Status = "Waiting for data from DCS";
    }

    public void Stop()
    {
        if (!IsRunning)
            return;
        _cts.Cancel();
        _thread.Join();

        _udp.Close();
        _udp = null;
        _endpoint = null;
        _cts = null;
        _thread = null;
        IsRunning = false;
        Status = "Stopped";
        Frames = 0;
        Skips = 0;
        LastFrameUtc = DateTime.MinValue;
        LastFrame = null;
        LastBulk = null;
        LastControl = null;
        _latencies.Clear();
    }

    private void thread(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var task = _udp.ReceiveAsync(token).AsTask();
            task.ContinueWith(t => { }).Wait(); // the only way to wait that doesn't throw on cancellation?
            if (task.IsCanceled || token.IsCancellationRequested)
                return;
            var bytes = task.Result.Buffer;
            _endpoint = task.Result.RemoteEndPoint;
            FrameData parsedFrame = null;
            BulkData parsedBulk = null;
            try
            {
                var data = bytes.FromUtf8().Split(";");
                if (data[0] == "frame")
                {
                    var fd = new FrameData();
                    for (int i = 1; i < data.Length;)
                        switch (data[i++])
                        {
                            case "sess": fd.Session = double.Parse(data[i++]); break;
                            case "fr": fd.Frame = int.Parse(data[i++]); break;
                            case "skips": fd.Skips = int.Parse(data[i++]); break;
                            case "time": fd.SimTime = double.Parse(data[i++]); break;
                            case "sent": fd.FrameTimestamp = double.Parse(data[i++]); break;
                            case "ltcy": fd.Latency = double.Parse(data[i++]); break;
                            case "exp": fd.ExportAllowed = data[i++] == "true"; break;
                            case "pitch": fd.Pitch = double.Parse(data[i++]).ToDeg(); break;
                            case "bank": fd.Bank = double.Parse(data[i++]).ToDeg(); break;
                            case "hdg": fd.Heading = double.Parse(data[i++]).ToDeg(); break;
                            case "ang": fd.GyroRoll = double.Parse(data[i++]).ToDeg(); fd.GyroYaw = -double.Parse(data[i++]).ToDeg(); fd.GyroPitch = double.Parse(data[i++]).ToDeg(); break;
                            case "pos": fd.PosX = double.Parse(data[i++]); fd.PosY = double.Parse(data[i++]); fd.PosZ = double.Parse(data[i++]); break;
                            case "vel": fd.VelX = double.Parse(data[i++]); fd.VelY = double.Parse(data[i++]); fd.VelZ = double.Parse(data[i++]); break;
                            case "acc": fd.AccX = double.Parse(data[i++]); fd.AccY = double.Parse(data[i++]); fd.AccZ = double.Parse(data[i++]); break;
                            case "asl": fd.AltitudeAsl = double.Parse(data[i++]); break;
                            case "agl": fd.AltitudeAgl = double.Parse(data[i++]); break;
                            case "balt": fd.AltitudeBaro = double.Parse(data[i++]); break;
                            case "ralt": fd.AltitudeRadar = double.Parse(data[i++]); break;
                            case "vspd": fd.SpeedVertical = double.Parse(data[i++]); break;
                            case "tas": fd.SpeedTrue = double.Parse(data[i++]); break;
                            case "ias": fd.SpeedIndicated = double.Parse(data[i++]); break;
                            case "mach": fd.SpeedMach = double.Parse(data[i++]); break;
                            case "aoa": fd.AngleOfAttack = double.Parse(data[i++]); break;
                            case "aoss": fd.AngleOfSideSlip = -double.Parse(data[i++]); break;
                            case "fuint": fd.FuelInternal = double.Parse(data[i++]); break;
                            case "fuext": fd.FuelExternal = double.Parse(data[i++]); break;
                            case "surf":
                                fd.AileronL = double.Parse(data[i++]); fd.AileronR = double.Parse(data[i++]);
                                fd.ElevatorL = double.Parse(data[i++]); fd.ElevatorR = double.Parse(data[i++]);
                                fd.RudderL = double.Parse(data[i++]); fd.RudderR = double.Parse(data[i++]);
                                break;
                            case "flap": fd.Flaps = double.Parse(data[i++]); break;
                            case "airbrk": fd.Airbrakes = double.Parse(data[i++]); break;
                            case "wind": fd.WindX = double.Parse(data[i++]); fd.WindY = double.Parse(data[i++]); fd.WindZ = double.Parse(data[i++]); break;
                            case "joyp": fd.JoyPitch = double.Parse(data[i++]); break;
                            case "joyr": fd.JoyRoll = double.Parse(data[i++]); break;
                            case "joyy": fd.JoyYaw = double.Parse(data[i++]); break;
                            case "joyt1": fd.JoyThrottle1 = double.Parse(data[i++]); break;
                            case "joyt2": fd.JoyThrottle2 = double.Parse(data[i++]); break;
                            default:
                                if (Warnings.Count > 100) Warnings.Clear(); // some warnings change all the time; ugly but good enough fix for that
                                Warnings.Add($"Unrecognized frame data entry: \"{data[i - 1]}\"");
                                LastReceiveWithWarnings = bytes;
                                goto exitloop; // can't continue parsing because we don't know how long this entry is
                        }
                    exitloop:;
                    parsedFrame = fd;
                }
                else if (data[0] == "bulk")
                {
                    var bd = new BulkData();
                    for (int i = 1; i < data.Length;)
                        switch (data[i++])
                        {
                            case "sess": bd.Session = double.Parse(data[i++]); break;
                            case "exp": bd.ExportAllowed = data[i++] == "true"; break;
                            case "aircraft": bd.Aircraft = data[i++]; break;
                            case "ver": bd.DcsVersion = data[i++]; break;
                            default:
                                Warnings.Add($"Unrecognized bulk data entry: \"{data[i - 1]}\"");
                                LastReceiveWithWarnings = bytes;
                                goto exitloop; // can't continue parsing because we don't know how long this entry is
                        }
                    exitloop:;
                    parsedBulk = bd;
                }
            }
            catch (Exception e)
            {
                Warnings.Add($"Exception while parsing UDP message: \"{e.Message}\"");
                LastReceiveWithWarnings = bytes;
            }

            if (parsedFrame != null)
            {
                if (_session != parsedFrame.Session)
                    Status = "Session changed; synchronising";
                else
                {
                    if (LastFrame != null && LastFrame.SimTime != parsedFrame.SimTime) // don't do control on the very first frame, also filter out duplicate frames sent on pause / resume
                    {
                        parsedFrame.dT = parsedFrame.SimTime - LastFrame.SimTime;
                        ControlData control = null;
                        foreach (var ctl in FlightControllers.Where(c => c.Enabled))
                        {
                            var cd = ctl.ProcessFrame(parsedFrame);
                            if (cd != null)
                                control = cd; // todo: merge axes
                        }
                        LastControl = control;
                        if (control != null)
                            Send(control);
                    }

                    Status = "Active control";
                    Frames = parsedFrame.Frame;
                    Skips = parsedFrame.Skips;
                    LastFrameBytes = bytes.Length;
                    LastFrameUtc = DateTime.UtcNow;
                    LastFrame = parsedFrame;
                    _latencies.Enqueue(parsedFrame.Latency);
                    while (_latencies.Count > 200)
                        _latencies.TryDequeue(out _);
                }
            }
            if (parsedBulk != null)
            {
                if (_session == parsedBulk.Session)
                {
                    foreach (var ctrl in FlightControllers) // including disabled
                        ctrl.ProcessBulkUpdate(parsedBulk);
                }
                else
                {
                    _session = parsedBulk.Session;
                    LastBulk = parsedBulk;
                    foreach (var ctrl in FlightControllers) // including disabled
                        ctrl.NewSession(parsedBulk);
                    Status = "Session started; waiting for data";
                }
            }
        }
    }

    private double _pitchTrimRateCounter = 0;
    private double _rollTrimRateCounter = 0;

    private void Send(ControlData data)
    {
        var cmd = new StringBuilder();

        if (data.FrameTimestamp != null)
            cmd.Append($"1;ts;{data.FrameTimestamp.Value};");
        if (data.PitchAxis != null)
            cmd.Append($"2;sc;2001;{data.PitchAxis.Value};");
        if (data.RollAxis != null)
            cmd.Append($"2;sc;2002;{data.RollAxis.Value};");
        if (data.YawAxis != null)
            cmd.Append($"2;sc;2003;{data.YawAxis.Value};");
        if (data.ThrottleAxis != null)
            cmd.Append($"2;sc;2004;{1 - data.ThrottleAxis.Value};");
        //if (data.PitchTrim != null) // not supported on Hornet - need to refactor with airplane-specific controllers
        //    cmd.Append($"2;sc;2022;{data.PitchTrim.Value};");
        //if (data.RollTrim != null)
        //    cmd.Append($"2;sc;2023;{data.RollTrim.Value};");
        if (data.YawTrim != null)
            cmd.Append($"2;sc;3001;{data.YawTrim.Value};");
        // the following commands are Hornet specific; need to refactor with airplane-specific controllers
        if (data.PitchTrimRate != null)
        {
            _pitchTrimRateCounter += data.PitchTrimRate.Value.Clip(-0.95, 0.95); // 0.95 max to ensure that we occasionally release and press the button again (fixes it getting stuck occasionally...)
            if (_pitchTrimRateCounter >= 0.5)
            {
                cmd.Append($"4;pca3w;13;3015;3014;1;");
                _pitchTrimRateCounter -= 1.0;
            }
            else if (_pitchTrimRateCounter < -0.5)
            {
                cmd.Append($"4;pca3w;13;3015;3014;-1;");
                _pitchTrimRateCounter += 1.0;
            }
            else
                cmd.Append($"4;pca3w;13;3015;3014;0;");
        }
        if (data.RollTrimRate != null)
        {
            _rollTrimRateCounter += data.RollTrimRate.Value.Clip(-0.95, 0.95); // 0.95 max to ensure that we occasionally release and press the button again (fixes it getting stuck occasionally...)
            if (_rollTrimRateCounter >= 0.5)
            {
                cmd.Append($"4;pca3w;13;3016;3017;1;");
                _rollTrimRateCounter -= 1.0;
            }
            else if (_rollTrimRateCounter < -0.5)
            {
                cmd.Append($"4;pca3w;13;3016;3017;-1;");
                _rollTrimRateCounter += 1.0;
            }
            else
                cmd.Append($"4;pca3w;13;3016;3017;0;");
        }

        var bytes = cmd.ToString().ToUtf8();
        _udp.Send(bytes, bytes.Length, _endpoint);
    }
}

public class BulkData
{
    public double Session;
    public bool ExportAllowed;
    public string Aircraft;
    public string DcsVersion;
}

public class FrameData
{
    public double Session;
    public int Frame, Skips;
    public bool ExportAllowed;
    public double FrameTimestamp, Latency;

    public double SimTime, dT;
    /// <summary>Angle of attack in degrees; -90 (nose down relative to airflow) .. 0 (boresight aligned with airflow) .. 90 (nose up relative to airflow)</summary>
    public double AngleOfAttack;
    /// <summary>Angle of sideslip in degrees; -90 (nose to the left of the airflow) .. 0 (aligned) .. 90 (nose to the right of the airflow)</summary>
    public double AngleOfSideSlip;
    public double PosX, PosY, PosZ;
    public double AccX, AccY, AccZ;
    /// <summary>True ASL altitude in meters. Not affected by the pressure setting.</summary>
    public double AltitudeAsl;
    /// <summary>True AGL altitude in meters. Not affected by the pressure setting. Not affected by buildings. High altitude lakes are "ground" for the purpose of this reading.</summary>
    public double AltitudeAgl;
    /// <summary>Barometric altitude in meters. Possibly affected by the pressure setting, but always reads 0 on Hornet.</summary>
    public double AltitudeBaro;
    /// <summary>Radar altitude in meters. Affected by buildings. Not affected by the radar range limit (eg in Hornet).</summary>
    public double AltitudeRadar;
    public double SpeedTrue, SpeedIndicated, SpeedMach, SpeedVertical; // meters/second; details untested
    public double VelX, VelY, VelZ; // meters/second; details untested
    /// <summary>Pitch angle in degrees relative to the horizon; -90 (down) .. 0 (horizon) .. 90 (up).</summary>
    public double Pitch;
    /// <summary>Bank angle in degrees relative to the horizon; -180 (upside down) .. -90 (left wing straight down) .. 0 (level) .. 90 (right wing straight down) .. 180 (upside down)</summary>
    public double Bank;
    /// <summary>Compass heading in degrees. This is the true heading (not magnetic) and is not affected by the FCS setting. 0..360.</summary>
    public double Heading;
    /// <summary>Velocity pitch angle in degrees relative to the horizon; -90 (down) .. 0 (horizon) .. 90 (up). This is where the velocity vector points.</summary>
    public double VelPitch => Math.Atan2(VelY, Math.Sqrt(VelX * VelX + VelZ * VelZ)).ToDeg();
    /// <summary>Angular pitch rate in degrees/second. Positive is pitching up. Relative to the wing axis: this is not the same as the rate of change of <see cref="Pitch"/> over time; it's what a gyro would read. A 90 deg bank turn would have a large pitch rate even as the horizon-relative <see cref="Pitch"/> stays constant.</summary>
    public double GyroPitch;
    /// <summary>Angular roll rate in degrees/second. Positive is roll to the right. Relative to the boresight axis: this is not the same as the rate of change of <see cref="Bank"/> over time; it's what a gyro would read.</summary>
    public double GyroRoll;
    /// <summary>Angular yaw rate in degrees/second. Positive is yaw to the right. Relative to the vertical airplane axis: this is not the same as the rate of change of <see cref="Heading"/> over time; it's what a gyro would read.</summary>
    public double GyroYaw;
    public double FuelInternal, FuelExternal;
    public double Flaps, Airbrakes;
    public double AileronL, AileronR, ElevatorL, ElevatorR, RudderL, RudderR;
    public double WindX, WindY, WindZ;
    public double JoyPitch, JoyRoll, JoyYaw, JoyThrottle1, JoyThrottle2;
}

public class ControlData
{
    public double? FrameTimestamp; // for latency reports
    public double? PitchAxis;
    public double? RollAxis;
    public double? YawAxis;
    public double? ThrottleAxis;
    public double? PitchTrim;
    public double? RollTrim;
    public double? YawTrim;
    public double? PitchTrimRate;
    public double? RollTrimRate;
    public double? YawTrimRate;

    // trim axes? airbrakes? flaps? landing gear?
}
