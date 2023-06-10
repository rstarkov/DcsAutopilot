using DcsAutopilot;
using RT.Util.ExtensionMethods;

namespace DcsExperiments;

class TunePidTests : MultiTester
{
    private List<string> _log = new();
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
        new Thread(() => { _chart.ShowDialog(); }) { IsBackground = true }.Start();

        var bestVector = new[] { 0.1, 0.01, 0.01, 0.01, 0.5 };
        var bestEval = Evaluate(3, bestVector);
        _chart.Lines["best"].Points = new(_chart.Lines["err"].Points);

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
        var eval = Math.Pow(firstCross, 0.7) * Math.Pow(maxErrorAfterCross, 3) * Math.Pow(totalErrorAfterCross, 1.5) * smoothness;
        Console.WriteLine($"   AVG={n}: firstCross={num(firstCross)}, maxError={num(maxErrorAfterCross)}, totalError={num(totalErrorAfterCross)}, smoothness={num(smoothness)}");
        Console.WriteLine($"Evaluating done: {num(eval)}");
        return eval;
    }

    private (double firstCross, double maxErrorAfterCross, double totalErrorAfterCross, double smoothness) EvaluateInner(double tgt, double[] vector)
    {
        InitialConditions();
        Console.WriteLine($"Evaluating... {vector.Select(v => $"{num(v)}").JoinString(", ")}");
        _chart.Lines["err"].Points.Clear();
        var duration = 15.0;
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
            var t = f.SimTime - tstart;
            if (t < 0 || t > duration)
                return;
            var velpitchFiltered = filterVelPitch.Step(f.VelPitch);
            var dTfiltered = filterDT.Step(f.dT);
            var e = velpitchFiltered - _ctrl.TgtPitch;
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
        };

        _ctrl.PidVelPitch = new BasicPid { MinControl = -1.0, MaxControl = 0.5, P = vector[0], I = vector[1], D = vector[2], IntegrationLimit = vector[3], DerivativeSmoothing = 1 - 1 / (vector[4] + 1) };
        _ctrl.TgtPitch = tgt;
        tstart = _dcs.LastFrame.SimTime; // ordering ensures that we don't integrate anything until all variables are initialised
        while (_dcs.LastFrame.SimTime - tstart < duration)
        {
            Thread.Sleep(100);
            _chart.AutoscaleY();
            _chart.Invoke(_chart.Repaint);
        }
        Console.WriteLine($"   firstCross={num(firstCross)}, maxError={num(maxErrorAfterCross)}, totalError={num(totalErrorAfterCross)}, smoothness={num(smoothness)}");
        if (_log != null)
        {
            var logname = $"log-{DateTime.Now:s}.csv".Replace(":", ".");
            Console.WriteLine($"   saved to {logname}");
            File.WriteAllLines(logname, _log);
        }
        return (firstCross, maxErrorAfterCross, totalErrorAfterCross, smoothness);
    }

    private string num(double v) => v.Rounded(5);
}
