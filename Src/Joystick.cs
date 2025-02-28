using RT.Util;
using Windows.Gaming.Input;

namespace DcsAutopilot;

public class JoystickState
{
    public JoystickReader Reader;
    private JoystickDeviceState[] _devices;
    private Dictionary<string, (int device, int axis)> _axisMappings;

    public JoystickState(JoystickConfig cfg)
    {
        // later: handle devices being connected/disconnected
        RawGameController.RawGameControllers.ToList(); // oddly enough the first call to this returns nothing; second call succeeds
        _devices = RawGameController.RawGameControllers
            .GroupBy(d => $"{d.HardwareVendorId}/{d.HardwareProductId}")
            .SelectMany(g => g.OrderBy(d => d.NonRoamableId).Select((d, di) => new JoystickDeviceState(d, $"{g.Key}/{di}", cfg))) // ensure a stable order if multiple identical devices are connected
            .ToArray();
        _axisMappings = _devices.SelectMany((d, di) => d.Config.Axes.Select((a, ai) => (a.Mapping, (di, ai)))).Where(x => x.Mapping != null).ToDictionary(); // throws for duplicates
        Reader = new JoystickReader(this);
    }

    public void Update()
    {
        foreach (var device in _devices)
            device.Update();
    }

    public double RawAxis(int device, int axis) => _devices[device].AxesRaw[axis];
    public double RawAxisPrev(int device, int axis) => _devices[device].AxesRawPrev[axis];
    public double GetAxis(string axis) => _axisMappings.TryGetValue(axis, out var r) ? _devices[r.device].Axes[r.axis] : 0;
    public double GetAxisPrev(string axis) => _axisMappings.TryGetValue(axis, out var r) ? _devices[r.device].AxesPrev[r.axis] : 0;
}

public class JoystickReader(JoystickState _state)
{
    public double RawAxis(int device, int axis) => _state.RawAxis(device, axis);
    public double RawAxisPrev(int device, int axis) => _state.RawAxisPrev(device, axis);
    public double GetAxis(string axis) => _state.GetAxis(axis);
    public double GetAxisPrev(string axis) => _state.GetAxisPrev(axis);
}

public class JoystickDeviceState
{
    private RawGameController _joystick;
    public JoystickDeviceConfig Config;
    private IFilter[] _axisFilters;
    public double[] AxesRaw, AxesRawPrev;
    public double[] Axes, AxesPrev;
    public bool[] Buttons, ButtonsPrev;
    public GameControllerSwitchPosition[] Switches;

    public JoystickDeviceState(RawGameController joystick, string id, JoystickConfig cfg)
    {
        _joystick = joystick;
        if (!cfg.Devices.ContainsKey(id))
            cfg.Devices[id] = new JoystickDeviceConfig { DeviceId = id, Axes = Ut.NewArray<JoystickDeviceAxisConfig>(joystick.AxisCount, _ => new()) };
        Config = cfg.Devices[id];
        if (Config.Axes.Length != _joystick.AxisCount)
            throw new InvalidOperationException($"Joystick {id} has {_joystick.AxisCount} axes but config specifies {Config.Axes.Length}");
        _axisFilters = Config.Axes.Select(a => Filters.Get(a.Filter)).ToArray();
        AxesRaw = new double[_joystick.AxisCount];
        AxesRawPrev = new double[_joystick.AxisCount];
        Axes = new double[_joystick.AxisCount];
        AxesPrev = new double[_joystick.AxisCount];
        Buttons = new bool[_joystick.ButtonCount];
        ButtonsPrev = new bool[_joystick.ButtonCount];
        Switches = new GameControllerSwitchPosition[_joystick.SwitchCount];
    }

    public void Update()
    {
        (Buttons, ButtonsPrev) = (ButtonsPrev, Buttons);
        (AxesRaw, AxesRawPrev) = (AxesRawPrev, AxesRaw);
        _joystick.GetCurrentReading(Buttons, Switches, AxesRaw);
        for (int i = 0; i < AxesRaw.Length; i++)
            Axes[i] = _axisFilters[i].Step(Config.Axes[i].Map(AxesRaw[i]));
    }
}

public class JoystickConfig
{
    public Dictionary<string, JoystickDeviceConfig> Devices = [];
}

public class JoystickDeviceConfig
{
    public string DeviceId;
    public JoystickDeviceAxisConfig[] Axes;
}

public class JoystickDeviceAxisConfig
{
    public string Mapping;
    public string Filter;
    public double Min = 0, Max = 1;
    public double Center = -999;
    public double Deadzone = 0;
    public double Map(double raw)
    {
        if (raw < Min) return 0;
        if (raw > Max) return 1;
        if (Center == -999) return Util.Linterp(Min, Max, 0, 1, raw);
        if (raw < Center - Deadzone) return Util.Linterp(Min, Center - Deadzone, 0.0, 0.5, raw);
        if (raw > Center + Deadzone) return Util.Linterp(Center + Deadzone, Max, 0.5, 1.0, raw);
        return 0.5;
    }
}
