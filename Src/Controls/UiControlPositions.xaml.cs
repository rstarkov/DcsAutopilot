using System.Windows.Controls;
using System.Windows.Media;
using static DcsAutopilot.Globals;

namespace DcsAutopilot;

public partial class UiControlPositions : UserControl
{
    private Pen _penAxis = new(Brushes.Gray, 0.5);
    private Pen _penTick = new(Brushes.Gray, 1.0);
    private Pen _penInner = new(Brushes.Yellow, 2.0);
    private Geometry _tickmark;
    private double _tickmarkSize = 4.0;

    public UiControlPositions()
    {
        InitializeComponent();
        _penAxis.Freeze();
        _penTick.Freeze();
        _penInner.Freeze();
        _tickmark = new PathGeometry([new PathFigure(new(0, 0), new[] { new LineSegment(new(_tickmarkSize, _tickmarkSize), true), new LineSegment(new(-_tickmarkSize, _tickmarkSize), true) }, true)]);
        _tickmark.Freeze();
    }

    public void UpdateGuiTimer()
    {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        void drawSlider(int layer, double length, double width, bool vert, double xc, double yc, double? outer, double? inner, bool skipCenter = false)
        {
            if (vert)
                dc.PushTransform(new RotateTransform(90, xc, yc));
            if (layer == 0)
            {
                dc.DrawLine(_penAxis, new(xc - length / 2, yc), new(xc + length / 2, yc));
                for (var x = -length / 2; x <= length / 1.99; x += length / 4)
                {
                    if (skipCenter && x >= -0.01 * length && x <= 0.01 * length)
                        continue;
                    dc.DrawLine(_penTick, new(xc + x, yc - 0.5 * width), new(xc + x, yc - 0.35 * width));
                    dc.DrawLine(_penTick, new(xc + x, yc + 0.5 * width), new(xc + x, yc + 0.35 * width));
                }
            }
            if (layer == 1)
            {
                if (outer != null)
                {
                    dc.PushTransform(new TranslateTransform(xc + outer.Value * length, yc + 0.3 * width));
                    dc.DrawGeometry(Brushes.Red, null, _tickmark);
                    dc.Pop();
                    dc.PushTransform(new TranslateTransform(xc + outer.Value * length, yc - 0.3 * width));
                    dc.PushTransform(new RotateTransform(180));
                    dc.DrawGeometry(Brushes.Red, null, _tickmark);
                    dc.Pop();
                    dc.Pop();
                }
            }
            if (layer == 2)
            {
                if (inner != null)
                    dc.DrawLine(_penInner, new(xc + inner.Value * length, yc - 0.25 * width), new(xc + inner.Value * length, yc + 0.25 * width));
            }
            if (vert)
                dc.Pop();
        }
        void drawAll(int layer)
        {
            // Throttle
            drawSlider(layer, 100, 15, true, 10, 50, 0.5 - Dcs?.Joystick?.GetAxis("throttle"), -Util.Linterp(0, 2, -0.5, 0.5, Dcs?.LastControl?.ThrottleAxis));
            // Pitch
            drawSlider(layer, 100, 15, true, 100, 50, Dcs?.Joystick?.GetAxis("pitch") - 0.5, Util.Linterp(-1, 1, -0.5, 0.5, Dcs?.LastControl?.PitchAxis), true);
            // Roll
            drawSlider(layer, 100, 15, false, 100, 50, Dcs?.Joystick?.GetAxis("roll") - 0.5, Util.Linterp(-1, 1, -0.5, 0.5, Dcs?.LastControl?.RollAxis), true);
            // Rudder
            drawSlider(layer, 100, 15, false, 100, 150 - 7.5, Dcs?.Joystick?.GetAxis("yaw") - 0.5, Util.Linterp(-1, 1, -0.5, 0.5, Dcs?.LastControl?.YawAxis));
        }

        //dc.DrawRectangle(Brushes.DarkRed, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (ActualWidth > ActualHeight)
        {
            dc.PushTransform(new TranslateTransform(ActualWidth / 2 - 150 / 2 * (ActualHeight / 150), 0));
            dc.PushTransform(new ScaleTransform(ActualHeight / 150, ActualHeight / 150));
        }
        else
        {
            dc.PushTransform(new TranslateTransform(0, ActualHeight / 2 - 150 / 2 * (ActualWidth / 150)));
            dc.PushTransform(new ScaleTransform(ActualWidth / 150, ActualWidth / 150));
        }
        //dc.DrawRectangle(Brushes.DarkBlue, null, new Rect(0, 0, 150, 150));
        drawAll(0);
        drawAll(1);
        drawAll(2);
        dc.Pop();
        dc.Pop();
    }
}
