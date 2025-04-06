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
    private string _session;
    private AircraftDataRequests _dataRequests;
    private ConcurrentQueue<(double data, double ctrl)> _latencies = new();

    public ObservableCollection<FlightControllerBase> FlightControllers { get; private set; } = new();
    public int Port { get; private set; }
    public ConcurrentDictionary<string, bool> Warnings { get; private set; } = new(); // client can remove seen warnings but they may get re-added on next occurrence

    public bool IsRunning { get; private set; } = false;
    public string Status { get; private set; } = "Stopped";
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
    public Action<DataPacket> ProcessRawFrameData { get; set; } = null; // for testing and tuning

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
        _latencies.Clear();
        ClearSession();
    }

    private void ClearSession()
    {
        Aircraft = null;
        PrevFrame = null;
        LastFrame = null;
        LastBulk = null;
        LastControl = null;
        _dataRequests = null;
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
                var pkt = new DataPacket(bytes);
                if (pkt.PacketType == "bulk")
                    ProcessBulkPacket(pkt);
                else if (pkt.PacketType == "frame")
                    ProcessFramePacket(pkt);
                else
                    Warnings[$"Unrecognized packet type: \"{pkt.PacketType}\""] = true;
            }
            catch (Exception e)
            {
                Warnings[$"Exception while parsing UDP message: \"{e.Message}\""] = true;
            }
            if (Warnings.Count > 100)
                Warnings.Clear(); // some warnings change all the time; ugly but good enough fix for that
        }
    }

    private void ProcessBulkPacket(DataPacket pkt)
    {
        var bulk = new BulkData();
        bulk.ReceivedUtc = DateTime.UtcNow;
        bulk.Bytes = pkt.Bytes;
        bulk.ExportAllowed = pkt.Entries["exp"][0] == "true";
        bulk.DcsVersion = pkt.Entries["ver"][0];

        if (pkt.Session != _session || pkt.Aircraft != Aircraft?.DcsId)
        {
            ClearSession();
            _session = pkt.Session;
            Aircraft = AircraftTypes.TryGetValue(pkt.Aircraft, out var make) ? make() : new Aircraft();
            Aircraft.ProcessBulk(pkt, bulk);
            _dataRequests = new AircraftDataRequests(Aircraft);
            LastBulk = bulk;
            foreach (var ctrl in FlightControllers)
                if (ctrl.Enabled)
                {
                    ctrl.Reset();
                    ctrl.NewSession(bulk);
                }
            Status = "Session started; waiting for data";
        }
        else
        {
            Aircraft.ProcessBulk(pkt, bulk);
            LastBulk = bulk;
            foreach (var ctrl in FlightControllers)
                if (ctrl.Enabled)
                    ctrl.ProcessBulkUpdate(bulk);
        }
        // Warnings[$"Unrecognized bulk data entry: \"{e.Key}\""] = true;
    }

    private void ProcessFramePacket(DataPacket pkt)
    {
        if (pkt.Session != _session || pkt.Aircraft != Aircraft?.DcsId)
        {
            Status = "Session changed; synchronising";
            return;
        }
        if (pkt.Entries["reqsid"][0] != _dataRequests?.ReqsId)
        {
            Status = "Data requests not ready; synchronising";
            SendControl(new());
            return;
        }

        Status = "Active control";
        var frame = new FrameData();
        frame.ReceivedUtc = DateTime.UtcNow;
        frame.Bytes = pkt.Bytes;
        frame.SimTime = double.Parse(pkt.Entries["time"][0]);
        if (LastFrame?.SimTime == frame.SimTime)
            return; // the frame we've just received may be a duplicate, which happens on pause / resume
        frame.FrameNum = int.Parse(pkt.Entries["fr"][0]);
        frame.Underflows = int.Parse(pkt.Entries["ufof"][0]);
        frame.Overflows = int.Parse(pkt.Entries["ufof"][1]);
        frame.LatencyData = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - double.Parse(pkt.Entries["sent"][0]);
        frame.LatencyControl = double.Parse(pkt.Entries["ltcy"][0]);
        frame.DataRequestsId = pkt.Entries["reqsid"][0];
        if (LastFrame != null)
            frame.dT = frame.SimTime - LastFrame.SimTime;
        Aircraft.ProcessFrame(pkt, frame, LastFrame);
        ProcessRawFrameData?.Invoke(pkt);
        // Warnings[$"Unrecognized frame data entry: \"{e.Key}\""] = true;

        PrevFrame = LastFrame;
        LastFrame = frame;

        _latencies.Enqueue((LastFrame.LatencyData, LastFrame.LatencyControl));
        while (_latencies.Count > 200)
            _latencies.TryDequeue(out _);

        if (PrevFrame != null) // don't do control on the very first frame
        {
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
                SendControl(control);
        }
    }

    private void SendControl(ControlData data)
    {
        var cmd = new StringBuilder();

        cmd.Append($"1;ts;{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0:0.000};");
        Aircraft.BuildControlPacket(data, cmd);
        if (_dataRequests.ReqsId != LastFrame?.DataRequestsId)
            _dataRequests.BuildControlPacket(cmd);

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

    class AircraftDataRequests
    {
        public string ReqsId;
        private string _requestDef;

        public AircraftDataRequests(Aircraft acft)
        {
            ReqsId = Random.Shared.NextString(8);
            var reqdef = new StringBuilder();
            reqdef.Append($"req;{ReqsId};");
            var reqs = acft.DataRequests.ToList();
            foreach (var req in reqs)
            {
                reqdef.Append($"{req.key};{req.req.Length};");
                foreach (var r in req.req)
                {
                    if (r.StartsWith(";") || !r.EndsWith(";")) throw new Exception($"Check semicolons in data request: {r}");
                    reqdef.Append(r);
                }
            }
            _requestDef = reqdef.ToString();
            var argsCount = _requestDef.Count(c => c == ';') - 1;
            _requestDef = argsCount + ";" + _requestDef;
        }

        public void BuildControlPacket(StringBuilder pkt)
        {
            pkt.Append(_requestDef);
        }
    }
}

public class DataPacket
{
    public string PacketType { get; private set; }
    public string Session { get; private set; }
    public string Aircraft { get; private set; }
    public int Bytes { get; private set; }
    public IReadOnlyDictionary<string, Entry> Entries { get; private set; }

    public DataPacket(byte[] raw)
    {
        Bytes = raw.Length;
        parse(raw);
    }

    private void parse(byte[] raw)
    {
        var data = raw.FromUtf8().Split(";");
        if (data[0] != "v2")
            throw new InvalidOperationException("Data packet format not supported.");
        PacketType = data[1];
        var entries = new Dictionary<string, Entry>();
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
            entries.Add(key, new(len, data, i));
            i += len;
        }
        Session = entries["sess"][0];
        Aircraft = entries["aircraft"][0];
    }

    public class Entry(int _length, string[] _data, int _offset)
    {
        public int Length => _length;
        public string this[int index] => index >= 0 && index < _length ? _data[_offset + index] : throw new IndexOutOfRangeException();
    }
}
