using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Input;
using RT.Keyboard;
using RT.Serialization;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public class DcsController
{
    private UdpClient _udp;
    private IPEndPoint _endpoint;
    private CancellationTokenSource _cts;
    private Thread _thread;
    private GlobalKeyboardListener _keyboardListener;
    private JoystickState _joystick;
    private JoystickConfig _joystickConfig = new();
    private double _session;
    private ConcurrentQueue<(double data, double ctrl)> _latencies = new();

    public ObservableCollection<FlightControllerBase> FlightControllers { get; private set; } = new();
    public int Port { get; private set; }
    public ConcurrentDictionary<string, bool> Warnings { get; private set; } = new(); // client can remove seen warnings but they may get re-added on next occurrence
    public byte[] LastReceiveWithWarnings { get; private set; }

    public bool IsRunning { get; private set; } = false;
    public string Status { get; private set; } = "Stopped";
    public int LastFrameBytes { get; private set; }
    public DateTime LastFrameUtc { get; private set; }
    public IEnumerable<(double data, double ctrl)> Latencies => _latencies;
    /// <summary>
    ///     The frame that preceded <see cref="LastFrame"/>. Not null during all <see cref="FlightControllerBase"/> callbacks.</summary>
    public FrameData PrevFrame { get; private set; }
    /// <summary>
    ///     The last frame received from DCS. Cleared to null on stop/session change. Not null during all <see
    ///     cref="FlightControllerBase"/> callbacks.</summary>
    public FrameData LastFrame { get; private set; }
    public BulkData LastBulk { get; private set; }
    public ControlData LastControl { get; private set; }
    public JoystickReader Joystick => _joystick?.Reader;

    public void LoadConfig()
    {
        if (File.Exists(PathUtil.AppPathCombine("Config", "joystick.xml")))
            _joystickConfig = ClassifyXml.DeserializeFile<JoystickConfig>(PathUtil.AppPathCombine("Config", "joystick.xml"));
    }
    public void SaveConfig()
    {
        ClassifyXml.SerializeToFile(_joystickConfig, PathUtil.AppPathCombine("Config", "joystick.xml"));
    }

    public void Start(int udpPort = 9876)
    {
        if (IsRunning)
            throw new InvalidOperationException("Controller is already running.");
        Port = udpPort;
        _udp = new UdpClient(Port, AddressFamily.InterNetwork);
        _endpoint = new IPEndPoint(IPAddress.Any, 0);
        _cts = new CancellationTokenSource();
        Warnings.Clear();
        foreach (var c in FlightControllers)
            if (c.Enabled)
                c.Reset();
        _thread = new Thread(() => thread(_cts.Token)) { IsBackground = true };
        _thread.Start();
        _keyboardListener = new() { HookAllKeys = true };
        _keyboardListener.KeyDown += globalKeyDown;
        _keyboardListener.KeyUp += globalKeyUp;
        _joystick = new(_joystickConfig);
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
        _keyboardListener.KeyDown -= globalKeyDown;
        _keyboardListener.KeyUp -= globalKeyUp;
        _keyboardListener.Dispose();
        _keyboardListener = null;

        IsRunning = false;
        Status = "Stopped";
        PrevFrame = null;
        LastFrameUtc = DateTime.MinValue;
        LastFrame = null;
        LastBulk = null;
        LastControl = null;
        _latencies.Clear();
    }

    private void thread(CancellationToken token)
    {
        var bankRateFilter = Filters.BesselD5;
        while (!token.IsCancellationRequested)
        {
            foreach (var ctl in FlightControllers)
                ctl.Dcs = this; // kind of dirty but the easiest way to set this property
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
                            case "fr": fd.FrameNum = int.Parse(data[i++]); break;
                            case "ufof": fd.Underflows = int.Parse(data[i++]); fd.Overflows = int.Parse(data[i++]); break;
                            case "time": fd.SimTime = double.Parse(data[i++]); break;
                            case "sent": fd.LatencyData = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - double.Parse(data[i++]); break;
                            case "ltcy": fd.LatencyControl = double.Parse(data[i++]); break;
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
                            case "ias": fd.SpeedIndicatedBad = double.Parse(data[i++]); break;
                            case "mach": fd.SpeedMachBad = double.Parse(data[i++]); break;
                            case "aoa": fd.AngleOfAttack = double.Parse(data[i++]); break;
                            case "aoss": fd.AngleOfSideSlip = -double.Parse(data[i++]); break;
                            case "fuint": fd.FuelInternal = double.Parse(data[i++]); break;
                            case "fuext": fd.FuelExternal = double.Parse(data[i++]); break;
                            case "fufl": fd.FuelFlow = double.Parse(data[i++]); break;
                            case "surf":
                                fd.AileronL = double.Parse(data[i++]); fd.AileronR = double.Parse(data[i++]);
                                fd.ElevatorL = double.Parse(data[i++]); fd.ElevatorR = double.Parse(data[i++]);
                                fd.RudderL = double.Parse(data[i++]); fd.RudderR = double.Parse(data[i++]);
                                break;
                            case "flap": fd.Flaps = double.Parse(data[i++]); break;
                            case "airbrk": fd.Airbrakes = double.Parse(data[i++]); break;
                            case "lg": fd.LandingGear = double.Parse(data[i++]); break;
                            case "wind": fd.WindX = double.Parse(data[i++]); fd.WindY = double.Parse(data[i++]); fd.WindZ = double.Parse(data[i++]); break;
                            case "joyp": fd.JoyPitch = double.Parse(data[i++]); break;
                            case "joyr": fd.JoyRoll = double.Parse(data[i++]); break;
                            case "joyy": fd.JoyYaw = double.Parse(data[i++]); break;
                            case "joyt1": fd.JoyThrottle1 = double.Parse(data[i++]); break;
                            case "joyt2": fd.JoyThrottle2 = double.Parse(data[i++]); break;
                            case "ptrm": fd.TrimPitch = double.Parse(data[i++]); break;
                            case "rtrm": fd.TrimRoll = double.Parse(data[i++]); break;
                            case "ytrm": fd.TrimYaw = double.Parse(data[i++]); break;
                            case "test1": fd.Test1 = double.Parse(data[i++]); break;
                            case "test2": fd.Test2 = double.Parse(data[i++]); break;
                            case "test3": fd.Test3 = double.Parse(data[i++]); break;
                            case "test4": fd.Test4 = double.Parse(data[i++]); break;
                            default:
                                if (Warnings.Count > 100) Warnings.Clear(); // some warnings change all the time; ugly but good enough fix for that
                                Warnings[$"Unrecognized frame data entry: \"{data[i - 1]}\""] = true;
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
                                Warnings[$"Unrecognized bulk data entry: \"{data[i - 1]}\""] = true;
                                LastReceiveWithWarnings = bytes;
                                goto exitloop; // can't continue parsing because we don't know how long this entry is
                        }
                    exitloop:;
                    parsedBulk = bd;
                }
            }
            catch (Exception e)
            {
                Warnings[$"Exception while parsing UDP message: \"{e.Message}\""] = true;
                LastReceiveWithWarnings = bytes;
            }

            if (parsedFrame != null)
            {
                if (_session != parsedFrame.Session)
                {
                    PrevFrame = LastFrame = null;
                    Status = "Session changed; synchronising";
                }
                else
                {
                    Status = "Active control";
                    if (LastFrame?.SimTime != parsedFrame.SimTime) // the frame we've just received may be a duplicate, which happens on pause / resume
                    {
                        PrevFrame = LastFrame;
                        LastFrame = parsedFrame;
                        LastFrameBytes = bytes.Length;
                        LastFrameUtc = DateTime.UtcNow;
                        _latencies.Enqueue((LastFrame.LatencyData, LastFrame.LatencyControl));
                        while (_latencies.Count > 200)
                            _latencies.TryDequeue(out _);
                        if (PrevFrame != null) // don't do control on the very first frame
                        {
                            LastFrame.dT = LastFrame.SimTime - PrevFrame.SimTime;
                            LastFrame.BankRate = bankRateFilter.Step((LastFrame.Bank - PrevFrame.Bank) / LastFrame.dT);
                            _joystick?.Update();
                            ControlData control = null;
                            foreach (var ctl in FlightControllers)
                                if (ctl.Enabled)
                                {
                                    var cd = ctl.ProcessFrame(LastFrame);
                                    if (cd != null)
                                    {
                                        if (control == null)
                                            control = cd;
                                        else if (!control.Merge(cd))
                                            Warnings[$"Controller \"{ctl.Name}\" is setting the same controls as an earlier controller; partially ignored."] = true;
                                    }
                                }
                            LastControl = control;
                            if (control != null)
                                Send(control);
                        }
                    }
                }
            }
            if (parsedBulk != null)
            {
                LastBulk = parsedBulk;
                if (_session == parsedBulk.Session)
                {
                    foreach (var ctrl in FlightControllers)
                        if (ctrl.Enabled)
                            ctrl.ProcessBulkUpdate(parsedBulk);
                }
                else
                {
                    _session = parsedBulk.Session;
                    PrevFrame = LastFrame = null;
                    bankRateFilter.Reset();
                    foreach (var ctrl in FlightControllers)
                        if (ctrl.Enabled)
                        {
                            ctrl.Reset();
                            ctrl.NewSession(parsedBulk);
                        }
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

        cmd.Append($"1;ts;{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0:0.000};");
        if (data.PitchAxis != null)
            cmd.Append($"2;sc;2001;{data.PitchAxis.Value};");
        if (data.RollAxis != null)
            cmd.Append($"2;sc;2002;{data.RollAxis.Value};");
        if (data.YawAxis != null)
            cmd.Append($"2;sc;2003;{data.YawAxis.Value};");
        if (data.ThrottleAxis != null)
            cmd.Append($"2;sc;2004;{1 - data.ThrottleAxis.Value};");
        if (data.PitchTrim != null)
        {
            if (LastBulk?.Aircraft == "F-16C_50")
                cmd.Append($"3;pca;2;3008;{data.PitchTrim.Value};");
            else
                throw new NotSupportedException($"Don't know how to apply pitch trim on {LastBulk?.Aircraft}");
        }
        if (data.RollTrim != null)
        {
            if (LastBulk?.Aircraft == "F-16C_50")
                cmd.Append($"3;pca;2;3007;{-data.RollTrim.Value};");
            else
                throw new NotSupportedException($"Don't know how to apply roll trim on {LastBulk?.Aircraft}");
        }
        if (data.YawTrim != null)
        {
            if (LastBulk?.Aircraft == "F-16C_50")
                cmd.Append($"3;pca;2;3009;{data.YawTrim.Value};");
            else
                throw new NotSupportedException($"Don't know how to apply yaw trim on {LastBulk?.Aircraft}");
        }
        if (data.PitchTrimRate != null)
        {
            var btn = LastBulk?.Aircraft switch { "FA-18C_hornet" => "13;3015;3014", "F-16C_50" => "16;3002;3003", _ => throw new NotSupportedException($"Don't know how to apply pitch trim rate on {LastBulk?.Aircraft}") };
            _pitchTrimRateCounter += data.PitchTrimRate.Value.Clip(-0.95, 0.95); // 0.95 max to ensure that we occasionally release and press the button again (fixes it getting stuck occasionally...)
            if (_pitchTrimRateCounter >= 0.5)
            {
                cmd.Append($"4;pca3w;{btn};1;");
                _pitchTrimRateCounter -= 1.0;
            }
            else if (_pitchTrimRateCounter < -0.5)
            {
                cmd.Append($"4;pca3w;{btn};-1;");
                _pitchTrimRateCounter += 1.0;
            }
            else
                cmd.Append($"4;pca3w;{btn};0;");
        }
        if (data.RollTrimRate != null)
        {
            var btn = LastBulk?.Aircraft switch { "FA-18C_hornet" => "13;3016;3017", "F-16C_50" => "16;3004;3005", _ => throw new NotSupportedException($"Don't know how to apply roll trim rate on {LastBulk?.Aircraft}") };
            _rollTrimRateCounter += data.RollTrimRate.Value.Clip(-0.95, 0.95); // 0.95 max to ensure that we occasionally release and press the button again (fixes it getting stuck occasionally...)
            if (_rollTrimRateCounter >= 0.5)
            {
                cmd.Append($"4;pca3w;{btn};1;");
                _rollTrimRateCounter -= 1.0;
            }
            else if (_rollTrimRateCounter < -0.5)
            {
                cmd.Append($"4;pca3w;{btn};-1;");
                _rollTrimRateCounter += 1.0;
            }
            else
                cmd.Append($"4;pca3w;{btn};0;");
        }
        if (data.YawTrimRate != null)
        {
            throw new NotSupportedException($"Don't know how to apply yaw trim rate on {LastBulk?.Aircraft}");
        }
        if (data.SpeedBrakeRate != null)
            if (LastBulk?.Aircraft == "FA-18C_hornet")
                cmd.Append($"3;pca;13;3035;{-data.SpeedBrakeRate};"); // 1=retract, -1=extend // 13 from devices.lua, 3035 from command_defs.lua
            else if (LastBulk?.Aircraft == "F-16C_50")
                cmd.Append($"3;pca;16;3031;{-data.SpeedBrakeRate};"); // 1=retract, -1=extend // 16 from devices.lua, 3031 from command_defs.lua
            else
                throw new NotSupportedException($"Don't know how to apply speedbrake on {LastBulk?.Aircraft}");

        var bytes = cmd.ToString().ToUtf8();
        _udp.Send(bytes, bytes.Length, _endpoint);
    }

    private void globalKeyDown(object sender, GlobalKeyEventArgs e) => globalKeyEvent(e, down: true);
    private void globalKeyUp(object sender, GlobalKeyEventArgs e) => globalKeyEvent(e, down: false);
    private void globalKeyEvent(GlobalKeyEventArgs e, bool down)
    {
        if (!IsRunning || !DcsWindow.DcsHasFocus())
            return;
        var ee = new KeyEventArgs
        {
            Down = down,
            Key = KeyInterop.KeyFromVirtualKey((int)e.VirtualKeyCode),
            Modifiers = (e.ModifierKeys.Win ? ModifierKeys.Windows : 0) | (e.ModifierKeys.Ctrl ? ModifierKeys.Control : 0) | (e.ModifierKeys.Alt ? ModifierKeys.Alt : 0) | (e.ModifierKeys.Shift ? ModifierKeys.Shift : 0),
        };
        foreach (var c in FlightControllers)
            if (c.Enabled && c.HandleKey(ee))
            {
                e.Handled = true;
                return;
            }
    }

    public T GetController<T>(bool orAdd = false) where T : FlightControllerBase, new()
    {
        var ctrl = FlightControllers.OfType<T>().SingleOrDefault();
        if (ctrl == null && orAdd)
        {
            ctrl = new T();
            ctrl.Dcs = this;
            FlightControllers.Add(ctrl);
        }
        return ctrl;
    }
}
