using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Input;
using RT.Serialization;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public abstract class FlightControllerBase
{
    public abstract string Name { get; set; }
    public virtual string Status => _status;
    protected string _status = "";
    /// <summary>
    ///     Disabled controllers receive no callbacks, and are as good as completely removed from the list of controllers.</summary>
    public bool Enabled { get; set; } = false;
    public DcsController Dcs { get; set; }
    /// <summary>Called on start, on setting <see cref="Enabled"/>=true, and also before every <see cref="NewSession"/>.</summary>
    public virtual void Reset() { }
    public virtual void NewSession(BulkData bulk) { }
    /// <summary>
    ///     Called only for enabled controllers every time a new data frame is received from DCS.</summary>
    /// <param name="frame">
    ///     Latest data frame. Equal to <see cref="DcsController.LastFrame"/>.</param>
    /// <returns>
    ///     Desired inputs. Null values for things that don't need to be controlled, or null if nothing needs to be
    ///     controlled.</returns>
    public virtual ControlData ProcessFrame(FrameData frame) { return null; }
    public virtual void ProcessBulkUpdate(BulkData bulk) { }
    public virtual void HandleSignal(string signal) { }
    public virtual bool HandleKey(KeyEventArgs e) { return false; }
}

public class KeyEventArgs
{
    public bool Down;
    public Key Key;
    public ModifierKeys Modifiers;
}

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
    private ConcurrentQueue<double> _latencies = new();

    public List<FlightControllerBase> FlightControllers { get; private set; } = new();
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
    /// <summary>
    ///     The frame that preceded <see cref="LastFrame"/>. Not null during all <see cref="FlightControllerBase"/> callbacks.</summary>
    public FrameData PrevFrame { get; private set; }
    /// <summary>
    ///     The last frame received from DCS. Cleared to null on stop/session change. Not null during all <see
    ///     cref="FlightControllerBase"/> callbacks.</summary>
    public FrameData LastFrame { get; private set; }
    public BulkData LastBulk { get; private set; }
    public ControlData LastControl { get; private set; }
    public JoystickReader Joystick => _joystick.Reader;

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
        Frames = 0;
        Skips = 0;
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
                {
                    PrevFrame = LastFrame = null;
                    Status = "Session changed; synchronising";
                }
                else
                {
                    Status = "Active control";
                    Frames = parsedFrame.Frame;
                    Skips = parsedFrame.Skips;
                    if (LastFrame?.SimTime != parsedFrame.SimTime) // the frame we've just received may be a duplicate, which happens on pause / resume
                    {
                        PrevFrame = LastFrame;
                        LastFrame = parsedFrame;
                        LastFrameBytes = bytes.Length;
                        LastFrameUtc = DateTime.UtcNow;
                        _latencies.Enqueue(LastFrame.Latency);
                        while (_latencies.Count > 200)
                            _latencies.TryDequeue(out _);
                        if (PrevFrame != null) // don't do control on the very first frame
                        {
                            LastFrame.dT = LastFrame.SimTime - PrevFrame.SimTime;
                            LastFrame.BankRate = bankRateFilter.Step((LastFrame.Bank - PrevFrame.Bank) / LastFrame.dT);
                            _joystick.Update();
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
                                            Warnings.Add($"Controller \"{ctl.Name}\" is setting the same controls as an earlier controller; partially ignored.");
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
    /// <summary>
    ///     Angle of attack in degrees; -90 (nose down relative to airflow) .. 0 (boresight aligned with airflow) .. 90 (nose
    ///     up relative to airflow)</summary>
    public double AngleOfAttack;
    /// <summary>
    ///     Angle of sideslip in degrees; -90 (nose to the left of the airflow) .. 0 (aligned) .. 90 (nose to the right of the
    ///     airflow)</summary>
    public double AngleOfSideSlip;
    public double PosX, PosY, PosZ;
    public double AccX, AccY, AccZ;
    /// <summary>True ASL altitude in meters. Not affected by the pressure setting.</summary>
    public double AltitudeAsl;
    /// <summary>
    ///     True AGL altitude in meters. Not affected by the pressure setting. Not affected by buildings. High altitude lakes
    ///     are "ground" for the purpose of this reading.</summary>
    public double AltitudeAgl;
    /// <summary>Barometric altitude in meters. Possibly affected by the pressure setting, but always reads 0 on Hornet.</summary>
    public double AltitudeBaro;
    /// <summary>Radar altitude in meters. Affected by buildings. Not affected by the radar range limit (eg in Hornet).</summary>
    public double AltitudeRadar;
    public double SpeedTrue, SpeedIndicated, SpeedMach, SpeedVertical; // meters/second; details untested
    public double VelX, VelY, VelZ; // meters/second; details untested
    /// <summary>Pitch angle in degrees relative to the horizon; -90 (down) .. 0 (horizon) .. 90 (up).</summary>
    public double Pitch;
    /// <summary>
    ///     Bank angle in degrees relative to the horizon; -180 (upside down) .. -90 (left wing straight down) .. 0 (level) ..
    ///     90 (right wing straight down) .. 180 (upside down)</summary>
    public double Bank;
    /// <summary>
    ///     Compass heading in degrees. This is the true heading (not magnetic) and is not affected by the FCS setting.
    ///     0..360.</summary>
    public double Heading;
    /// <summary>
    ///     Velocity pitch angle in degrees relative to the horizon; -90 (down) .. 0 (horizon) .. 90 (up). This is where the
    ///     velocity vector points.</summary>
    public double VelPitch => Math.Atan2(VelY, Math.Sqrt(VelX * VelX + VelZ * VelZ)).ToDeg();
    /// <summary>
    ///     Angular pitch rate in degrees/second. Positive is pitching up. Relative to the wing axis: this is not the same as
    ///     the rate of change of <see cref="Pitch"/> over time; it's what a gyro would read. A 90 deg bank turn would have a
    ///     large pitch rate even as the horizon-relative <see cref="Pitch"/> stays constant.</summary>
    public double GyroPitch;
    /// <summary>
    ///     Angular roll rate in degrees/second. Positive is roll to the right. Relative to the boresight axis: this is not
    ///     the same as the rate of change of <see cref="Bank"/> over time; it's what a gyro would read.</summary>
    public double GyroRoll;
    /// <summary>
    ///     Angular yaw rate in degrees/second. Positive is yaw to the right. Relative to the vertical airplane axis: this is
    ///     not the same as the rate of change of <see cref="Heading"/> over time; it's what a gyro would read.</summary>
    public double GyroYaw;
    public double FuelInternal, FuelExternal;
    /// <summary>
    ///     Total fuel flow in pounds/hour. May be read off gauges which cause glitches in the reading as it goes through
    ///     changing decimal places.</summary>
    public double FuelFlow;
    public double Flaps, Airbrakes, LandingGear;
    public double AileronL, AileronR, ElevatorL, ElevatorR, RudderL, RudderR;
    public double? TrimRoll, TrimPitch, TrimYaw;
    public double WindX, WindY, WindZ;
    public double JoyPitch, JoyRoll, JoyYaw, JoyThrottle1, JoyThrottle2;
    public double Test1, Test2, Test3, Test4;

    /// <summary>
    ///     Rate of change of <see cref="Bank"/> in degrees/second. Computed directly from <see cref="Bank"/> with a short
    ///     filter. This can be a better metric than gyro roll rate for controllers attempting to control the bank angle,
    ///     especially when trying to keep it steady.</summary>
    public double BankRate;
}

public class ControlData
{
    public double? FrameTimestamp; // for latency reports
    /// <summary>
    ///     Pitch input: -1.0 (max pitch down), 0 (neutral), 1.0 (max pitch up). Controls the stick position. The motion range
    ///     varies by plane. F-18: -0.5 to 1.0.</summary>
    public double? PitchAxis;
    /// <summary>Roll input: -1.0 (max roll left), 0 (neutral), 1.0 (max roll right).</summary>
    public double? RollAxis;
    /// <summary>Yaw input: -1.0 (max yaw left), 0 (neutral), 1.0 (max yaw right).</summary>
    public double? YawAxis;
    /// <summary>
    ///     Overall throttle setting; implementation varies by plane. F-16: 0.0-1.5 normal power range; 1.50-1.58 no change;
    ///     1.59-2.00 afterburner. F-18: same but no-change range is 1.50-1.57. Normal power range seems fully proportional
    ///     while afterburner range appears to be stepped.</summary>
    public double? ThrottleAxis;
    /// <summary>
    ///     Absolute pitch trim setting: -1.0 (max trim down), 0 (neutral), 1.0 (max trim up). Note that it can take a while
    ///     for the plane to achieve the specified setting after a large change. Supported: F-16. Not supported: F-18.</summary>
    public double? PitchTrim;
    /// <summary>
    ///     Absolute roll trim setting: -1.0 (max trim left), 0 (neutral), 1.0 (max trim right). Note that it can take a while
    ///     for the plane to achieve the specified setting after a large change. Supported: F-16. Not supported: F-18.</summary>
    public double? RollTrim;
    /// <summary>
    ///     Absolute yaw trim setting: -1.0 (max trim left), 0 (neutral), 1.0 (max trim right). Note that it can take a while
    ///     for the plane to achieve the specified setting after a large change. Supported: F-16. Not supported: F-18.</summary>
    public double? YawTrim;
    /// <summary>
    ///     Rate of change for pitch trim: -1.0 (max rate trim down), 0 (no change), 1.0 (max rate trim up). This typically
    ///     controls the HOTAS trim switch, with -1/1 being full down, and smaller values implemented as PWM (pressing and
    ///     releasing the switch).</summary>
    public double? PitchTrimRate;
    /// <summary>
    ///     Rate of change for roll trim: -1.0 (max rate trim left), 0 (no change), 1.0 (max rate trim right). This typically
    ///     controls the HOTAS trim switch, with -1/1 being full down, and smaller values implemented as PWM (pressing and
    ///     releasing the switch).</summary>
    public double? RollTrimRate;
    /// <summary>Rate of change for yaw trim: -1.0 (max rate trim left), 0 (no change), 1.0 (max rate trim right).</summary>
    public double? YawTrimRate;
    public double? SpeedBrakeRate; // 1=more brake, -1=less brake

    /// <summary>
    ///     Merges <paramref name="other"/> into this instance. Returns <c>true</c> if none of the properties are set in both
    ///     instances. Otherwise this instance takes priority and the method returns <c>false</c>.</summary>
    public bool Merge(ControlData other)
    {
        bool ok = true;
        PitchAxis = merge(PitchAxis, other.PitchAxis);
        RollAxis = merge(RollAxis, other.RollAxis);
        YawAxis = merge(YawAxis, other.YawAxis);
        ThrottleAxis = merge(ThrottleAxis, other.ThrottleAxis);
        PitchTrim = merge(PitchTrim, other.PitchTrim);
        RollTrim = merge(RollTrim, other.RollTrim);
        YawTrim = merge(YawTrim, other.YawTrim);
        PitchTrimRate = merge(PitchTrimRate, other.PitchTrimRate);
        RollTrimRate = merge(RollTrimRate, other.RollTrimRate);
        YawTrimRate = merge(YawTrimRate, other.YawTrimRate);
        SpeedBrakeRate = merge(SpeedBrakeRate, other.SpeedBrakeRate);
        return ok;

        double? merge(double? a, double? b)
        {
            if (a != null && b != null)
                ok = false;
            return a ?? b;
        }
    }
}
