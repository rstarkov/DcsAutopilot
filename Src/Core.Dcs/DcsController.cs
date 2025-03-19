using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
    public Aircraft Aircraft { get; private set; }
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

    public static Dictionary<string, Func<Aircraft>> AircraftTypes;

    static DcsController()
    {
        AircraftTypes = new();
        discoverAircraftTypes(Assembly.GetExecutingAssembly());
        discoverAircraftTypes(Assembly.GetEntryAssembly());

        void discoverAircraftTypes(Assembly assy)
        {
            foreach (var type in assy.GetTypes().Where(t => t.IsSubclassOf(typeof(Aircraft))))
            {
                var ctor = type.GetConstructor(Type.EmptyTypes);
                var make = () => (Aircraft)ctor.Invoke([]);
                AircraftTypes[make().DcsId] = make;
            }
        }
    }

    public DcsController()
    {
        FlightControllers.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
                foreach (FlightControllerBase c in e.NewItems)
                    c.Dcs = this;
        };
    }

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
        Aircraft = null;
        PrevFrame = null;
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
                var data = new DataPacket(bytes);
                if (data.PacketType == "frame")
                {
                    var fd = new FrameData();
                    foreach (var e in data.Entries)
                        switch (e.Key)
                        {
                            case "sess": fd.Session = double.Parse(e[0]); break;
                            case "fr": fd.FrameNum = int.Parse(e[0]); break;
                            case "ufof": fd.Underflows = int.Parse(e[0]); fd.Overflows = int.Parse(e[1]); break;
                            case "time": fd.SimTime = double.Parse(e[0]); break;
                            case "sent": fd.LatencyData = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - double.Parse(e[0]); break;
                            case "ltcy": fd.LatencyControl = double.Parse(e[0]); break;
                            case "exp": fd.ExportAllowed = e[0] == "true"; break;
                            default:
                                if (Aircraft.ProcessFrameEntry(fd, e))
                                    break;
                                if (Warnings.Count > 100) Warnings.Clear(); // some warnings change all the time; ugly but good enough fix for that
                                Warnings[$"Unrecognized frame data entry: \"{e.Key}\""] = true;
                                LastReceiveWithWarnings = bytes;
                                break;
                        }
                    parsedFrame = fd;
                }
                else if (data.PacketType == "bulk")
                {
                    var bd = new BulkData();
                    foreach (var e in data.Entries)
                        switch (e.Key)
                        {
                            case "sess": bd.Session = double.Parse(e[0]); break;
                            case "exp": bd.ExportAllowed = e[0] == "true"; break;
                            case "aircraft": bd.Aircraft = e[0]; break;
                            case "ver": bd.DcsVersion = e[0]; break;
                            default:
                                Warnings[$"Unrecognized bulk data entry: \"{e.Key}\""] = true;
                                LastReceiveWithWarnings = bytes;
                                break;
                        }
                    parsedBulk = bd;
                }
                else
                    Warnings[$"Unrecognized data type: \"{data.PacketType}\""] = true;
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
                            Aircraft.ProcessFrame(LastFrame, PrevFrame);
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
                    Aircraft = AircraftTypes.TryGetValue(parsedBulk.Aircraft, out var make) ? make() : new Aircraft(); // TODO: detect aircraft change within session and reset everything
                    PrevFrame = LastFrame = null;
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

public class DataPacket
{
    public string PacketType { get; private set; }
    public IReadOnlyList<Entry> Entries { get; private set; }

    public DataPacket(byte[] raw)
    {
        parse(raw);
    }

    private void parse(byte[] raw)
    {
        var data = raw.FromUtf8().Split(";");
        if (data[0] != "v2")
            throw new InvalidOperationException("Data packet format not supported.");
        PacketType = data[1];
        var entries = new List<Entry>();
        Entries = entries.AsReadOnly();
        for (int i = 2; i < data.Length;)
        {
            var key = data[i++];
            int len = 1;
            int lenPos = key.IndexOf(':');
            if (lenPos >= 0)
            {
                len = int.Parse(key[(lenPos + 1)..]);
                key = key[..lenPos];
            }
            entries.Add(new(key, len, data, i));
            i += len;
        }
    }

    public class Entry(string _key, int _length, string[] _data, int _offset)
    {
        public string Key => _key;
        public int Length => _length;
        public string this[int index] => index >= 0 && index < _length ? _data[_offset + index] : throw new IndexOutOfRangeException();
    }
}
