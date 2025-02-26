using Windows.Gaming.Input;

namespace DcsAutopilot;

public class JoystickState
{
    private RawGameController _joystick;
    public double[] _axes;
    public bool[] _buttons1, _buttons2;
    public GameControllerSwitchPosition[] _switches;
    public JoystickReader Reader;

    public JoystickState()
    {
        RawGameController.RawGameControllers.ToList(); // oddly enough the first call to this returns nothing; second call succeeds
        _joystick = RawGameController.RawGameControllers.FirstOrDefault();
        _axes = new double[_joystick?.AxisCount ?? 0];
        _switches = new GameControllerSwitchPosition[_joystick?.SwitchCount ?? 0];
        _buttons1 = new bool[_joystick?.ButtonCount ?? 0];
        _buttons2 = new bool[_joystick?.ButtonCount ?? 0];
        Reader = new JoystickReader(this);
    }

    public void Update()
    {
        (_buttons1, _buttons2) = (_buttons2, _buttons1);
        _joystick?.GetCurrentReading(_buttons1, _switches, _axes);
    }

    public IEnumerable<(int btnIndex, bool down)> GetChangedButtons()
    {
        // todo: use this to implement HandleButton
        for (int i = 0; i < _buttons1.Length; i++)
            if (_buttons1[i] != _buttons2[i])
                yield return (i, _buttons1[i]);
    }
}

public class JoystickReader(JoystickState _state)
{
    public double? TryAxis(int index) => index >= 0 && index < _state._axes.Length ? _state._axes[index] : null;
}
