namespace DcsExperiments;

public static class Fit
{
    public interface IPt
    {
        bool Include { get; }
        double X { get; }
        double Y { get; }
    }

    public static (double gradient, double intercept, double rSquared) LinearReg(IEnumerable<IPt> points)
    {
        int n = 0;
        double sumX = 0;
        double sumY = 0;
        double sumX2 = 0;
        double sumXY = 0;
        foreach (var p in points)
            if (p.Include)
            {
                double x = p.X;
                double y = p.Y;
                sumX += x;
                sumY += y;
                sumX2 += x * x;
                sumXY += x * y;
                n++;
            }
        double meanX = sumX / n;
        double meanY = sumY / n;
        double gradient = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        double intercept = meanY - gradient * meanX;
        // compute r^2
        double ssTot = 0;
        double ssRes = 0;
        foreach (var p in points)
            if (p.Include)
            {
                double y = p.Y;
                double yPred = gradient * p.X + intercept;
                double diffTot = y - meanY;
                double diffRes = y - yPred;
                ssTot += diffTot * diffTot;
                ssRes += diffRes * diffRes;
            }
        double rSquared = ssTot == 0 ? 1 : 1 - ssRes / ssTot;
        return (gradient, intercept, rSquared);
    }

    public static (double Slope, double Intercept, double PerpRMS) OrthoLinReg(IEnumerable<IPt> points)
    {
        int n = 0;
        double sumX = 0, sumY = 0;
        foreach (var p in points)
            if (p.Include)
            {
                n++;
                sumX += p.X;
                sumY += p.Y;
            }
        double meanX = sumX / n;
        double meanY = sumY / n;

        double sXX = 0, sXY = 0, sYY = 0;
        foreach (var p in points)
            if (p.Include)
            {
                double dx = p.X - meanX;
                double dy = p.Y - meanY;
                sXX += dx * dx;
                sXY += dx * dy;
                sYY += dy * dy;
            }

        double trace = sXX + sYY;
        double det = sXX * sYY - sXY * sXY;
        double lambda = trace / 2 + Math.Sqrt(trace * trace / 4 - det);

        double vX = sXY;
        double vY = lambda - sXX;
        double norm = Math.Sqrt(vX * vX + vY * vY);
        vX /= norm;
        vY /= norm;

        double slope;
        if (Math.Abs(vX) < 1e-10)
            throw new InvalidOperationException("Best-fit line is vertical.");
        else
            slope = vY / vX;

        double intercept = meanY - slope * meanX;

        // Compute RMSPE
        double sumSqDist = 0;
        double denom = Math.Sqrt(slope * slope + 1);
        foreach (var p in points)
        {
            double distance = Math.Abs(slope * p.X - p.Y + intercept) / denom;
            sumSqDist += distance * distance;
        }

        double rmspe = Math.Sqrt(sumSqDist / n);

        return (slope, intercept, rmspe);
    }
}
