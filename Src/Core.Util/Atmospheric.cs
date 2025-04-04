using RT.Util.Collections;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public static class Atmospheric
{
    public const double IsaLapse = 0.0065;
    public const double IsaSeaTemp = 288.15;
    public const double IsaSeaPress = 101325;
    public const double IsaSGC = 287.05287;
    public const double IsaSeaSpeedOfSound = 340.294; // Math.Sqrt(IsaSeaTemp * 1.4 * isaSGC);
    public const double IsaAltConst = IsaSeaTemp / IsaLapse;
    public const double IsaBaroPow = 9.80665 / (IsaLapse * IsaSGC);
    // We don't know what DCS uses for these, so just use the best reasonable precision

    public static (double cas, double mach, double alt) CalcDials(double speedTrue, double altitudeTrue, double qnhDialPa, double seaLevelTemp, double seaLevelPress)
    {
        // source: https://aerotoolbox.com/airspeed-conversions/
        var outsideAirTemp = seaLevelTemp - IsaLapse * altitudeTrue;
        var outsideAirPress = seaLevelPress * Math.Pow(outsideAirTemp / seaLevelTemp, IsaBaroPow);
        var speedOfSound = Math.Sqrt(outsideAirTemp * 1.4 * IsaSGC);
        var mach = speedTrue / speedOfSound;
        var cas = IsaSeaSpeedOfSound * Math.Sqrt(5 * (Math.Pow(outsideAirPress * (Math.Pow(1 + 0.2 * mach * mach, 7.0 / 2.0) - 1) / IsaSeaPress + 1, 2.0 / 7.0) - 1));
        var alt = IsaAltConst * (1 - Math.Pow(outsideAirPress / qnhDialPa, 1 / IsaBaroPow));
        return (cas, mach, alt);
    }
}



/// <summary>
///     It is theoretically possible to just compute the sea level temperature from mach dial, and then sea level pressure
///     from CAS or Alt (which should match). However, our dial readings aren't super precise, so this code attempts to
///     continually refine both estimates by finding a best fit for all three dials.</summary>
public class AtmosphericFit
{
    public double MinQual;
    public double KeepMin;
    public double KeepSteep;
    public double CasDelay;
    public double MachDelay;
    public double AltDelay;

    public class Pt
    {
        public double Time;
        public double SpeedTrue;
        public double AltTrue;
        public double DialQnh;
        public double DialCas;
        public double DialMach;
        public double DialAlt;
    }

    private QueueViewable<Pt> _recent = [];
    private double _lastUpdated;

    public double EstSeaLevelTemp = Atmospheric.IsaSeaTemp;
    public double EstSeaLevelPress = Atmospheric.IsaSeaPress;
    public double EstQuality = 1.0; // 0 is perfect, 1 is worst possible
    public double SinceLastUpdate = 99; // seconds

