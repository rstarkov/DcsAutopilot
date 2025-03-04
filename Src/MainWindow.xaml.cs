using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RT.Util.ExtensionMethods;
using RT.Util.Forms;
using static DcsAutopilot.Globals;

namespace DcsAutopilot;

public partial class MainWindow : ManagedWindow
{
    private DispatcherTimer _refreshTimer = new();
    private DispatcherTimer _sliderTimer = new();
    private SmoothMover _sliderMover = new(10.0, -1, 1);
    private FlightControllerBase _ctrl;
    private BobbleheadWindow _bobblehead;

    public MainWindow() : base(App.Settings.MainWindow)
    {
        InitializeComponent();
        Dcs = new();
        Dcs.LoadConfig();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(100);
        _refreshTimer.Tick += refreshTimer_Tick;
        _sliderTimer.Interval = TimeSpan.FromMilliseconds(10);
        _sliderTimer.Tick += _sliderTimer_Tick;
        _sliderTimer.Start();
        Dcs.FlightControllers.Clear();
        Dcs.FlightControllers.Add(new ChartPopulate());
        Dcs.FlightControllers.Add(_ctrl = new RollAutoTrim());
        Dcs.FlightControllers.Add(new SmartThrottle());
        foreach (var c in Dcs.FlightControllers)
            c.Dcs = Dcs;
        ctControllers.ItemsSource = Dcs.FlightControllers;

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
        uiSmartThrottle.UpdateGuiTimer();
        uiRollAutoTrim.UpdateGuiTimer();
        uiChart.UpdateGuiTimer();
        ctWindComp.UpdateGuiTimer();
        ctWindDir.UpdateGuiTimer();

        var status = Dcs.Status;
        if (status == "Active control" && (DateTime.UtcNow - Dcs.LastFrameUtc).TotalMilliseconds > 250)
            status = $"Stalled; waiting for DCS";
        if (Dcs.Warnings.Count > 0)
            status = $"{Dcs.Warnings.Count} warnings: {Dcs.Warnings.First()} ...";
        lblStatus.Content = status;

        var fpsnew = (DateTime.UtcNow, Dcs.Frames);
        _fps.Enqueue(fpsnew);
        while (_fps.Count > 0 && _fps.Peek().ts < DateTime.UtcNow.AddSeconds(-1))
            _fps.Dequeue();
        var fps = _fps.Count > 1 ? (fpsnew.Item2 - _fps.Peek().frames) / (fpsnew.Item1 - _fps.Peek().ts).TotalSeconds : 0;
        var latency = Dcs.Latencies.Count() > 0 ? Dcs.Latencies.Average() : 0;
        var statsStr = $"FPS: {fps:0}   Skips: {Dcs.Skips:#,0} of {Dcs.Frames:#,0}   Bytes: {Dcs.LastFrameBytes:#,0}";
        if (latency > 0)
            statsStr = $"Latency: {latency * 1000:0.0}ms   " + statsStr;
        lblStats.Content = statsStr;

        var sb = new StringBuilder();
        sb.AppendLine($"Altitude ASL: {Dcs.LastFrame?.AltitudeAsl.MetersToFeet():#,0.000} ft");
        sb.AppendLine($"Vertical speed: {Dcs.LastFrame?.SpeedVertical.MetersToFeet() * 60:#,0.000} ft/min");
        sb.AppendLine($"AoA: {Dcs.LastFrame?.AngleOfAttack:0.00}°    AoSS: {Dcs.LastFrame?.AngleOfSideSlip:0.000}°");
        sb.AppendLine($"Mach: {Dcs.LastFrame?.SpeedMach:0.00000}    IAS: {Dcs.LastFrame?.SpeedIndicated.MsToKts():0.0} kts");
        sb.AppendLine($"Pitch: {Dcs.LastFrame?.Pitch:0.00}°/{Dcs.LastFrame?.VelPitch:0.00}°   Bank: {Dcs.LastFrame?.Bank:0.00}°   Hdg: {Dcs.LastFrame?.Heading:0.00}°");
        sb.AppendLine($"Gyros: pitch={Dcs.LastFrame?.GyroPitch:0.00}   roll={Dcs.LastFrame?.GyroRoll:0.00}   yaw={Dcs.LastFrame?.GyroYaw:0.00}");
        sb.AppendLine($"Joystick: {Dcs.LastFrame?.JoyPitch:0.000}   {Dcs.LastFrame?.JoyRoll:0.000}   {Dcs.LastFrame?.JoyYaw:0.000}   T:{Dcs.LastFrame?.JoyThrottle1:0.000}");
        sb.AppendLine($"Flaps: {Dcs.LastFrame?.Flaps:0.000}   Speedbrakes: {Dcs.LastFrame?.Airbrakes:0.000}   Gear: {Dcs.LastFrame?.LandingGear:0.000}");
        sb.AppendLine($"Acc: {Dcs.LastFrame?.AccX:0.000} / {Dcs.LastFrame?.AccY:0.000} / {Dcs.LastFrame?.AccZ:0.000}");
        sb.AppendLine($"Test: {Dcs.LastFrame?.FuelFlow:#,0} / {Dcs.LastFrame?.Test1:0.000} / {Dcs.LastFrame?.Test2:0.000} / {Dcs.LastFrame?.Test3:0.000} / {Dcs.LastFrame?.Test4:0.000}");
        sb.AppendLine("Controller: " + _ctrl?.Status);
        sb.AppendLine();
        lblInfo.Text = sb.ToString();

        void setSlider(Slider sl, double? value)
        {
            sl.IsEnabled = Dcs.IsRunning ? value != null : false;
            sl.Value = Dcs.IsRunning ? value ?? 0 : 0;
        }
        setSlider(ctrlPitch, -Dcs.LastControl?.PitchAxis);
        setSlider(ctrlRoll, Dcs.LastControl?.RollAxis);
        setSlider(ctrlYaw, Dcs.LastControl?.YawAxis);
        setSlider(ctrlThrottle, Dcs.LastControl?.ThrottleAxis);
    }

    private void btnStart_Click(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Start();
        refreshTimer_Tick(sender, null);
        Dcs.Start();
        UpdateGui();
    }

    private void btnStop_Click(object sender, RoutedEventArgs e)
    {
        Dcs.Stop();
        Dcs.SaveConfig();
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
        if (!Dcs.FlightControllers.OfType<BobbleheadController>().Any())
            Dcs.FlightControllers.Add(new BobbleheadController());
        var ctrl = Dcs.FlightControllers.OfType<BobbleheadController>().Single();
        _bobblehead.Closing += delegate { _bobblehead = null; ctrl.Window = null; };
        _bobblehead.Show();
        ctrl.Window = _bobblehead;
    }

    private void ControllerButton_Click(object sender, RoutedEventArgs e)
    {
        var ctrl = (FlightControllerBase)(ctControllers.SelectedItem ?? _ctrl);
        var signal = ((Button)sender).Content.ToString();
        if (ctrl.Enabled)
            ctrl.HandleSignal(signal);
    }

    private void UpdateGui()
    {
        btnStart.IsEnabled = !Dcs.IsRunning;
        btnStop.IsEnabled = Dcs.IsRunning;
        uiSmartThrottle.UpdateGui();
        uiRollAutoTrim.UpdateGui();
        uiChart.UpdateGui();
        refreshTimer_Tick(null, null);
    }
}
