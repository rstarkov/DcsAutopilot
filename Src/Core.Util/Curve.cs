using RT.Util.Geometry;

namespace DcsAutopilot;

public abstract class CurveSegment
{
    public double FromX, ToX;
    public double Misc1;
    public CurveSegment(double fromX, double toX)
    {
        FromX = fromX;
        ToX = toX;
    }
    public abstract double Calc(double x);
}

public class LineSegment : CurveSegment
{
    public double Offset, Slope;
    public LineSegment(double fromX, double toX) : base(fromX, toX) { }
    public LineSegment(double fromX, double toX, double offset, double slope)
        : base(fromX, toX)
    {
        Offset = offset;
        Slope = slope;
    }
    public static LineSegment FromPts((double x, double y) fr, (double x, double y) to)
    {
        double slope = (to.y - fr.y) / (to.x - fr.x);
        double offset = fr.y - slope * fr.x;
        return new LineSegment(fr.x, to.x, offset, slope);
    }
    public override double Calc(double x)
    {
        return Offset + Slope * x;
    }
    public EdgeD Edge => new(FromX, Calc(FromX), ToX, Calc(ToX));
}

public class SineSegment : CurveSegment
{
    public double Offset, Ampl, Freq, Phase;
    public SineSegment(double fromX, double toX) : base(fromX, toX) { }
    public SineSegment(double fromX, double toX, double offset, double amplitude, double freq, double phase)
        : base(fromX, toX)
    {
        Offset = offset;
        Ampl = amplitude;
        Freq = freq;
        Phase = phase;
    }
    public override double Calc(double x)
    {
        return Offset + Ampl * Math.Sin(Freq * x + Phase);
    }
}
