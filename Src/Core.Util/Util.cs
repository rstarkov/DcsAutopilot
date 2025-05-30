﻿using System.Numerics;
using RT.Util.Geometry;

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
    public static double InHgToPa(this double inHg) => inHg * 3386.389;
    public static double PaToInHg(this double pa) => pa / 3386.389;
    public static double KtoC(this double k) => k - 273.15;
    public static double CtoK(this double c) => c + 273.15;

    public static string ToStringNullTerm(this Span<char> span) => span[..span.IndexOf('\0')].ToString(); // throws if no null terminator
    public static string ToStringNullTerm(this char[] chars) => chars.AsSpan().ToStringNullTerm();

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

    public static double Linterp(double x1, double x2, double y1, double y2, double x) => y1 + (x - x1) / (x2 - x1) * (y2 - y1);
    public static double? Linterp(double x1, double x2, double y1, double y2, double? x) => x == null ? null : Linterp(x1, x2, y1, y2, x.Value);

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

    public static Vector2 ToVector2(this PointD pt) => new Vector2((float)pt.X, (float)pt.Y);

    public static string SignStr(double value, string fmt, string neg, string pos, string zero)
    {
        var str = Math.Abs(value).ToString(fmt);
        if (str == 0.ToString(fmt))
            return zero + str;
        else if (value < 0)
            return neg + str;
        else
            return pos + str;
    }

    /// <summary>
    ///     Read a number from a set of decimal drums. Each drum is a number from 0 to 10, with a fractional part for drums
    ///     partway between digits. Drums are passed with least significant digit (LSD) first.</summary>
    public static double ReadDrums(params double[] drums)
    {
        // When a lower digit rotates between 9 and 0, it drags the next drum with it, with approximately the same fractional part.
        // Even if several digits are dragged through this transition, they will all have roughly the same fractional part.
        // The remaining digits will be roughly aligned on a digit.
        var transitionPos = drums[0] - Math.Floor(drums[0]); // Math.Frac of first drum
        var result = transitionPos;
        var scale = 1.0;
        for (int d = 0; d < drums.Length; d++)
        {
            var digit = Math.Round(drums[d] - transitionPos) % 10.0; // "special_floor(val [at] pos)": 4.49 and 4.51 floor to 4 at both 0.49 and 0.51; 5.01 at 0.99 floors to 4; 9.99 at 0.01 floors to 0
            result += scale * digit;
            if (digit < 9)
                transitionPos = 0;
            scale *= 10;
        }
        return result;
    }
}
