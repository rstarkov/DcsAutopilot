using System;
using System.Collections.Generic;
using System.Linq;

namespace DcsAutopilot;

public static class Util
{
    public static double ToDeg(this double rad) => rad / Math.PI * 180;
    public static double ToRad(this double deg) => deg / 180.0 * Math.PI;
    public static double ToRad(this int deg) => deg / 180.0 * Math.PI;
    public static double FeetToMeters(this double feet) => feet / 3.2808399;
    public static double FeetToMeters(this int feet) => feet / 3.2808399;
    public static double MetersToFeet(this double meters) => meters * 3.2808399;
    public static double MsToKts(this double ms) => ms / 0.51444444;
    public static double KtsToMs(this double kts) => kts * 0.51444444;
    public static double KtsToMs(this int kts) => kts * 0.51444444;

    public static string Rounded(this double n, int sf = 5)
    {
        var a = Math.Abs(n);
        var mul = Math.Pow(10, sf - 1);
        if (a >= mul) return $"{n:#,0}";
        if (a >= mul * 0.1) return $"{n:#,0.0}";
        if (a >= mul * 0.01) return $"{n:#,0.00}";
        if (a >= mul * 0.001) return $"{n:#,0.000}";
        if (a >= mul * 0.0001) return $"{n:#,0.0000}";
        if (a >= mul * 0.00001) return $"{n:#,0.00000}";
        if (a >= mul * 0.000001) return $"{n:#,0.000000}";
        if (a >= mul * 0.0000001) return $"{n:#,0.0000000}";
        if (a >= mul * 0.00000001) return $"{n:#,0.00000000}";
        return n.ToString();
    }

    public static IEnumerable<double> Range(double start, double step, double endInclusive)
    {
        for (var v = start; v < endInclusive + 0.5 * step; v += step)
            yield return v;
    }

    public static double Linterp(double x1, double x2, double y1, double y2, double x) => y1 + (x - x1) / (x2 - x1) * (y2 - y1);

    public static double Median(this IEnumerable<double> values, double removeOutliersFraction = 0)
    {
        var sorted = values.Order().ToList();
        if (removeOutliersFraction > 0)
        {
            var targetCount = (int)(sorted.Count * (1 - removeOutliersFraction));
            while (sorted.Count > targetCount)
            {
                var m = sorted[sorted.Count / 2];
                if (m - sorted[0] > sorted[^1] - m)
                    sorted.RemoveAt(0);
                else
                    sorted.RemoveAt(sorted.Count - 1);
            }
        }
        if (sorted.Count % 2 == 1)
            return sorted[sorted.Count / 2];
        else
            return (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }
}
