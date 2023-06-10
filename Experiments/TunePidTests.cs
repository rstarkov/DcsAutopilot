using DcsAutopilot;
using RT.Util.ExtensionMethods;
using RT.Util.Geometry;

namespace DcsExperiments;

class TunePidTests : MultiTester
{
    private List<string> _log = null;// new();
    private Chart _chart;

    public void Run(string[] args)
    {
        _chart = new Chart("Tune PID");
        _chart.MinX = 0; _chart.MinY = 0;
        _chart.MaxX = 20; _chart.MaxY = 10;
        _chart.GridX = 5; _chart.GridY = 5;
        _chart.Border = 10;
        _chart.Lines["err"].Pen = Pens.Yellow;
        _chart.Lines["best"].Pen = Pens.Gray;
        _chart.Lines["fit"].Pen = Pens.Magenta;
        _chart.Lines["bestfit"].Pen = Pens.Gray;
        new Thread(() => { _chart.ShowDialog(); }) { IsBackground = true }.Start();

        var bestVector = new[] { 0.005, 0.001, 0.001, 0.001, 0.5 };
        var bestEval = Evaluate(1, bestVector);
        _chart.Lines["best"].Points = new(_chart.Lines["err"].Points);
        _chart.Lines["bestfit"].Points = new(_chart.Lines["fit"].Points);
        UpdateChart();

        int noImprovement = 0;

        bool bestImproved(double eval, double[] vector)
        {
            if (eval < bestEval)
                eval = Evaluate(3, vector);
            if (eval < bestEval)
            {
                bestEval = eval;
                bestVector = vector.ToArray();
                Console.Title = $"Best: {num(bestEval)} - {bestVector.Select(v => $"{num(v)}").JoinString(", ")}";
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(Console.Title);
                Console.ResetColor();
                _chart.Lines["best"].Points = new(_chart.Lines["err"].Points);
                _chart.Lines["bestfit"].Points = new(_chart.Lines["fit"].Points);
                UpdateChart();
                noImprovement = 0;
                return true;
            }
            noImprovement++;
            return false;
        }

        while (true)
        {
            Console.WriteLine();
            var scale = Random.Shared.NextDouble(1, 2);
            var variation = bestVector.Select(_ => Random.Shared.NextDouble(1 / scale, scale)).ToArray();
            // forwards
            bool any = false;
            again1:;
            Console.WriteLine($"\nTrying forwards, scale={num(scale)}");
            var vector = bestVector.Select((v, i) => v * variation[i]).ToArray();
            var eval = Evaluate(1, vector);
            if (bestImproved(eval, vector))
            {
                any = true;
                goto again1;
            }
            // backwards
            if (!any)
            {
                again2:;
                Console.WriteLine($"\nTrying backwards, scale={num(scale)}");
                variation = variation.Select(v => 1 / v).ToArray();
                vector = bestVector.Select((v, i) => v * variation[i]).ToArray();
                eval = Evaluate(1, vector);
                if (bestImproved(eval, vector))
                    goto again2;
            }
            // re-evaluate best if we're stuck because our measurements are noisy
            if (noImprovement > 20)
            {
                var was = bestEval;
                bestEval = Evaluate(3, bestVector);
                noImprovement = 0;
                Console.WriteLine($"===== Re-eval: from {num(was)} to {num(bestEval)} =====");
            }
        }
    }

    private double Evaluate(int n, double[] vector)
    {
        var evals = new[] { EvaluateInner(25, vector) }.ToList();
        while (evals.Count < n)
            evals.Add(EvaluateInner(25, vector));
        var firstCross = Math.Pow(evals.Select(e => e.firstCross).Aggregate(1.0, (tot, val) => tot * val), 1.0 / n);
        var maxErrorAfterCross = Math.Pow(evals.Select(e => e.maxErrorAfterCross).Aggregate(1.0, (tot, val) => tot * val), 1.0 / n);
        var totalErrorAfterCross = Math.Pow(evals.Select(e => e.totalErrorAfterCross).Aggregate(1.0, (tot, val) => tot * val), 1.0 / n);
        var smoothness = Math.Pow(evals.Select(e => e.smoothness).Aggregate(1.0, (tot, val) => tot * val), 1.0 / n);
        var expError = evals.Select(e => e.expError).Average();
        //var eval = Math.Pow(firstCross, 0.7) * Math.Pow(maxErrorAfterCross, 3) * Math.Pow(totalErrorAfterCross, 1.5) * smoothness;
        var eval = expError;
        Console.WriteLine($"   AVG={n}: firstCross={num(firstCross)}, maxError={num(maxErrorAfterCross)}, totalError={num(totalErrorAfterCross)}, smoothness={num(smoothness)}, expError={num(expError)}");
        Console.WriteLine($"Evaluating done: {num(eval)}");
        return eval;
    }

