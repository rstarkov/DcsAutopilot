using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RT.Util.ExtensionMethods;
using RT.Util.Forms;
using RT.Util.Geometry;

namespace DcsAutopilot;

public partial class MainWindow : ManagedWindow
{
    private DcsController _dcs;
    private DispatcherTimer _refreshTimer = new();
    private DispatcherTimer _sliderTimer = new();
    private SmoothMover _sliderMover = new(10.0, -1, 1);
    private FlightControllerBase _ctrl;
    public static ChartLine LineR = new(), LineG = new(), LineY = new();
    private Brush _brushToggleBorderNormal = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
    private Brush _brushToggleBorderActive = new SolidColorBrush(Color.FromRgb(0x00, 0x99, 0x07)); // 1447FF
    private Brush _brushToggleBorderHigh = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));
    private Brush _brushToggleBackNormal = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private Brush _brushToggleBackActive = new SolidColorBrush(Color.FromRgb(0xB5, 0xFF, 0xA3)); // BFFAFF
    private Brush _brushToggleBackHigh = new SolidColorBrush(Color.FromRgb(0xFF, 0xDE, 0xDB));
    private BobbleheadWindow _bobblehead;

    public MainWindow() : base(App.Settings.MainWindow)
    {
        InitializeComponent();
        _dcs = new();
        _dcs.LoadConfig();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(100);
        _refreshTimer.Tick += refreshTimer_Tick;
        _sliderTimer.Interval = TimeSpan.FromMilliseconds(10);
        _sliderTimer.Tick += _sliderTimer_Tick;
        _sliderTimer.Start();
        ctChart.Lines.Add(LineR);
        ctChart.Lines.Add(LineG);
        ctChart.Lines.Add(LineY);
        LineR.Pen = new Pen(Brushes.Red, 1);
        LineG.Pen = new Pen(Brushes.Lime, 1);
        LineY.Pen = new Pen(Brushes.Yellow, 1);
        foreach (var line in ctChart.Lines)
            line.Pen.Freeze();
        _dcs.FlightControllers.Clear();
        _dcs.FlightControllers.Add(new ChartPopulate(this));
        _dcs.FlightControllers.Add(_ctrl = new RollAutoTrim());
        _dcs.FlightControllers.Add(new SmartThrottle());
        ctControllers.ItemsSource = _dcs.FlightControllers;

        btnStop_Click(null, null);
    }

    private void ManagedWindow_SizeLocationChanged(object sender, EventArgs e)
    {
        App.SettingsFile.SaveInBackground();
    }

    private void _sliderTimer_Tick(object sender, EventArgs e)
    {
        var tgt = ctSliderTest2.Value / 1000.0;
        ctSliderTest1.Value = _sliderMover.MoveTo(tgt, (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds) * 1000.0;
    }

    private Queue<(DateTime ts, int frames)> _fps = new();

    private void refreshTimer_Tick(object sender, EventArgs e)
    {
        WithController<RollAutoTrim>(vat =>
        {
            if (!vat.Enabled) return;
            lblAutoTrimRollLabel.Content = vat.UsingBankRate ? "Bank:" : "Roll:";
            lblAutoTrimRoll.Content = _dcs.LastFrame == null ? "?" : vat.UsingBankRate ? (Util.SignStr(_dcs.LastFrame.Bank, "0.00", "⮜ ", "⮞ ", "⬥ ") + "°") : (Util.SignStr(_dcs.LastFrame.GyroRoll, "0.00", "⮜ ", "⮞ ", "⬥ ") + "°/s");
            lblAutoTrimTrim.Content = _dcs.LastFrame?.TrimRoll == null ? "?" : Util.SignStr(_dcs.LastFrame.TrimRoll.Value * 100, "0.0", "⮜ ", "⮞ ", "⬥ ") + "%";
            lblAutoTrimState.Content = vat.Status;
        });
        WithController<SmartThrottle>(hct =>
        {
            btnSmartThrottleAfterburner.BorderBrush = hct.AfterburnerActive ? _brushToggleBorderHigh : hct.AllowAfterburner ? _brushToggleBorderActive : _brushToggleBorderNormal;
            btnSmartThrottleSpeedbrake.BorderBrush = hct.SpeedbrakeActive ? _brushToggleBorderHigh : hct.AllowSpeedbrake ? _brushToggleBorderActive : _brushToggleBorderNormal;
            btnSmartThrottleAfterburner.Background = hct.AfterburnerActive ? _brushToggleBackHigh : hct.AllowAfterburner ? _brushToggleBackActive : _brushToggleBackNormal;
            btnSmartThrottleSpeedbrake.Background = hct.SpeedbrakeActive ? _brushToggleBackHigh : hct.AllowSpeedbrake ? _brushToggleBackActive : _brushToggleBackNormal;
            lblSmartThrottle.Content = !hct.Enabled ? "off" : hct.AutothrottleSpeedKts == null ? hct.Status : $"{hct.AutothrottleSpeedKts:0} kt";
            if (!hct.Enabled || hct.AutothrottleSpeedKts == null)
                lblSmartThrottle.Foreground = Brushes.Black;
            else
                lblSmartThrottle.Foreground = Math.Abs(hct.AutothrottleSpeedKts.Value - (_dcs.LastFrame?.SpeedIndicated ?? 0).MsToKts()) <= 15 ? Brushes.Green : Brushes.DarkRed;
        });

        var status = _dcs.Status;
        if (status == "Active control" && (DateTime.UtcNow - _dcs.LastFrameUtc).TotalMilliseconds > 250)
            status = $"Stalled; waiting for DCS";
        if (_dcs.Warnings.Count > 0)
            status = $"{_dcs.Warnings.Count} warnings: {_dcs.Warnings.First()} ...";
        lblStatus.Content = status;

        var fpsnew = (DateTime.UtcNow, _dcs.Frames);
        _fps.Enqueue(fpsnew);
        while (_fps.Count > 0 && _fps.Peek().ts < DateTime.UtcNow.AddSeconds(-1))
            _fps.Dequeue();
        var fps = _fps.Count > 1 ? (fpsnew.Item2 - _fps.Peek().frames) / (fpsnew.Item1 - _fps.Peek().ts).TotalSeconds : 0;
        var latency = _dcs.Latencies.Count() > 0 ? _dcs.Latencies.Average() : 0;
        var statsStr = $"FPS: {fps:0}   Skips: {_dcs.Skips:#,0} of {_dcs.Frames:#,0}   Bytes: {_dcs.LastFrameBytes:#,0}";
        if (latency > 0)
            statsStr = $"Latency: {latency * 1000:0.0}ms   " + statsStr;
        lblStats.Content = statsStr;

        var sb = new StringBuilder();
        sb.AppendLine($"Altitude ASL: {_dcs.LastFrame?.AltitudeAsl.MetersToFeet():#,0.000} ft");
        sb.AppendLine($"Vertical speed: {_dcs.LastFrame?.SpeedVertical.MetersToFeet() * 60:#,0.000} ft/min");
        sb.AppendLine($"AoA: {_dcs.LastFrame?.AngleOfAttack:0.00}°    AoSS: {_dcs.LastFrame?.AngleOfSideSlip:0.000}°");
        sb.AppendLine($"Mach: {_dcs.LastFrame?.SpeedMach:0.00000}    IAS: {_dcs.LastFrame?.SpeedIndicated.MsToKts():0.0} kts");
        sb.AppendLine($"Pitch: {_dcs.LastFrame?.Pitch:0.00}°/{_dcs.LastFrame?.VelPitch:0.00}°   Bank: {_dcs.LastFrame?.Bank:0.00}°   Hdg: {_dcs.LastFrame?.Heading:0.00}°");
        sb.AppendLine($"Gyros: pitch={_dcs.LastFrame?.GyroPitch:0.00}   roll={_dcs.LastFrame?.GyroRoll:0.00}   yaw={_dcs.LastFrame?.GyroYaw:0.00}");
        sb.AppendLine($"Joystick: {_dcs.LastFrame?.JoyPitch:0.000}   {_dcs.LastFrame?.JoyRoll:0.000}   {_dcs.LastFrame?.JoyYaw:0.000}   T:{_dcs.LastFrame?.JoyThrottle1:0.000}");
        sb.AppendLine($"Flaps: {_dcs.LastFrame?.Flaps:0.000}   Speedbrakes: {_dcs.LastFrame?.Airbrakes:0.000}   Gear: {_dcs.LastFrame?.LandingGear:0.000}");
        sb.AppendLine($"Acc: {_dcs.LastFrame?.AccX:0.000} / {_dcs.LastFrame?.AccY:0.000} / {_dcs.LastFrame?.AccZ:0.000}");
        sb.AppendLine($"Test: {_dcs.LastFrame?.FuelFlow:#,0} / {_dcs.LastFrame?.Test1:0.000} / {_dcs.LastFrame?.Test2:0.000} / {_dcs.LastFrame?.Test3:0.000} / {_dcs.LastFrame?.Test4:0.000}");
        sb.AppendLine("Controller: " + _ctrl?.Status);
        sb.AppendLine();
        lblInfo.Text = sb.ToString();

        void setSlider(Slider sl, double? value)
        {
            sl.IsEnabled = _dcs.IsRunning ? value != null : false;
            sl.Value = _dcs.IsRunning ? value ?? 0 : 0;
        }
        setSlider(ctrlPitch, -_dcs.LastControl?.PitchAxis);
        setSlider(ctrlRoll, _dcs.LastControl?.RollAxis);
        setSlider(ctrlYaw, _dcs.LastControl?.YawAxis);
        setSlider(ctrlThrottle, _dcs.LastControl?.ThrottleAxis);

        ctChart.InvalidateVisual();

        string oscPeriod(ChartLine line)
        {
            var tgt = line.Data.Count == 0 ? 0 : line.Data.Average();
            var intersections = line.Data.ConsecutivePairs(false).SelectIndexWhere(p => p.Item1 < tgt && p.Item2 > tgt).ToList();
            var times = ctChart.Times.ToList();
            var periods = intersections.Select(i => times[i]).SelectConsecutivePairs(false, (p1, p2) => p2 - p1).Order().ToList();
            return periods.Count < 3 ? "n/a" : periods[periods.Count / 2].Rounded();
        }
        lblChartInfo.Text = $"Oscillation:  R={oscPeriod(LineR)}  G={oscPeriod(LineG)}  Y={oscPeriod(LineY)}";

        if (_dcs.LastFrame != null)
        {
            var windabs = new PointD(_dcs.LastFrame.WindX.MsToKts(), _dcs.LastFrame.WindZ.MsToKts());
            var dir = new PointD(_dcs.LastFrame.Heading.ToRad());
            ctWindComp.SetWind(-windabs.Dot(dir), -windabs.Dot(dir.Rotated(Math.PI / 2)));
            ctWindDir.SetWind(windabs.Theta().ToDeg(), windabs.Abs(), _dcs.LastFrame.Heading);
        }
    }

    private class ChartPopulate : FlightControllerBase
    {
        private MainWindow _wnd;
        private int _skip = 0;

        public override string Name { get; set; } = "Chart Populate";

        public ChartPopulate(MainWindow wnd)
        {
            _wnd = wnd;
        }

        public override ControlData ProcessFrame(FrameData frame)
        {
            if (_skip % 3 == 0)
            {
                _wnd.ctChart.Times.Enqueue(frame.SimTime);
                LineY.Data.Enqueue(Math.Atan2(frame.VelY, Math.Sqrt(frame.VelX * frame.VelX + frame.VelZ * frame.VelZ)));
                //LineY.Data.Enqueue(frame.AccY);
                //LineY.Data.Enqueue(frame.Pitch);
                LineR.Data.Enqueue(_wnd._dcs.LastControl?.PitchAxis ?? 0);
                //LineR.Data.Enqueue(frame.Bank);
            }
            _skip++;
            return null;
        }
    }

    private void btnStart_Click(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Start();
        refreshTimer_Tick(sender, null);
        foreach (var line in ctChart.Lines)
            line.Data.Clear();
        foreach (var c in _dcs.FlightControllers)
            if (c.Enabled)
                c.Reset();
        _dcs.Start();
        UpdateGui();
    }

    private void btnStop_Click(object sender, RoutedEventArgs e)
    {
        _dcs.Stop();
        _dcs.SaveConfig();
        _refreshTimer.Stop();
        refreshTimer_Tick(sender, null);
        lblStats.Content = "";
        _fps.Clear();
        UpdateGui();
    }

    private void btnBob_Click(object sender, RoutedEventArgs e)
    {
        if (_bobblehead != null)
            return;
        _bobblehead = new BobbleheadWindow();
        if (!_dcs.FlightControllers.OfType<BobbleheadController>().Any())
            _dcs.FlightControllers.Add(new BobbleheadController());
        var ctrl = _dcs.FlightControllers.OfType<BobbleheadController>().Single();
        _bobblehead.Closing += delegate { _bobblehead = null; ctrl.Window = null; };
        _bobblehead.Show();
        ctrl.Window = _bobblehead;
    }

    private void btnAutoTrimOnOff_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (!_dcs.FlightControllers.OfType<RollAutoTrim>().Any())
            _dcs.FlightControllers.Add(new RollAutoTrim());
        var c = _dcs.FlightControllers.OfType<RollAutoTrim>().Single();
        c.Enabled = !c.Enabled;
        if (c.Enabled)
            c.Reset();
        UpdateGui();
    }

    private void btnSmartThrottleAfterburner_Click(object sender, RoutedEventArgs e)
    {
        WithController<SmartThrottle>(c => c.AllowAfterburner = !c.AllowAfterburner);
    }

    private void btnSmartThrottleSpeedbrake_Click(object sender, RoutedEventArgs e)
    {
        WithController<SmartThrottle>(c => c.AllowSpeedbrake = !c.AllowSpeedbrake);
    }

    private void lblSmartThrottle_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        WithController<SmartThrottle>(c => c.Enabled = !c.Enabled);
    }

    private void ControllerButton_Click(object sender, RoutedEventArgs e)
    {
        var ctrl = (FlightControllerBase)(ctControllers.SelectedItem ?? _ctrl);
        var signal = ((Button)sender).Content.ToString();
        if (ctrl.Enabled)
            ctrl.HandleSignal(signal);
    }

    private void WithController<T>(Action<T> action)
    {
        var c = _dcs.FlightControllers.OfType<T>().FirstOrDefault();
        if (c != null)
            action(c);
    }

    private bool _updating = false;

    private void UpdateGui()
    {
        _updating = true;
        btnStart.IsEnabled = !_dcs.IsRunning;
        btnStop.IsEnabled = _dcs.IsRunning;
        updateAutoTrim();
        _updating = false;
        refreshTimer_Tick(null, null);

        void updateAutoTrim()
        {
            var on = updateCtrlPanel<RollAutoTrim>(pnlAutoTrim, btnAutoTrimOnOff);
            if (!on)
            {
                lblAutoTrimRoll.Content = "?";
                lblAutoTrimTrim.Content = "?";
                lblAutoTrimState.Content = "disabled";
            }
        }

        bool updateCtrlPanel<TCtrl>(DependencyObject panel, Button btnOnOff) where TCtrl : FlightControllerBase
        {
            var c = _dcs.FlightControllers.OfType<TCtrl>().SingleOrDefault();
            if (c?.Enabled == true)
            {
                btnOnOff.Content = "ON";
                enableTree(panel, true);
            }
            else
            {
                btnOnOff.Content = "off";
                enableTree(panel, false);
                enableParents(btnOnOff, panel);
            }
            return c?.Enabled == true;
        }
        void enableTree(DependencyObject obj, bool enable)
        {
            if (obj is Control ctrl)
                ctrl.IsEnabled = enable;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
                enableTree(VisualTreeHelper.GetChild(obj, i), enable);
        }
        void enableParents(DependencyObject obj, DependencyObject stop)
        {
            if (obj == null || obj == stop)
                return;
            if (obj is Control ctrl)
                ctrl.IsEnabled = true;
            enableParents(VisualTreeHelper.GetParent(obj), stop);
        }
    }
}
