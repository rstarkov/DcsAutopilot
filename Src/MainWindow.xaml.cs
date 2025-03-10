using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RT.Util.ExtensionMethods;
using RT.Util.Forms;
using static DcsAutopilot.Globals;

namespace DcsAutopilot;

public partial class MainWindow : ManagedWindow
{
    private DispatcherTimer _updateGuiTimer = new();
    private BobbleheadWindow _bobblehead;
    private Queue<(DateTime ts, int frames)> _fps = new();

    public MainWindow() : base(App.Settings.MainWindow)
    {
        Dcs = new();
        Dcs.LoadConfig();
        InitializeComponent();
        _updateGuiTimer.Interval = TimeSpan.FromMilliseconds(100);
        _updateGuiTimer.Tick += UpdateGuiTimer;

        btnStop_Click(null, null);
    }

    private void ManagedWindow_SizeLocationChanged(object sender, EventArgs e)
    {
        App.SettingsFile.SaveInBackground();
    }

    private void UpdateGui()
    {
        btnStart.IsEnabled = !Dcs.IsRunning;
        btnStop.IsEnabled = Dcs.IsRunning;
        uiSmartThrottle.UpdateGui();
        uiRollAutoTrim.UpdateGui();
        uiSoundWarnings.UpdateGui();
        uiChart.UpdateGui();
        UpdateGuiTimer(null, null);
    }

    private void UpdateGuiTimer(object sender, EventArgs e)
    {
        uiSmartThrottle.UpdateGuiTimer();
        uiRollAutoTrim.UpdateGuiTimer();
        uiSoundWarnings.UpdateGuiTimer();
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
        _updateGuiTimer.Start();
        UpdateGuiTimer(sender, null);
        Dcs.Start();
        UpdateGui();
    }

    private void btnStop_Click(object sender, RoutedEventArgs e)
    {
        Dcs.Stop();
        Dcs.SaveConfig();
        _updateGuiTimer.Stop();
        UpdateGuiTimer(sender, null);
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
}
