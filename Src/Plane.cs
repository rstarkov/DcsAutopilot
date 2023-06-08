using System.Collections.Generic;
using System.Linq;
using RT.Util;

namespace DcsAutopilot;

public class Plane
{
    public AxisMap MapPitch, MapRoll, MapYaw;
}

public record struct AxisMapPoint
{
    public double RawInput;
    public double NormalisedInput;
    public override string ToString() => $"({RawInput.Rounded()}, {NormalisedInput.Rounded()})";
}

public class AxisMap
{
    private AxisMapPoint[] _map;
    public IEnumerable<AxisMapPoint> Map => _map;

    public double Min => _map.Min(p => p.NormalisedInput);
    public double Max => _map.Max(p => p.NormalisedInput);

    private AxisMap() { } // for Classify

    public AxisMap(IEnumerable<AxisMapPoint> map)
    {
        _map = map.ToArray();
    }

    public double NormToRaw(double input)
    {
        if (input <= _map[0].NormalisedInput)
            return _map[0].RawInput;
        if (input >= _map[^1].NormalisedInput)
            return _map[^1].RawInput;
        int bi = 0;
        int ti = _map.Length - 1;
        while (ti - bi > 1)
        {
            var ci = (bi + ti) / 2;
            if (input <= _map[ci].NormalisedInput) // "=" condition is important to ensure that bi is at the bottom of a row of identical values
                ti = ci;
            else
                bi = ci;
        }
        Ut.Assert(bi == ti - 1);
        if (_map[ti].NormalisedInput == input && ti + 1 < _map.Length && _map[ti + 1].NormalisedInput == input) // _map[bi].Normalised can never be equal to input; it's guaranteed to be less than
            return _map[ti + 1].RawInput; // for discontinuities we can specify the same normalised value three times in a row, and the second value gets picked when input == discontinuity
        return Util.Linterp(_map[bi].NormalisedInput, _map[ti].NormalisedInput, _map[bi].RawInput, _map[ti].RawInput, input);
    }
}
