using System;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

class SmoothMoverFilter
{
    private double xv0, xv1, xv2;
    private double yv0, yv1, yv2;
    private double _min, _max;

    public SmoothMoverFilter(double min, double max)
    {
        _min = min;
        _max = max;
    }

    public double MoveTo(double tgtpos, double time)
    {
        // Order 2 Bessel filter with cutoff frequency 0.04 of sampling rate. Ignores time scale so the cut-off will vary with framerate :/
        // Source: https://doctorpapadopoulos.com/low-pass-filter-bessel-c-c-implementation/
        xv0 = xv1;
        xv1 = xv2;
        xv2 = tgtpos / 50.50469146;
        yv0 = yv1;
        yv1 = yv2;
        yv2 = xv0 + xv1 * 2 + xv2 - yv0 * 0.5731643146 - yv1 * (-1.4939637515);
        return yv1.Clip(_min, _max);
    }
}

class SmoothMover
{
    private double _prevtime, _prevtgtpos;
    private double _pos;
    private double _speed;
    private double _maxaccel;
    private double _min, _max;

    public SmoothMover(double maxaccel, double min, double max)
    {
        _maxaccel = maxaccel;
        _min = min;
        _max = max;
    }

    public void Reset(double pos)
    {
        _prevtime = 0;
        _prevtgtpos = pos;
        _pos = pos;
        _speed = 0;
    }

    public double MoveTo(double tgtpos, double time)
    {
        if (time == _prevtime)
            throw new ArgumentException($"{nameof(SmoothMover)}.{nameof(MoveTo)} called twice with the same time");
        tgtpos = tgtpos.Clip(_min, _max);
        var dt = time - _prevtime;
        if (dt > 1 || dt < 0)
        {
            _pos = _speed = _prevtgtpos = 0; // reset - time change is too big
            _prevtime = time;
            return 0;
        }
        //var dist = pos - _pos; // target pos exactly - this means the control lags behind if target pos is moving at a fixed speed, as we aim to stop by this pos value
        var posvel = (tgtpos - _prevtgtpos) / dt;
        var posvelstop = (tgtpos + posvel * posvel / 2 / _maxaccel * Math.Sign(posvel)); // where it would stop at maxaccel from this speed
        var dist = posvelstop - _pos; // this is an OK middle ground between severe lag and zero overshoot, and the other extreme which is to catch up exactly with tgt pos assuming it keeps going, and then overshoot a lot if it suddenly stops
        _prevtgtpos = tgtpos;
        if (dist == 0)
        {
            _speed = 0; // reset it regardless of value, as matching the target exactly in floating point values can only happen at low speed (or at the end stops)
            _prevtime = time;
            return tgtpos;
        }
        // if we snapped to pos in one timestep what acceleration does that take?
        var snapacc = (tgtpos - _pos - _speed * dt) * 2 / dt / dt;
        if (Math.Abs(snapacc) < _maxaccel)
        {
            _pos = tgtpos;
            _speed += snapacc * dt;
            _prevtime = time;
            return _pos;
        }

        double accel;
        var stopdist = _speed * _speed / 2 / _maxaccel; // stopping distance at current speed - only valid if moving towards the target!
        if ((_speed != 0 && Math.Sign(_speed) != Math.Sign(dist)) || stopdist >= Math.Abs(dist))
        {
            // we're moving in the wrong direction, or the direction is right but we can't stop in the remaining distance: just stop under max acceleration
            accel = -Math.Sign(_speed) * _maxaccel;
        }
        else
        {
            var acceldist = (Math.Abs(dist) - stopdist) / 2; // difference between distance and stopping distance is to be travelled under a symmetrical accel/decel, so we stop accelerating after the midpoint of that
            var t = (Math.Sqrt(2 * _maxaccel * acceldist + _speed * _speed) - Math.Abs(_speed)) / _maxaccel; // time to travel acceldist
            if (t >= dt)
                accel = Math.Sign(dist) * _maxaccel; // more than one timestep of acceleration, so accelerate at full blast
            else
                accel = -_speed * _speed / 2 / dist; // we've got less than one timestep worth of acceleration to do, so just decelerate at the perfect deceleration to reach the target
        }
        double tstop;
        if (_speed != 0 && Math.Sign(_speed) != Math.Sign(accel) && (tstop = Math.Abs(_speed) / Math.Abs(accel)) < dt)
        {
            // we're decelerating and have less than a timestep left before we hit zero speed. Propagate to the zero speed exactly by using the shorter time delta
            _pos = (_pos + _speed * tstop + accel * tstop * tstop / 2).Clip(_min, _max);
            _speed = 0;
        }
        else
        {
            _pos = (_pos + _speed * dt + accel * dt * dt / 2).Clip(_min, _max);
            _speed += accel * dt;
            if (_pos == _min || _pos == _max)
                _speed = 0; // we clipped the limits and that's a dead stop in terms of speed - otherwise the output gets briefly stuck at the limits while the speed drops to zero and reverses
        }
        if (Math.Abs(_speed) < 0.00001) _speed = 0;
        if (Math.Abs(_pos - tgtpos) < 0.00001) _pos = tgtpos;
        _prevtime = time;
        return _pos;
    }
}
