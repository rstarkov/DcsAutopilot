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

namespace DcsAutopilot;

public partial class MainWindow : ManagedWindow
{
    private DcsController _dcs;
    private DispatcherTimer _refreshTimer = new();
    private DispatcherTimer _sliderTimer = new();
    private SmoothMover _sliderMover = new(10.0, -1, 1);
    private IFlightController _ctrl;
    public static ChartLine LineR = new(), LineG = new(), LineY = new();

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
        sb.AppendLine($"Joystick: {_dcs.LastFrame?.JoyPitch:0.000}   {_dcs.LastFrame?.JoyRoll:0.000}   {_dcs.LastFrame?.JoyYaw:0.000}");
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

        var tgt = LineY.Data.Count == 0 ? 0 : LineY.Data.Average();
        var intersections = LineY.Data.ConsecutivePairs(false).SelectIndexWhere(p => (p.Item1 < tgt && p.Item2 > tgt) || (p.Item2 < tgt && p.Item2 > tgt)).ToList();
        var times = ctChart.Times.ToList();
        var periods = intersections.Select(i => times[i]).SelectConsecutivePairs(false, (p1, p2) => p2 - p1).Order().ToList();
        var period = periods.Count >= 3 ? periods[periods.Count / 2] : -1;
        if (period != -1)
            Title = period.ToString();
        //var enable = false;
        //if (enable)
        //{
        //    foreach (var pt in LineG.Data.Zip(LineY.Data, LineR.Data))
        //        File.AppendAllLines("lines.csv", new[] { Ut.FormatCsvRow(pt.First, pt.Second, pt.Third) });
        //}
        if (_ctrl?.Status.Contains("pitchdown") == true)
            Background = Brushes.Red;
        else if (_ctrl?.Status.Contains("done") == true)
            Background = Brushes.Blue;
    }

    private class ChartPopulate : IFlightController
    {
        private MainWindow _wnd;
        private int _skip = 0;

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
        _dcs.Start();
        btnStart.IsEnabled = !_dcs.IsRunning;
        btnStop.IsEnabled = _dcs.IsRunning;
    }

    private void btnStop_Click(object sender, RoutedEventArgs e)
    {
        _dcs.Stop();
        _refreshTimer.Stop();
        refreshTimer_Tick(sender, null);
        btnStart.IsEnabled = !_dcs.IsRunning;
        btnStop.IsEnabled = _dcs.IsRunning;
        lblStats.Content = "";
        _fps.Clear();
    }
}
