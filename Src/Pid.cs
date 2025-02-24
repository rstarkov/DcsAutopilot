using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public class BasicPid
{
    public double P { get; set; }
    public double I { get; set; }
    public double D { get; set; }

    public double MinControl { get; set; }
    public double MaxControl { get; set; }
    public double MinTermP { get; set; } = -999;
    public double MaxTermP { get; set; } = 999;
    public double ErrorIntegral { get; set; }
    public double Derivative { get; set; }
    public double DerivativeSmoothing { get; set; } = 0.8;
    public double IntegrationLimit { get; set; } // derivative must be less than this to start integrating error
    private double _prevError = double.NaN;
    public bool Integrating { get; private set; }
    public double Bias { get; set; }

    public double Update(double error, double dt)
    {
        if (double.IsNaN(_prevError))
            Derivative = 0;
        else
            Derivative = DerivativeSmoothing * Derivative + (1 - DerivativeSmoothing) * (error - _prevError) / dt;
        _prevError = error;

        double p = (P * error).Clip(MinTermP, MaxTermP);
        double output = p + I * ErrorIntegral + D * Derivative + Bias;

        output = output.Clip(MinControl, MaxControl);
        Integrating = Math.Abs(Derivative) < IntegrationLimit && output > MinControl && output < MaxControl && p > MinTermP && p < MaxTermP;
        if (Integrating)
            ErrorIntegral += error * dt;
        return output;
    }

    public BasicPid SetP(double p)
    {
        P = p;
        I = 0;
        D = 0;
        return this;
    }

    public BasicPid SetZiNiClassic(double pCritical, double tOsc)
    {
        // https://en.wikipedia.org/wiki/Ziegler%E2%80%93Nichols_method
        P = 0.6 * pCritical;
        I = 1.2 * pCritical / tOsc;
        D = 0.075 * pCritical * tOsc;
        return this;
    }

    public BasicPid SetZiNiSome(double pCritical, double tOsc)
    {
        P = 0.33 * pCritical;
        I = 0.66 * pCritical / tOsc;
        D = 0.11 * pCritical * tOsc;
        return this;
    }

    public BasicPid SetZiNiNone(double pCritical, double tOsc)
    {
        P = 0.20 * pCritical;
        I = 0.40 * pCritical / tOsc;
        D = 0.066 * pCritical * tOsc;
        return this;
    }
}

public class ParamPidPt
{
    public string Name { get; set; }
    public BasicPid Pid { get; set; }
    public Vec Params { get; set; }
    public double Scale { get; set; }

    public ParamPidPt(BasicPid pid, params double[] vec)
    {
        Name = vec.JoinString(", ");
        Pid = pid;
        Params = new Vec(vec);
    }
}

public class ParamPid
{
    public BasicPid PID { get; } = new();
    public List<ParamPidPt> Points;

    private int _dim;
    private List<Line> _lines = new();
    private Vec _min, _max;

    public ParamPid(Vec min, Vec max, params ParamPidPt[] points) : this(min, max, points.AsEnumerable()) { }
    public ParamPid(Vec min, Vec max, IEnumerable<ParamPidPt> points)
    {
        _min = min;
        _max = max;
        Points = points.ToList();
        _dim = Points[0].Params.Count;
        foreach (var pt in Points)
            if (pt.Params.Count != _dim) throw new Exception($"All PIDs must be parameterised to the same number of dimensions.");
        // scale per min/max
        foreach (var pt in Points)
            for (int i = 0; i < pt.Params.Count; i++)
                pt.Params[i] = (pt.Params[i] - min[i]) / (max[i] - min[i]);
        // pre-compute every possible line
        if (Points.Count > 1)
            foreach (var pts in Points.Subsequences(2, 2))
                _lines.Add(new Line(pts.First(), pts.Last()));
        // remove every line that has a point too close to the line and within the segment - as it will also exist as two separate lines, guaranteed
        _lines.RemoveAll(line => Points.Any(pt =>
        {
            var t = line.ProjT(pt.Params);
            var dist = line.Distance(pt.Params, t);
            return t > 0 && t < 1 && dist < 0.01;
        }));
    }

    public void Update(Vec state)
    {
        for (int i = 0; i < state.Count; i++)
            state[i] = (state[i] - _min[i]) / (_max[i] - _min[i]);
        foreach (var pt in Points)
            pt.Scale = 0;
        foreach (var line in _lines)
        {
            var t = line.ProjT(state);
            var dist = line.Distance(state, t);
            var linescale = 1 / dist;
            var pt1scale = t < 0 ? 1 : t > 1 ? 0 : (1 - t);
            var pt2scale = t < 0 ? 0 : t > 1 ? 1 : t;
            line.Point1.Scale += linescale * pt1scale;
            line.Point2.Scale += linescale * pt2scale;
        }
        if (Points.Count == 1)
            Points[0].Scale = 1;
        double totalscale = 0;
        foreach (var pt in Points)
            totalscale += pt.Scale;
        PID.P = 0;
        PID.I = 0;
        PID.D = 0;
        PID.Bias = 0;
        PID.DerivativeSmoothing = 0;
        PID.IntegrationLimit = 0;
        PID.MinControl = 0;
        PID.MaxControl = 0;
        foreach (var pt in Points)
        {
            pt.Scale /= totalscale;
            PID.P += pt.Scale * pt.Pid.P;
            PID.I += pt.Scale * pt.Pid.I;
            PID.D += pt.Scale * pt.Pid.D;
            PID.Bias += pt.Scale * pt.Pid.Bias;
            PID.DerivativeSmoothing += pt.Scale * pt.Pid.DerivativeSmoothing;
            PID.IntegrationLimit += pt.Scale * pt.Pid.IntegrationLimit;
            PID.MinControl += pt.Scale * pt.Pid.MinControl;
            PID.MaxControl += pt.Scale * pt.Pid.MaxControl;
        }
    }

    private class Line
    {
        public ParamPidPt Point1, Point2;
        public Vec Dir;
        private double _1overDirDotDir;

        public Line(ParamPidPt pt1, ParamPidPt pt2)
        {
            Point1 = pt1;
            Point2 = pt2;
            Dir = Point2.Params - Point1.Params;
            _1overDirDotDir = 1 / (Dir * Dir);
        }

        public double ProjT(Vec point) => (point - Point1.Params) * Dir * _1overDirDotDir;

        public double Distance(Vec point, double projT)
        {
            if (projT <= 0)
                return (point - Point1.Params).Abs();
            else if (projT >= 1)
                return (point - Point2.Params).Abs();
            else
                return (point - (Point1.Params + projT * Dir)).Abs();
        }
    }
}
