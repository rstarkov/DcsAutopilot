using System;

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
        var mul = Math.Pow(10, sf - 1);
        if (n >= mul) return $"{n:#,0}";
        if (n >= mul * 0.1) return $"{n:#,0.0}";
        if (n >= mul * 0.01) return $"{n:#,0.00}";
        if (n >= mul * 0.001) return $"{n:#,0.000}";
        if (n >= mul * 0.0001) return $"{n:#,0.0000}";
        if (n >= mul * 0.00001) return $"{n:#,0.00000}";
        if (n >= mul * 0.000001) return $"{n:#,0.000000}";
        if (n >= mul * 0.0000001) return $"{n:#,0.0000000}";
        if (n >= mul * 0.00000001) return $"{n:#,0.00000000}";
        return n.ToString();
    }
}
