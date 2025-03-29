using RT.Util.Geometry;

namespace DcsAutopilot;

public class Curve
{
    public List<CurveSeg> Segments = [];

    public double Calc(double x)
    {
        if (x < Segments[0].ToX)
            return Segments[0].Calc(x);
        if (x >= Segments[^1].FromX)
            return Segments[^1].Calc(x);
        foreach (var seg in Segments)
            if (x >= seg.FromX && x < seg.ToX)
                return seg.Calc(x);
        throw new Exception();
    }

    public void Add(CurveSeg seg)
    {
        Segments.Add(seg);
    }

    public void AddPolyline(params (double x, double y)[] pts)
    {
        for (int i = 0; i < pts.Length - 1; i++)
            Add(LineCurveSeg.FromPts(pts[i], pts[i + 1]));
    }
}

public abstract class CurveSeg
{
    public double FromX, ToX;
    public double Misc1;
    public CurveSeg(double fromX, double toX)
    {
        FromX = fromX;
        ToX = toX;
    }
    public abstract double Calc(double x);
    public abstract string ToCsharp();
}

public class LineCurveSeg : CurveSeg
{
    public double Offset, Slope;
    public double FromY => Calc(FromX);
    public double ToY => Calc(ToX);
    public LineCurveSeg(double fromX, double toX) : base(fromX, toX) { }
    public LineCurveSeg(double fromX, double toX, double offset, double slope)
        : base(fromX, toX)
    {
        Offset = offset;
        Slope = slope;
    }
    public static LineCurveSeg FromPts((double x, double y) fr, (double x, double y) to)
    {
        var seg = new LineCurveSeg(fr.x, to.x);
        seg.SetPts(fr, to);
        return seg;
    }
    public override double Calc(double x)
    {
        return Offset + Slope * x;
    }

    public void SetPts((double x, double y) fr, (double x, double y) to)
    {
        FromX = fr.x;
        ToX = to.x;
        Slope = (to.y - fr.y) / (to.x - fr.x);
        Offset = fr.y - Slope * fr.x;
    }

    public override string ToCsharp() => $"LineSegment.FromPts(({FromX:0.#####}, {FromY:0..#####}), ({ToX:0..#####}, {ToY:0..#####}))";

    public EdgeD Edge => new(FromX, FromY, ToX, ToY);
}

public class SineCurveSeg : CurveSeg
{
    public double Offset, Ampl, Freq, Phase;
    public SineCurveSeg(double fromX, double toX) : base(fromX, toX) { }
    public SineCurveSeg(double fromX, double toX, double offset, double amplitude, double freq, double phase)
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

    public override string ToCsharp() => $"new SineSegment({FromX:0..#####}, {ToX:0..#####}, {Offset:0..#####}, {Ampl:0..#####}, {Freq:0..#####}, {Phase:0..#####})";
}
