using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RT.Util.Forms;

namespace DcsAutopilot;

public partial class MainWindow : ManagedWindow
{
    private DcsController _dcs;
    private DispatcherTimer _refreshTimer = new();
    private DispatcherTimer _sliderTimer = new();
    private SmoothMover _sliderMover = new(10.0, -1, 1);

    public MainWindow() : base(App.Settings.MainWindow)
    {
        InitializeComponent();
        _dcs = new();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(100);
        _refreshTimer.Tick += refreshTimer_Tick;
        _sliderTimer.Interval = TimeSpan.FromMilliseconds(10);
        _sliderTimer.Tick += _sliderTimer_Tick;
        _sliderTimer.Start();
    }

    private void _sliderTimer_Tick(object sender, EventArgs e)
    {
        var tgt = ctSliderTest2.Value / 1000.0;
        ctSliderTest1.Value = _sliderMover.MoveTo(tgt, (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds) * 1000.0;
    }

    private Queue<(DateTime ts, int frames)> _fps = new();

    private void refreshTimer_Tick(object sender, EventArgs e)
    {
        var status = _dcs.Status;
        if (status == "Active control" && (DateTime.UtcNow - _dcs.LastFrameUtc).TotalMilliseconds > 250)
            status = $"Stalled; waiting for DCS";
        lblStatus.Content = status;

        var fpsnew = (DateTime.UtcNow, _dcs.Frames);
        _fps.Enqueue(fpsnew);
        while (_fps.Count > 0 && _fps.Peek().ts < DateTime.UtcNow.AddSeconds(-1))
            _fps.Dequeue();
        var fps = _fps.Count > 1 ? (fpsnew.Item2 - _fps.Peek().frames) / (fpsnew.Item1 - _fps.Peek().ts).TotalSeconds : 0;
        var latency = _dcs.Latencies.Count() > 0 ? _dcs.Latencies.Average() : 0;
        lblStats.Content = $"FPS: {fps:0}, latency: {latency * 1000:0.0}ms. Frames: {_dcs.Frames:#,0}, skips: {_dcs.Skips:#,0}. Bytes: {_dcs.LastFrameBytes:#,0}";

        void setSlider(Slider sl, double? value)
        {
            sl.IsEnabled = _dcs.IsRunning ? value != null : false;
            sl.Value = _dcs.IsRunning ? value ?? 0 : 0;
        }
        setSlider(ctrlPitch, -_dcs.LastControl?.PitchAxis);
        setSlider(ctrlRoll, _dcs.LastControl?.RollAxis);
        setSlider(ctrlYaw, _dcs.LastControl?.YawAxis);
        setSlider(ctrlThrottle, _dcs.LastControl?.ThrottleAxis);
    }

    private void btnStart_Click(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Start();
        _dcs.Start(new BasicAltitudeController());
    }

    private void btnStop_Click(object sender, RoutedEventArgs e)
    {
        _dcs.Stop();
        _refreshTimer.Stop();
        refreshTimer_Tick(sender, null);
    }
}
