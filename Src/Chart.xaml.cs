using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public class ChartLine
{
    public ConcurrentQueue<double> Data = new();
    public Pen Pen = new(Brushes.White, 1.0);
}

public partial class Chart : UserControl
{
    public ConcurrentBag<ChartLine> Lines = new();
    public ConcurrentQueue<double> Times = new();

    public Chart()
    {
        InitializeComponent();
        //CompositionTarget.Rendering += delegate { InvalidateVisual(); }; // too expensive for no gain; this doesn't have to be pretty or smooth
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var xscale = 1 / 3.0;
        foreach (var line in Lines)
        {
            if (line.Data.Count < 2)
                continue;
            Point? ptPrev = null;
            while (line.Data.Count > ActualWidth / xscale)
                line.Data.TryDequeue(out _);
            var mm = line.Data.MinMaxSumCount();
            if (mm.Max == mm.Min)
                continue;
            var yscale = ActualHeight / (mm.Max - mm.Min);
            var x = 0;
            foreach (var y in line.Data)
            {
                var pt = new Point(xscale * x, ActualHeight - (y - mm.Min) * yscale);
                if (ptPrev != null)
                    dc.DrawLine(line.Pen, ptPrev.Value, pt);
                x++;
                ptPrev = pt;
            }
            //dc.DrawGeometry(null, line.Pen, geometry); // far far slower than the lines
        }
        while (Times.Count > ActualWidth / xscale)
            Times.TryDequeue(out _);
    }
}