    private (double firstCross, double maxErrorAfterCross, double totalErrorAfterCross, double smoothness, double expError) EvaluateInner(double tgt, double[] vector)
    {
        InitialConditions();
        Console.WriteLine($"Evaluating... {vector.Select(v => $"{num(v)}").JoinString(", ")}");
        _chart.Lines["err"].Points.Clear();
        _chart.Lines["fit"].Points.Clear();
        var duration = 20.0;
        var running = false;
        var tstart = -9999.0;
        var totalErrorAfterCross = 0.0;
        var direction = 0;
        var firstCross = duration;
        var maxErrorAfterCross = 0.0;
        var prevGrad = double.NaN;
        var prevE = double.NaN;
        var smoothness = 0.0;
        _log?.Clear();
        var filterVelPitch = Filters.BesselD20;
        var filterDT = Filters.BesselD20;
        _ctrl.Tick = (f) =>
        {
            if (!running)
                return;
            var velpitchFiltered = filterVelPitch.Step(f.VelPitch);
            var dTfiltered = filterDT.Step(f.dT);
            var e = velpitchFiltered - _ctrl.TgtPitch;
            if (tstart < 0 && Math.Abs(e) < 5)
                tstart = f.SimTime;
            if (tstart < 0)
                return;
            var t = f.SimTime - tstart;
            _chart.Lines["err"].Add(t, e);
            _log?.Add($"{t},{velpitchFiltered},{e}");
            if (direction == 0)
                direction = Math.Sign(e);
            if (firstCross == duration && Math.Sign(e) != direction)
            {
                firstCross = t;
                maxErrorAfterCross = 0; // this way if we never cross we still get a measure for these that is pretty large but can shrink
                totalErrorAfterCross = 0;
            }
            maxErrorAfterCross = Math.Max(maxErrorAfterCross, Math.Abs(e));
            totalErrorAfterCross += Math.Abs(e) * t * t * dTfiltered;
            if (t > 1.0)
            {
                var g = (e - prevE) / dTfiltered;
                if (!double.IsNaN(prevGrad))
                    smoothness += (g - prevGrad) * (g - prevGrad);
                prevGrad = g;
            }
            prevE = e;
            if (t >= duration)
                running = false;
        };

        _ctrl.PidVelPitch = new BasicPid { MinControl = -1.0, MaxControl = 0.5, P = vector[0], I = vector[1], D = vector[2], IntegrationLimit = vector[3], DerivativeSmoothing = 0 }; // 1 - 1 / (vector[4] + 1) };
        _ctrl.TgtPitch = tgt;
        running = true; // ordering ensures that we don't integrate anything until all variables are initialised
        while (running)
        {
            Thread.Sleep(100);
            UpdateChart();
        }
        var pts = _chart.Lines["err"].Points.OrderBy(p => p.X).ToArray();
        var (expErr, expK) = fitExponential(pts);
        _chart.Lines["fit"].Points = new(exponential(pts[0].Y, pts[^1].X, expK, 0.2));
        UpdateChart();
        Console.WriteLine($"   firstCross={num(firstCross)}, maxError={num(maxErrorAfterCross)}, totalError={num(totalErrorAfterCross)}, smoothness={num(smoothness)}, expErr={num(expErr)}, expK={num(expK)}");
        if (_log != null)
        {
            var logname = $"log-{DateTime.Now:s}.csv".Replace(":", ".");
            Console.WriteLine($"   saved to {logname}");
            File.WriteAllLines(logname, _log);
        }
        return (firstCross, maxErrorAfterCross, totalErrorAfterCross, smoothness, expErr);
    }

    private void UpdateChart()
    {
        _chart.AutoscaleY();
        _chart.Invoke(_chart.Repaint);
    }

    private IEnumerable<PointD> exponential(double A, double maxX, double expK, double stepX)
    {
        for (var x = 0.0; x < maxX; x += stepX)
            yield return new PointD(x, A * Math.Exp(-expK * x));
    }

    private (double error, double k) fitExponential(PointD[] points)
    {
        // it's tempting to just look for the lowest k exponential that none of the points exceed, but a slower than exponential approach tends to be the limiting factor then
        // also the error function ends up being not very well behaved if the overshoot "pushes" the exponential from the other side as we try to increase the approach speed
        // so instead we look for best fit exponential and use both error and k in the eval function
        var A = points[0].Y;
        var maxX = points[^1].X;
        double getError(double k)
        {
            int xi = 0;
            var error = 0.0;
            for (var x = 0.0; x < maxX; x += 0.01)
            {
                while (xi + 1 < points.Length && points[xi + 1].X <= x)
                    xi++;
                if (xi == points.Length - 1)
                    break;
                var e = A * Math.Exp(-k * x);
                var y = Util.Linterp(points[xi].X, points[xi + 1].X, points[xi].Y, points[xi + 1].Y, x);
                error += Math.Abs(e - y);
            }
            return error;
        }
        var xmin = 0.0;
        var xmax = 10.0;
        var xmid = (xmin + xmax) / 2;
        var emid = getError(xmid);
        while (xmax - xmin > 0.001)
        {
            var xcur = (xmin + xmid) / 2;
            var ecur = getError(xcur);
            if (ecur < emid)
            {
                xmax = xmid;
                xmid = xcur;
                emid = ecur;
            }
            else
            {
                xcur = (xmid + xmax) / 2;
                ecur = getError(xcur);
                if (ecur < emid)
                {
                    xmin = xmid;
                    xmid = xcur;
                    emid = ecur;
                }
                else
                {
                    var dist = (xmid - xmin) / 2;
                    xmin = xmid - dist;
                    xmax = xmid + dist;
                }
            }
        }
        return (emid, xmid);
    }

    private string num(double v) => v.Rounded(5);
}
