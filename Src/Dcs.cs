using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

interface IFlightController
{
    void NewSession(BulkData bulk);
    ControlData ProcessFrame(FrameData frame); // can return null
    void ProcessBulkUpdate(BulkData bulk);
}

class DcsController
{
    private UdpClient _udp;
    private IPEndPoint _endpoint;
    private CancellationTokenSource _cts;
    private Thread _thread;
    private double _session;
    private Queue<double> _latencies = new();

    public IFlightController FlightController { get; private set; }
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

    public void Start(IFlightController controller, int udpPort = 9876)
    {
        if (IsRunning)
            throw new InvalidOperationException("Controller is already running.");
        FlightController = controller ?? throw new ArgumentNullException(nameof(controller));
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
        FlightController = null;
        IsRunning = false;
        Status = "Stopped";
        Frames = 0;
        Skips = 0;
        LastFrameUtc = DateTime.MinValue;
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
                            case "skips": fd.Skips = int.Parse(data[i++]); break;
                            case "time": fd.SimTime = double.Parse(data[i++]); break;
                            case "sent": fd.FrameTimestamp = double.Parse(data[i++]); break;
                            case "ltcy": fd.Latency = double.Parse(data[i++]); break;
                            case "exp": fd.ExportAllowed = data[i++] == "true"; break;
                            case "pitch": fd.Pitch = double.Parse(data[i++]); break;
                            case "bank": fd.Bank = double.Parse(data[i++]); break;
                            case "hdg": fd.Heading = double.Parse(data[i++]); break;
                            case "ang": fd.AngVelX = double.Parse(data[i++]); fd.AngVelY = double.Parse(data[i++]); fd.AngVelZ = double.Parse(data[i++]); break;
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
                            case "aoss": fd.AngleOfSideSlip = double.Parse(data[i++]); break;
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
                            default:
                                Warnings.Add($"Unrecognized frame data entry: \"{data[i - 1]}\"");
                                LastReceiveWithWarnings = bytes;
                                goto exitloop; // can't continue parsing because we don't know how long this entry is
                        }
                    exitloop:;
                    if (_session != fd.Session)
                        Status = "Session changed; synchronising";
                    else
                    {
                        var control = FlightController.ProcessFrame(fd);
                        if (control != null)
                            Send(control);

                        Status = "Active control";
                        Frames++;
                        Skips = fd.Skips;
                        LastFrameBytes = bytes.Length;
                        LastFrameUtc = DateTime.UtcNow;
                        _latencies.Enqueue(fd.Latency);
                        while (_latencies.Count > 200)
                            _latencies.Dequeue();
                    }
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
                            default:
                                Warnings.Add($"Unrecognized bulk data entry: \"{data[i - 1]}\"");
                                LastReceiveWithWarnings = bytes;
                                goto exitloop; // can't continue parsing because we don't know how long this entry is
                        }
                    exitloop:;
                    if (_session == bd.Session)
                        FlightController.ProcessBulkUpdate(bd);
                    else
                    {
                        _session = bd.Session;
                        FlightController.NewSession(bd);
                        Status = "Session started; waiting for data";
                    }
                }
            }
            catch (Exception e)
            {
                Warnings.Add($"Exception while parsing UDP message: \"{e.Message}\"");
                LastReceiveWithWarnings = bytes;
            }
        }
    }

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

        var bytes = cmd.ToString().ToUtf8();
        _udp.Send(bytes, bytes.Length, _endpoint);
    }
}

class BulkData
{
    public double Session;
    public bool ExportAllowed;
    public string Aircraft;
}

class FrameData
{
    public double Session;
    public int Skips;
    public bool ExportAllowed;
    public double FrameTimestamp, Latency;

    public double SimTime;
    public double AngleOfAttack, AngleOfSideSlip;
    public double PosX, PosY, PosZ;
    public double AccX, AccY, AccZ;
    public double AltitudeAsl, AltitudeAgl, AltitudeBaro, AltitudeRadar;
    public double SpeedTrue, SpeedIndicated, SpeedMach, SpeedVertical;
    public double VelX, VelY, VelZ;
    public double Pitch, Bank, Heading;
    public double AngVelX, AngVelY, AngVelZ;
    public double FuelInternal, FuelExternal;
    public double Flaps, Airbrakes;
    public double AileronL, AileronR, ElevatorL, ElevatorR, RudderL, RudderR;
    public double WindX, WindY, WindZ;
}

class ControlData
{
    public double? FrameTimestamp; // for latency reports
    public double? PitchAxis;
    public double? RollAxis;
    public double? YawAxis;
    public double? ThrottleAxis;

    // trim axes? airbrakes? flaps? landing gear?
}
