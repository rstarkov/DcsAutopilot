namespace DcsAutopilot;

public static class Filters
{
    public static IFilter None => new NullFilter();
    public static IFilter BesselD5 => new IirOrder2Filter { B0 = 0.022562748285759465, B1 = 0.04512549657151893, B2 = 0.022562748285759465, A1 = 1.4585675700036072, A2 = -0.548818563146645 }; // fs=0.05460; 5 sample delay at f=0
    public static IFilter BesselD10 => new IirOrder2Filter { B0 = 0.006480433048102745, B1 = 0.01296086609620549, B2 = 0.006480433048102745, A1 = 1.7148814350679784, A2 = -0.7408031672603894 }; // fs=0.02750; 10 sample delay at f=0
    public static IFilter BesselD20 => new IirOrder2Filter { B0 = 0.0017407547134617684, B1 = 0.0034815094269235367, B2 = 0.0017407547134617684, A1 = 1.8537602284019106, A2 = -0.8607232472557577 }; // fs=0.01377; 20 sample delay at f=0
}

public interface IFilter
{
    IFilter New();
    double Step(double v);
}

public class NullFilter : IFilter
{
    public IFilter New() => new NullFilter();
    public double Step(double v) => v;
}

public class IirOrder2Filter : IFilter
{
    private double _x1, _x2;
    private double _y1, _y2;
    private bool _first = true;

    public double A1, A2, B0, B1, B2;

    public IFilter New() => new IirOrder2Filter { A1 = A1, A2 = A2, B0 = B0, B1 = B1, B2 = B2 };

    public double Step(double x)
    {
        if (double.IsNaN(x)) throw new ArgumentException();
        if (double.IsInfinity(x)) throw new ArgumentException();
        if (_first)
        {
            _x1 = _x2 = x;
            _y1 = _y2 = x;
            _first = false;
        }
        // based on https://www.micromodeler.com/dsp/
        var result = _x2 * B2 + _x1 * B1 + x * B0 + _y2 * A2 + _y1 * A1;
        if (double.IsNaN(result)) throw new Exception();
        if (double.IsInfinity(result)) throw new Exception();
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = result;
        return result;
    }
}
