using System.Collections;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

public class Vec : IList<double>
{
    private double[] _x;

    public double this[int index] { get => _x[index]; set { _x[index] = value; } }
    public int Count => _x.Length;

    public bool IsReadOnly => false;
    public bool Contains(double item) => _x.Contains(item);
    public int IndexOf(double item) => _x.IndexOf(item);
    public IEnumerator<double> GetEnumerator() => ((IEnumerable<double>)_x).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _x.GetEnumerator();
    public void CopyTo(double[] array, int arrayIndex) => _x.CopyTo(array, arrayIndex);

    public void Clear() { throw new NotSupportedException(); }
    public void Add(double item) { throw new NotSupportedException(); }
    public void Insert(int index, double item) { throw new NotSupportedException(); }
    public bool Remove(double item) { throw new NotSupportedException(); }
    public void RemoveAt(int index) { throw new NotSupportedException(); }

    public Vec(IEnumerable<double> v) { _x = v.ToArray(); }
    public Vec(params double[] v) { _x = v.ToArray(); }

    public static Vec operator *(Vec v, double s) => new(v._x.Select(x => x * s));
    public static Vec operator *(double s, Vec v) => new(v._x.Select(x => x * s));
    public static Vec operator /(Vec v, double s) => new(v._x.Select(x => x / s));
    public static Vec operator -(Vec v) => new(v._x.Select(x => -x));
    public static Vec operator +(Vec v1, Vec v2)
    {
        sameLen(v1, v2);
        var vr = new double[v1.Count];
        for (int i = 0; i < v1.Count; i++)
            vr[i] = v1[i] + v2[i];
        return new Vec(vr);
    }
    public static Vec operator -(Vec v1, Vec v2)
    {
        sameLen(v1, v2);
        var vr = new double[v1.Count];
        for (int i = 0; i < v1.Count; i++)
            vr[i] = v1[i] - v2[i];
        return new Vec(vr);
    }
    public static double operator *(Vec v1, Vec v2)
    {
        sameLen(v1, v2);
        double sum = 0;
        for (int i = 0; i < v1.Count; i++)
            sum += v1[i] * v2[i];
        return sum;
    }

    private static void sameLen(Vec v1, Vec v2)
    {
        if (v1.Count != v2.Count)
            throw new InvalidOperationException("This operation requires the vectors to be the same length");
    }

    public double Abs() => Math.Sqrt(_x.Sum(x => x * x));
}
