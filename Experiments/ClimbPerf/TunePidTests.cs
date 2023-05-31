using DcsAutopilot;
using RT.Util.ExtensionMethods;

namespace ClimbPerf;

class TunePidTests
{
    private DcsController _dcs;
    private TunePidController _ctrl;
    private List<string> _log = new();

    public void Run(string[] args)
    {
        var bestVector = new[] { 0.1, 0.01, 0.01, 0.01, 0.5 };
        var bestEval = Evaluate(3, bestVector);

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

    private void Restart()
    {
        _dcs?.Stop();
        DcsWindow.RestartMission();
        _ctrl = new();
        _ctrl.PidSpeedIndicated = new();
        _ctrl.PidBank = new();
        _ctrl.PidVelPitch = new();
        _dcs = new();
        _dcs.FlightControllers.Add(_ctrl);
        _dcs.Start();
        while (_dcs.Status != "Active control" || _dcs.LastFrameUtc < DateTime.UtcNow.AddMilliseconds(-50))
            Thread.Sleep(100);
        DcsWindow.SpeedUp();
    }

    private void DefaultPids()
    {
        _ctrl.PidSpeedIndicated = new BasicPid { MinControl = 0, MaxControl = 2, IntegrationLimit = 1 /*m/s / sec*/ }.SetZiNiNone(2.0, 2.1);
        _ctrl.PidBank = new BasicPid { MinControl = -1, MaxControl = 1, IntegrationLimit = 5 /*deg/sec*/, DerivativeSmoothing = 0 }.SetZiNiNone(0.05, 3);
        _ctrl.PidVelPitch = new BasicPid { MinControl = -0.5, MaxControl = 0.3, IntegrationLimit = 0.1 /*deg/sec*/, DerivativeSmoothing = 0 }.SetZiNiNone(0.20, 2.45);
    }

    private void InitialConditions()
    {
        Console.WriteLine("Initial conditions...");
        if (_dcs == null || _dcs.LastFrame == null || _dcs.LastFrame?.FuelInternal < 0.8 || _dcs.LastFrame.AltitudeAsl < 1000.FeetToMeters() || (DateTime.UtcNow - _dcs.LastFrameUtc).TotalSeconds > 5)
            Restart();
        DefaultPids();
        _ctrl.TgtPitch = 0;
        _ctrl.TgtRoll = 0;
        _ctrl.TgtSpeed = 300.KtsToMs();
        while (true)
        {
            Thread.Sleep(100);
            var altError = 10_000 - _dcs.LastFrame.AltitudeAsl.MetersToFeet();
            _ctrl.TgtPitch = Math.Abs(altError) < 200 ? 0 : (0.01 * altError).Clip(-20, 20);
            if (Math.Abs(altError) < 200 && Math.Abs(_ctrl.ErrSpeed) < 1.KtsToMs() && Math.Abs(_ctrl.ErrPitch) < 0.1 && Math.Abs(_ctrl.ErrRoll) < 0.1 && Math.Abs(_ctrl.ErrRateSpeed) < 0.1 && Math.Abs(_ctrl.ErrRatePitch) < 0.05 && Math.Abs(_ctrl.ErrRateRoll) < 0.05)
                break;
        }
        Console.WriteLine("Initial conditions done.");
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
        var duration = 15.0;
        var tstart = -9999.0;
        var totalErrorAfterCross = 0.0;
        var direction = 0;
        var firstCross = duration;
        var maxErrorAfterCross = 0.0;
        var prevGrad = double.NaN;
        var prevE = double.NaN;
        var smoothness = 0.0;
        _log.Clear();
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
            Thread.Sleep(100);
        Console.WriteLine($"   firstCross={num(firstCross)}, maxError={num(maxErrorAfterCross)}, totalError={num(totalErrorAfterCross)}, smoothness={num(smoothness)}");
        var logname = $"log-{DateTime.Now:s}.csv".Replace(":", ".");
        Console.WriteLine($"   saved to {logname}");
        File.WriteAllLines(logname, _log);
        return (firstCross, maxErrorAfterCross, totalErrorAfterCross, smoothness);
    }

    private void Stabilise(double speedTolerance = 1)
    {
        Console.WriteLine("Stabilising...");
        DefaultPids();
        var unstable = new List<string>();
        while (true)
        {
            Thread.Sleep(100);

            bool s(double error, double limit, string name)
            {
                if (Math.Abs(error) <= limit) return true;
                unstable.Add(name);
                return false;
            }
            unstable.Clear();
            if (s(_ctrl.ErrSpeed, speedTolerance.KtsToMs(), "speed") && s(_ctrl.ErrRateSpeed, 0.1 * speedTolerance, "speed-rate") && s(_ctrl.ErrPitch, 1, "pitch") && s(_ctrl.ErrRatePitch, 0.1, "pitch-rate") && s(_ctrl.ErrRoll, 1, "roll") && s(_ctrl.ErrRateRoll, 0.1, "roll-rate"))
                break;
            //Console.Title = "Unstable: " + unstable.JoinString(", ");
        }
        //Console.Title = "Stable";
        //Console.WriteLine($"Stabilising done. pitch={_ctrl.ErrPitch:0.0}/{_ctrl.ErrRatePitch:0.00}  speed={_ctrl.ErrSpeed:0.0}/{_ctrl.ErrRateSpeed:0.00}");
    }

    private string num(double n)
    {
        if (n >= 10_000) return $"{n:#,0}";
        if (n >= 1000) return $"{n:#,0.0}";
        if (n >= 100) return $"{n:0.00}";
        if (n >= 10) return $"{n:0.000}";
        if (n >= 1) return $"{n:0.0000}";
        if (n >= 0.1) return $"{n:0.00000}";
        if (n >= 0.01) return $"{n:0.000000}";
        if (n >= 0.001) return $"{n:0.0000000}";
        return n.ToString();
    }
}