    public void Update(Pt pt)
    {
        _recent.Enqueue(pt);
        var trueDelay = Math.Max(CasDelay, Math.Max(MachDelay, AltDelay));
        while (_recent.Count >= 3 && _recent[1].Time < pt.Time - trueDelay)
            _recent.Dequeue();
        if (_recent.Count == 1)
            return;
        double speedTrue = double.NaN;
        double altTrue = double.NaN;
        double dialCas = double.NaN;
        double dialMach = double.NaN;
        double dialAlt = double.NaN;
        double dialQnh = double.NaN;
        double timeTrue = pt.Time - trueDelay;
        double timeCas = pt.Time - trueDelay + CasDelay;
        double timeMach = pt.Time - trueDelay + MachDelay;
        double timeAlt = pt.Time - trueDelay + AltDelay;
        for (int i = 1; i < _recent.Count; i++)
        {
            if (double.IsNaN(speedTrue) && timeTrue > _recent[i - 1].Time && timeTrue <= _recent[i].Time)
            {
                speedTrue = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].SpeedTrue, _recent[i].SpeedTrue, timeTrue);
                altTrue = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].AltTrue, _recent[i].AltTrue, timeTrue);
            }
            if (double.IsNaN(dialAlt) && timeAlt > _recent[i - 1].Time && timeAlt <= _recent[i].Time)
            {
                dialAlt = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].DialAlt, _recent[i].DialAlt, timeAlt);
                dialQnh = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].DialQnh, _recent[i].DialQnh, timeAlt);
            }
            if (double.IsNaN(dialCas) && timeCas > _recent[i - 1].Time && timeCas <= _recent[i].Time)
                dialCas = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].DialCas, _recent[i].DialCas, timeCas);
            if (double.IsNaN(dialMach) && timeMach > _recent[i - 1].Time && timeMach <= _recent[i].Time)
                dialMach = Util.Linterp(_recent[i - 1].Time, _recent[i].Time, _recent[i - 1].DialMach, _recent[i].DialMach, timeMach);
        }

        var fit = FitAtmoToDials(speedTrue, altTrue, dialQnh, dialCas, dialMach, dialAlt, EstSeaLevelTemp, EstSeaLevelPress);

        if (EstQuality > MinQual)
        {
            // above MinQual we just keep the best fit we've ever seen
            if (fit.err < EstQuality)
            {
                EstQuality = fit.err;
                EstSeaLevelTemp = fit.seaLevelTemp;
                EstSeaLevelPress = fit.seaLevelPress;
                _lastUpdated = pt.Time;
            }
        }
        else
        {
            // once that is achieved, we ignore all fits with worse fit than Min, and use better fits to update the estimate - more so the better the fit
            if (fit.err <= MinQual)
            {
                var c = 1 / (1 - 1 / (MinQual * KeepSteep + 1 / KeepMin));
                var keep = c - c / (KeepSteep * fit.err + 1 / KeepMin);
                EstSeaLevelTemp = keep * EstSeaLevelTemp + (1 - keep) * fit.seaLevelTemp;
                EstSeaLevelPress = keep * EstSeaLevelPress + (1 - keep) * fit.seaLevelPress;
                EstQuality = keep * EstQuality + (1 - keep) * fit.err;
                _lastUpdated = pt.Time;
            }
        }
        SinceLastUpdate = pt.Time - _lastUpdated;
    }

    public static (double seaLevelTemp, double seaLevelPress, double err, double steps)
        FitAtmoToDials(double speedTrue, double altitudeTrue, double qnhDialPa, double dialCAS, double dialMach, double dialAlt, double guessTemp, double guessPress)
    {
        // Most of the time we are near the solution, so that's the case we optimise for. If we're far, the steps grow exponentially well enough.
        // The error function is very smooth and looks to be well-behaved, easily suitable for binary-search-like on alternating axes.
        const double stopT = 0.01;
        const double stopP = 2;
        const double minT = 273.15 - 30;
        const double maxT = 273.15 + 50;
        const double minP = 96000 - 3000; // 28.35
        const double maxP = 106000 + 3000; // 31.30
        var dirT = 1;
        var dirP = 0;
        var curT = guessTemp;
        var curP = guessPress;
        var curErr = double.NaN;
        int steps = 0;
        // Find a starting point; our initial guess may give us NaN
        while (true)
        {
            steps++;
            var r = Atmospheric.CalcDials(speedTrue, altitudeTrue, qnhDialPa, curT, curP);
            curErr = Math.Abs(r.cas - dialCAS) + 440 * Math.Abs(r.mach - dialMach) + 0.025 * Math.Abs(r.alt - dialAlt);
            if (!double.IsNaN(curErr) && !double.IsInfinity(curErr))
                break;
            if (steps > 20) // give up
                return (curT.Clip(minT, maxT), curP.Clip(minP, maxP), err: 99999, steps);
            curT = Random.Shared.NextDouble(minT, maxT);
            curP = Random.Shared.NextDouble(minP, maxP);
        }
        // Binary-ish search
        int noChange = 0;
        while (true)
        {
            // Search along current dirT/dirP
            var stepT = 0.05;
            var stepP = 25.0;
            var mul = 2.0;
            var wasT = curT;
            var wasP = curP;
            var wasErr = curErr;
            while (true)
            {
                curT += dirT * stepT;
                curP += dirP * stepP;
                steps++;
                var r = Atmospheric.CalcDials(speedTrue, altitudeTrue, qnhDialPa, curT.Clip(minT, maxT), curP.Clip(minP, maxP));
                var newErr = Math.Abs(r.cas - dialCAS) + 440 * Math.Abs(r.mach - dialMach) + 0.025 * Math.Abs(r.alt - dialAlt); // approx equal scale for a given error in T+P
                if (double.IsNaN(newErr) || double.IsInfinity(newErr))
                    break;
                if (newErr > curErr)
                {
                    mul = 0.5;
                    stepT = -stepT;
                    stepP = -stepP;
                }
                curErr = newErr; // even if it's worse - normal seek relies on that. On final step we could step back once if this was worse, but not really worth
                if (Math.Abs(stepT) < stopT || Math.Abs(stepP) < stopP)
                    break;
                stepT *= mul;
                stepP *= mul;
            }
            // Did we make progress?
            if (curErr < wasErr)
                noChange = 0; // yes
            else
            {
                noChange++; // no
                curT = wasT;
                curP = wasP;
                curErr = wasErr;
                if (noChange >= 4) // we've tried all four directions and found nothing better
                    break;
            }
            // Next direction
            if (dirT == 1) { dirT = 0; dirP = 1; }
            else if (dirP == 1) { dirT = -1; dirP = 0; }
            else if (dirT == -1) { dirT = 0; dirP = -1; }
            else if (dirP == -1) { dirT = 1; dirP = 0; }
        }
        return (curT.Clip(minT, maxT), curP.Clip(minP, maxP), curErr, steps);
    }
}
