using System.Text;
using System.Windows.Controls;
using RT.Util.ExtensionMethods;
using static DcsAutopilot.Globals;

namespace DcsAutopilot;

public partial class UiChart : UserControl
{
    public UiChart()
    {
        InitializeComponent();
        if (Dcs?.FlightControllers != null)
            Dcs.FlightControllers.CollectionChanged += (_, _) => pnlMain.DataContext = Dcs.GetController<ChartPopulate>();
    }

    private void btnOnOff_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var ctrl = Dcs.GetController<ChartPopulate>(orAdd: true);
        ctrl.Enabled = !ctrl.Enabled;
        UpdateGui();
        UpdateGuiTimer();
    }

    public void UpdateGui()
    {
    }

    public void UpdateGuiTimer()
    {
        var ctrl = Dcs.GetController<ChartPopulate>();
        if (ctrl?.Enabled != true)
            return;
        ctChart.Data = ctrl.Data;
        ctChart.InvalidateVisual();

        var times = ctrl.Data.Times.ToList();
        string oscPeriod(ChartLine line)
        {
            var tgt = line.Data.Count == 0 ? 0 : line.Data.Average();
            var intersections = line.Data.ConsecutivePairs(false).SelectIndexWhere(p => p.Item1 < tgt && p.Item2 > tgt).ToList();
            var periods = intersections.Select(i => times[i]).SelectConsecutivePairs(false, (p1, p2) => p2 - p1).Order().ToList();
            return periods.Count < 3 ? "n/a" : periods[periods.Count / 2].Rounded();
        }
        var sb = new StringBuilder();
        sb.Append("Oscillation:");
        foreach (var kvp in ctrl.Data.Lines)
            sb.Append($"  {kvp.Key}={oscPeriod(kvp.Value)}");
        lblChartInfo.Text = sb.ToString();
    }
}

public class ChartPopulate : FlightControllerBase
{
    private int _skip = 0;
    public ChartData Data = new();

    public override string Name { get; set; } = "Chart Populate";

    public override void Reset()
    {
        foreach (var line in Data.Lines.Values)
            line.Data.Clear();
    }

    public override ControlData ProcessFrame(FrameData frame)
    {
        if (_skip % 3 == 0)
        {
            Data.Times.Enqueue(frame.SimTime);
            Data["VelPitch"].Data.Enqueue(frame.VelPitch);
            Data["Spd"].Data.Enqueue(frame.SpeedCalibrated);
            Data["Thr"].Data.Enqueue(Dcs.LastControl?.ThrottleAxis ?? 0);
        }
        _skip++;
        return null;
    }
}
