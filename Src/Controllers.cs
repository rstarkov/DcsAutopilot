using System;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public class BasicPid
{
    public double P { get; set; }
    public double I { get; set; }
    public double D { get; set; }

    public double MinControl { get; set; }
    public double MaxControl { get; set; }
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

        double output = P * error + I * ErrorIntegral + D * Derivative + Bias;

        output = output.Clip(MinControl, MaxControl);
        Integrating = Math.Abs(Derivative) < IntegrationLimit && output > MinControl && output < MaxControl;
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
