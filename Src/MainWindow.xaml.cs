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
    private BobbleheadWindow _bobblehead;

    public MainWindow() : base(App.Settings.MainWindow)
    {
        InitializeComponent();
        Dcs = new();
        Dcs.LoadConfig();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(100);
        _refreshTimer.Tick += refreshTimer_Tick;
        Dcs.FlightControllers.Clear();
        Dcs.FlightControllers.Add(new ChartPopulate());
        Dcs.FlightControllers.Add(new RollAutoTrim());
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

    private Queue<(DateTime ts, int frames)> _fps = new();

    private void refreshTimer_Tick(object sender, EventArgs e)
    {
        uiSmartThrottle.UpdateGuiTimer();
        uiRollAutoTrim.UpdateGuiTimer();
        uiChart.UpdateGuiTimer();
        ctWindComp.UpdateGuiTimer();
        ctWindDir.UpdateGuiTimer();
        uiInfoDump.UpdateGuiTimer();
        uiControlPositions.UpdateGuiTimer();

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
        var ctrl = (FlightControllerBase)(ctControllers.SelectedItem);
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
