﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Forms;
using RT.Util.Geometry;
using Windows.Gaming.Input;

namespace DcsAutopilot;

public partial class MainWindow : ManagedWindow
{
    private DcsController _dcs;
    private DispatcherTimer _refreshTimer = new();
    private DispatcherTimer _sliderTimer = new();
    private SmoothMover _sliderMover = new(10.0, -1, 1);
    private IFlightController _ctrl;
    public static ChartLine LineR = new(), LineG = new(), LineY = new();
    private RawGameController _joystick;
    private double[] _joyAxes;
    private bool[] _joyButtons;
    private GameControllerSwitchPosition[] _joySwitches;
    private Brush _brushToggleBorderNormal = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
    private Brush _brushToggleBorderActive = new SolidColorBrush(Color.FromRgb(0x00, 0x99, 0x07)); // 1447FF
    private Brush _brushToggleBorderHigh = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));
    private Brush _brushToggleBackNormal = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private Brush _brushToggleBackActive = new SolidColorBrush(Color.FromRgb(0xB5, 0xFF, 0xA3)); // BFFAFF
    private Brush _brushToggleBackHigh = new SolidColorBrush(Color.FromRgb(0xFF, 0xDE, 0xDB));

    public MainWindow() : base(App.Settings.MainWindow)
    {
        InitializeComponent();
        _dcs = new();
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

        RawGameController.RawGameControllers.ToList(); // oddly enough the first call to this returns nothing; second call succeeds
        _joystick = RawGameController.RawGameControllers.FirstOrDefault();
        _joyAxes = new double[_joystick?.AxisCount ?? 0];
        _joySwitches = new GameControllerSwitchPosition[_joystick?.SwitchCount ?? 0];
        _joyButtons = new bool[_joystick?.ButtonCount ?? 0];

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
        _joystick?.GetCurrentReading(_joyButtons, _joySwitches, _joyAxes);
        WithController<HornetSmartThrottle>(hct =>
        {
            hct.ThrottleInput = Util.Linterp(0.082, 0.890, 0, 1, _joyAxes[4]);
            btnSmartThrottleAfterburner.BorderBrush = hct.AfterburnerActive ? _brushToggleBorderHigh : hct.AllowAfterburner ? _brushToggleBorderActive : _brushToggleBorderNormal;
            btnSmartThrottleSpeedbrake.BorderBrush = hct.SpeedbrakeActive ? _brushToggleBorderHigh : hct.AllowSpeedbrake ? _brushToggleBorderActive : _brushToggleBorderNormal;
            btnSmartThrottleAfterburner.Background = hct.AfterburnerActive ? _brushToggleBackHigh : hct.AllowAfterburner ? _brushToggleBackActive : _brushToggleBackNormal;
            btnSmartThrottleSpeedbrake.Background = hct.SpeedbrakeActive ? _brushToggleBackHigh : hct.AllowSpeedbrake ? _brushToggleBackActive : _brushToggleBackNormal;
            lblSmartThrottle.Content = !hct.Enabled ? "off" : hct.TargetSpeedIasKts == null ? hct.Status : $"{hct.TargetSpeedIasKts:0} kt";
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
        sb.AppendLine($"Pitch: {_dcs.LastFrame?.Pitch:0.00}°   Bank: {_dcs.LastFrame?.Bank:0.00}°   Hdg: {_dcs.LastFrame?.Heading:0.00}°");
        sb.AppendLine($"Gyros: pitch={_dcs.LastFrame?.GyroPitch:0.00}   roll={_dcs.LastFrame?.GyroRoll:0.00}   yaw={_dcs.LastFrame?.GyroYaw:0.00}");
        sb.AppendLine($"Joystick: {_dcs.LastFrame?.JoyPitch:0.000}   {_dcs.LastFrame?.JoyRoll:0.000}   {_dcs.LastFrame?.JoyYaw:0.000}   T:{_dcs.LastFrame?.JoyThrottle1:0.000}");
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

    private class ChartPopulate : IFlightController
    {
        private MainWindow _wnd;
        private int _skip = 0;

        public bool Enabled { get; set; } = true;

        public ChartPopulate(MainWindow wnd)
        {
            _wnd = wnd;
        }

        public string Status => "";

        public void NewSession(BulkData bulk)
        {
        }

        public void ProcessBulkUpdate(BulkData bulk)
        {
        }

        public ControlData ProcessFrame(FrameData frame)
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
        foreach (var line in ctChart.Lines)
            line.Data.Clear();
        _dcs.FlightControllers.Clear();
        _dcs.FlightControllers.Add(new ChartPopulate(this));
        _dcs.FlightControllers.Add(_ctrl = new HornetAutoTrim());
        _dcs.FlightControllers.Add(new HornetSmartThrottle() { Enabled = true });
        _dcs.Start();
        UpdateGui();
    }

    private void btnStop_Click(object sender, RoutedEventArgs e)
    {
        _dcs.Stop();
        _refreshTimer.Stop();
        refreshTimer_Tick(sender, null);
        lblStats.Content = "";
        _fps.Clear();
        UpdateGui();
    }

    private void HornetAutoTrim_Toggle(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        var c = _dcs.FlightControllers.OfType<HornetAutoTrim>().Single();
        c.Enabled = !c.Enabled;
        UpdateGui();
    }

    private void btnSmartThrottleAfterburner_Click(object sender, RoutedEventArgs e)
    {
        WithController<HornetSmartThrottle>(c => c.AllowAfterburner = !c.AllowAfterburner);
    }

    private void btnSmartThrottleSpeedbrake_Click(object sender, RoutedEventArgs e)
    {
        WithController<HornetSmartThrottle>(c => c.AllowSpeedbrake = !c.AllowSpeedbrake);
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
        btnHornetAutoTrim.IsEnabled = _dcs.FlightControllers.OfType<HornetAutoTrim>().Any();
        btnHornetAutoTrim.IsChecked = btnHornetAutoTrim.IsEnabled ? _dcs.FlightControllers.OfType<HornetAutoTrim>().Single().Enabled : false;
        btnHornetAutoTrim.Content = $"Hornet auto-trim: {(btnHornetAutoTrim.IsChecked == true ? "ON" : "off")}";
        _updating = false;
    }
}
