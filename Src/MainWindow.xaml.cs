using System;
using System.Windows;
using System.Windows.Threading;
using RT.Util.Forms;

namespace DcsAutopilot;

public partial class MainWindow : ManagedWindow
{
    private DcsController _dcs;
    private DispatcherTimer _refreshTimer = new();

    public MainWindow() : base(App.Settings.MainWindow)
    {
        InitializeComponent();
        _dcs = new();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(100);
        _refreshTimer.Tick += refreshTimer_Tick;
    }

    private void refreshTimer_Tick(object sender, EventArgs e)
    {
        var status = _dcs.Status;
        if (status == "Active control" && (DateTime.UtcNow - _dcs.LastFrameUtc).TotalMilliseconds > 250)
            status = $"Stalled; waiting for DCS";
        lblStatus.Content = status;
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
