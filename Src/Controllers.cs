using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace DcsAutopilot;

class HornetAutoTrim : IFlightController
{
    private string _status = "";
    public string Status => _status;
    public double _timeoutUntil;
    private double _neutralPitch = 0.120;
    private double _neutralRoll = 0;

    public void NewSession(BulkData bulk)
    {
    }

    public void ProcessBulkUpdate(BulkData bulk)
    {
    }

    public ControlData ProcessFrame(FrameData frame)
    {
        var ctrl = new ControlData();
        _status = "";
        if (Math.Abs(frame.Bank.ToDeg()) > 15)
        {
            _status = "[bank limit]";
            return ctrl;
        }
        if (Math.Abs(frame.JoyPitch - _neutralPitch) > 0.005 || Math.Abs(frame.JoyRoll - _neutralRoll) > 0.005)
        {
            _status = "[stick deflection]";
            _timeoutUntil = frame.SimTime + 1.5;
            return ctrl;
        }
        if (frame.SimTime < _timeoutUntil)
        {
            _status = "[stick timeout]";
            return ctrl;
        }
        {
            var rate = frame.GyroPitch.ToDeg();
            if (Math.Abs(rate) < 0.02) // in the Hornet one tick of pitch trim changes roll rate by about 0.02 deg/sec
                _status += $" [pitch]";
            else
            {
                _status += $" [PITCH]";
                ctrl.PitchTrimRate = (-rate * 2.0).Clip(-1, 1);
            }
        }
        {
            var rate = frame.GyroRoll.ToDeg();
            if (Math.Abs(rate) < 0.10) // in the Hornet one tick of roll trim changes roll rate by about 0.1 deg/sec
                _status += $" [roll]";
            else
            {
                _status += $" [ROLL]";
                ctrl.RollTrimRate = (-rate * 0.5).Clip(-1, 1);
            }
        }
        return ctrl;
    }
}

class HornetClimbPerfController : IFlightController
{
    private string _status = "";
    public string Status => _status;
    private BasicPid _speed2axisPID = new BasicPid { MinControl = 0, MaxControl = 2, IntegrationLimit = 1 /*m/s / sec*/ }.SetZiNiNone(2.0, 2.1); // at 10k ft
    private BasicPid _hdg2bankPID = new BasicPid { P = 20, I = 0.01, MinControl = -10, MaxControl = 10, IntegrationLimit = 0.1 /*deg/sec*/ }; // at 300 ias 5000ft
    private BasicPid _bank2axisSmoothPID = new BasicPid { MinControl = -1, MaxControl = 1, IntegrationLimit = 5 /*deg/sec*/, DerivativeSmoothing = 0 }.SetZiNiNone(0.05, 3); // at 280 ias kts 5000ft
    private BasicPid _pitch2axisSmoothPID = new BasicPid { MinControl = -0.5, MaxControl = 0.5, IntegrationLimit = 1 /*deg/sec*/, DerivativeSmoothing = 0 }.SetZiNiNone(0.1, 1.5);
    private BasicPid _velpitch2axisSmoothPID = new BasicPid { MinControl = -0.5, MaxControl = 0.3, IntegrationLimit = 0.1 /*deg/sec*/, DerivativeSmoothing = 0 }.SetZiNiNone(0.20, 2.45);
    private BasicPid _alt2pitchPID = new BasicPid { MinControl = -10, MaxControl = 30, IntegrationLimit = 1 /*m/sec*/ }.SetZiNiNone(0.3, 11);
    private BasicPid _velpitch2axisHighPID = new BasicPid { MinControl = -0.5, MaxControl = 0.5, IntegrationLimit = 0.1, DerivativeSmoothing = 0.8 }.SetZiNiNone(0.22, 2.857);
    //private BasicPid _g2pitchPID = new BasicPid { MinControl = -0.5, MaxControl = 0.5, IntegrationLimit = 0.1, DerivativeSmoothing = 0.8 }.SetZiNiNone(0.7, 1.76);
    //private BasicPid _g2pitchPID = new BasicPid { MinControl = -0.5, MaxControl = 0.5, IntegrationLimit = 0.1, DerivativeSmoothing = 0.8 }.SetZiNiNone(0.5, 1.55);
    //private BasicPid _g2pitchPID = new BasicPid { MinControl = -0.5, MaxControl = 0.5, IntegrationLimit = 0.1, DerivativeSmoothing = 0.4 }.SetZiNiNone(0.5, 1.36);
    private BasicPid _g2pitchPID = new BasicPid { P = 0.10, I = 0.20, D = 0, MinControl = -0.5, MaxControl = 0.5, IntegrationLimit = 1 };
    private SmoothMoverFilter _throttle = new(0, 2);
    private SmoothMoverFilter _roll = new(-1, 1);
    private SmoothMoverFilter _pitch = new(-1, 1);

    public void NewSession(BulkData bulk)
    {
    }

    public void ProcessBulkUpdate(BulkData bulk)
    {
    }

    private string _stage = "prep";
    private double _tgtPitch;
    private int _curTestAngle = 30;
    private Dictionary<int, double> _lvloffAltByPitch = new();
    private double _actualVelPitch, _actualIAS, _actualTAS, _actualFuel;
    private double _leveloffTarget = loadLeveloffTgt("lvloff.txt");
    private double _maxAlt;

    public ControlData ProcessFrame(FrameData frame)
    {
        var ctl = new ControlData();
        var wantedHeading = 0;
        var wantedBank = _hdg2bankPID.Update(angdiff(wantedHeading - frame.Heading), frame.dT);
        var velPitch = Math.Atan2(frame.VelY, Math.Sqrt(frame.VelX * frame.VelX + frame.VelZ * frame.VelZ)).ToDeg();
        _maxAlt = Math.Max(_maxAlt, frame.AltitudeAsl.MetersToFeet());
        var wasStage = _stage;

        var testThrust = 2.0;
        var testStartSpeed = 350;
        var testAngle = 28;

        _speed2axisPID.MaxControl = testThrust;

        if (_stage == "tuneA")
        {
            // tune _velpitch2axisHighPID at high altitude
            var wantedSpeed = 227;
            ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(wantedSpeed.KtsToMs() - frame.SpeedIndicated, frame.dT), frame.SimTime);
            var wantedPitch = ((int)(frame.SimTime / 10)) % 6 == 0 ? -25 : ((int)(frame.SimTime / 10)) % 6 == 3 ? 25 : 0;
            ctl.PitchAxis = _velpitch2axisHighPID.Update(wantedPitch - velPitch, frame.dT);
        }
        else if (_stage == "tuneC")
        {
            // tune _g2pitchPID (set _tgtPitch = 20 initially)
            var wantedSpeed = 227;
            ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(wantedSpeed.KtsToMs() - frame.SpeedIndicated, frame.dT), frame.SimTime);
            var wantedG = _tgtPitch > 0 ? 2 : 0;
            if (_tgtPitch < 0)
                wantedBank = 0;
            ctl.PitchAxis = _g2pitchPID.Update(wantedG - frame.AccY, frame.dT);
            if (_tgtPitch > 0 && velPitch > 20)
            {
                _tgtPitch = -20;
                _g2pitchPID.ErrorIntegral = 0;
            }
            if (_tgtPitch < 0 && velPitch < -20)
            {
                _tgtPitch = 20;
                _g2pitchPID.ErrorIntegral = 0;
            }
        }
        else if (_stage == "tuneB-stabilise")
        {
            // tune the level out altitude by pitch angle and speed
            var wantedSpeed = 250;
            ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(wantedSpeed.KtsToMs() - frame.SpeedIndicated, frame.dT), frame.SimTime);
            ctl.PitchAxis = _velpitch2axisHighPID.Update(0 - velPitch, frame.dT);
            if (Math.Abs(velPitch) < 0.5 && Math.Abs(frame.GyroRoll) < 0.5 && frame.SpeedIndicated.MsToKts() > 249)
            {
                _stage = "tuneB-climbtest";
                _tgtPitch = 0;
                if (!_lvloffAltByPitch.ContainsKey(_curTestAngle))
                    _lvloffAltByPitch[_curTestAngle] = 33000;
            }
        }
        else if (_stage == "tuneB-climbtest")
        {
            if (_tgtPitch < _curTestAngle)
            {
                _tgtPitch += 1.5 * frame.dT;
                _velpitch2axisHighPID.ErrorIntegral = 0;
            }
            else
                _tgtPitch = _curTestAngle;
            ctl.ThrottleAxis = 2;
            ctl.PitchAxis = _velpitch2axisHighPID.Update(_tgtPitch - velPitch, frame.dT);
            if (frame.AltitudeAsl > _lvloffAltByPitch[_curTestAngle].FeetToMeters())
            {
                _stage = "tuneB-lvloff";
                _actualVelPitch = velPitch;
                _actualIAS = frame.SpeedIndicated.MsToKts();
                _actualTAS = frame.SpeedTrue.MsToKts();
                _actualFuel = frame.FuelInternal + frame.FuelExternal;
            }
        }
        else if (_stage == "tuneB-lvloff")
        {
            ctl.ThrottleAxis = 2;
            //ctl.PitchAxis = _velpitch2axisHighPID.Update(0 - velPitch, frame.dT);
            ctl.PitchAxis = _g2pitchPID.Update(0 - frame.AccY, frame.dT);
            wantedBank = 0;
            if (velPitch <= 0)
            {
                _stage = "tuneB-descend";
                File.AppendAllLines("levelofftune.csv", new[] { Ut.FormatCsvRow(_curTestAngle, _actualVelPitch, _actualIAS, _actualTAS, _actualFuel, _lvloffAltByPitch[_curTestAngle], frame.AltitudeAsl.MetersToFeet(), frame.SpeedIndicated.MsToKts(), frame.SpeedTrue.MsToKts()) });
                _lvloffAltByPitch[_curTestAngle] -= frame.AltitudeAsl.MetersToFeet() - 35000;
                _curTestAngle -= 2;
                if (_curTestAngle < 28)
                    _curTestAngle = 30;
            }
        }
        else if (_stage == "tuneB-descend")
        {
            var wantedSpeed = 250;
            ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(wantedSpeed.KtsToMs() - frame.SpeedIndicated, frame.dT), frame.SimTime);
            ctl.PitchAxis = _velpitch2axisHighPID.Update(-20 - velPitch, frame.dT);
            if (velPitch > -15) wantedBank = 0;
            if (frame.AltitudeAsl < 28000.FeetToMeters())
                _stage = "tuneB-stabilise";
        }

        else if (_stage == "prep")
        {
            var wantedSpeed = 200;
            var wantedAlt = 200;
            var wantedPitch = _alt2pitchPID.Update(wantedAlt.FeetToMeters() - frame.AltitudeAsl, frame.dT);
            ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(wantedSpeed.KtsToMs() - frame.SpeedIndicated, frame.dT), frame.SimTime);
            ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(wantedPitch - velPitch, frame.dT), frame.SimTime);
            if (frame.SimTime >= 20)
                _stage = "lowaccel";
        }
        else if (_stage == "lowaccel")
        {
            var wantedAlt = 200;
            var wantedPitch = _alt2pitchPID.Update(wantedAlt.FeetToMeters() - frame.AltitudeAsl, frame.dT);
            ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(wantedPitch - velPitch, frame.dT), frame.SimTime);
            ctl.ThrottleAxis = _throttle.MoveTo(testThrust, frame.SimTime);
            if (frame.SpeedIndicated >= testStartSpeed.KtsToMs())
            {
                _stage = "pitchup";
                _tgtPitch = 0;
            }
        }
        else if (_stage == "pitchup")
        {
            _tgtPitch += ((testAngle - velPitch) / 3).Clip(0.1, 3) * frame.dT;
            ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(_tgtPitch - velPitch, frame.dT), frame.SimTime);
            ctl.ThrottleAxis = _throttle.MoveTo(testThrust, frame.SimTime);
            if (_tgtPitch >= testAngle)
            {
                _stage = "climb";
                _tgtPitch = testAngle;
            }
        }
        else if (_stage == "climb")
        {
            ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(_tgtPitch - velPitch, frame.dT), frame.SimTime);
            ctl.ThrottleAxis = _throttle.MoveTo(testThrust, frame.SimTime);
            //var wantedSpeed = frame.SpeedIndicated / frame.SpeedMach * 0.90;
            //ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(wantedSpeed - frame.SpeedIndicated, frame.dT), frame.SimTime);
            if (frame.AltitudeAsl > _leveloffTarget.FeetToMeters())
            {
                _stage = "pitchdown";
            }
        }
        else if (_stage == "pitchdown")
        {
            //_tgtPitch -= 1.5 * frame.dT;
            //ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(_tgtPitch - velPitch, frame.dT), frame.SimTime);
            ctl.PitchAxis = _g2pitchPID.Update(0 - frame.AccY, frame.dT);
            ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(295.KtsToMs() - frame.SpeedIndicated, frame.dT), frame.SimTime); // accel to slightly over: don't waste fuel with full throttle, but don't creep up on 0.90 mach too slowly either
            wantedBank = 0;
            if (velPitch <= 4)
            {
                _stage = "highaccel";
                _velpitch2axisSmoothPID.ErrorIntegral = 0;
            }
        }
        else if (_stage == "highaccel")
        {
            var wantedPitch = 0;
            ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(wantedPitch - velPitch, frame.dT), frame.SimTime);
            ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(295.KtsToMs() - frame.SpeedIndicated, frame.dT), frame.SimTime); // accel to slightly over: don't waste fuel with full throttle, but don't creep up on 0.90 mach too slowly either
            if (frame.SpeedMach >= 0.90) // 289.2 kts IAS @ 35k
            {
                _stage = "done";
                File.AppendAllLines("lvloff.txt", new[] { Ut.FormatCsvRow(_leveloffTarget, _maxAlt, frame.Skips, frame.FuelInternal * 10803) });
            }
        }
        else
            wantedBank = 45;
        ctl.RollAxis = _roll.MoveTo(_bank2axisSmoothPID.Update(wantedBank - frame.Bank, frame.dT), frame.SimTime);
        _status = $"{_stage}; tgtpitch={_tgtPitch:0.0}; lvloff={_leveloffTarget:#,0}";
        //_status = $"{_stage}; testangle={_curTestAngle}";

        if (wasStage != "done")
        {
            MainWindow.Log.Enqueue(Ut.FormatCsvRow(frame.SimTime, frame.FuelInternal, frame.FuelExternal, frame.AltitudeAsl.MetersToFeet(), frame.Pitch, velPitch, frame.AngleOfAttack, frame.SpeedTrue.MsToKts(), frame.SpeedIndicated.MsToKts(), frame.SpeedMach, frame.SpeedVertical.MetersToFeet(), frame.PosX, frame.PosZ, frame.PosY, frame.AccY, ctl.ThrottleAxis, ctl.RollAxis));
            if (_stage == "done")
                MainWindow.Log.Enqueue("DONE");
        }

        return ctl;
    }

    private static double loadLeveloffTgt(string filename)
    {
        return 33137.44534114057;
        if (!File.Exists(filename))
            return 33000;
        var data = Ut.ParseCsvFile(filename).Select(r => (tgt: double.Parse(r[0]), final: double.Parse(r[1]))).ToList();
        if (data.Count == 1)
            return data[0].tgt - (data[0].final - 35000) * 1.2;
        var lo = data.Where(d => d.final < 35000).MaxElementOrDefault(d => d.tgt);
        var hi = data.Where(d => d.final > 35000).MinElementOrDefault(d => d.tgt);
        if (lo == default || hi == default)
        {
            var nearest = data.OrderBy(d => Math.Abs(d.final - 35000)).Take(2).ToList();
            lo = nearest[0]; // lo doesn't have to actually be less than hi
            hi = nearest[1];
        }
        return lo.tgt + (35000 - lo.final) / (hi.final - lo.final) * (hi.tgt - lo.tgt);
    }

    private double angdiff(double diff) => diff < -180 ? diff + 360 : diff > 180 ? diff - 360 : diff;
}

class HornetSlowFlightController : IFlightController
{
    private BasicPid _vspeed2pitchPID = new() { P = 0.004, I = 0.003, D = 0.00127 * 0.05, MinControl = -90.ToRad(), MaxControl = 90.ToRad(), IntegrationLimit = 1 /*m/s / sec*/ }; // oscillates at P=0.005 T=3.4s
    private BasicPid _pitch2axisPID = new() { P = 2, I = 1, D = 0.3, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ }; // oscillates at P=7 T=2.74s
    private BasicPid _speed2axisPID = new() { P = 0.5, I = 0.7, D = 0.05, MinControl = 0, MaxControl = 1.5, IntegrationLimit = 1 /*m/s / sec*/ };
    //private BasicPid _bank2axisPID = new() { P = 2.4, I = 2.8, D = 0.51, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ }; // oscillates at P=4 T=1.7
    private BasicPid _bank2axisPID = new() { P = 5, I = 0, D = 0, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ }; // oscillates at P=4 T=1.7
    // for the afterburner phase
    private BasicPid _vspeed2axisSlowPID = new() { P = 0.02, I = 0.001, D = 0, MinControl = -0.25, MaxControl = 0.25, IntegrationLimit = 10 /*m/s / sec*/ };
    private BasicPid _alt2axisSlowPID = new() { P = 0.02, I = 0.001, D = 0, MinControl = -0.25, MaxControl = 0.25, IntegrationLimit = 10 /*m/s / sec*/ };
    private BasicPid _pitch2axisSlowPID = new() { P = 6, I = 1, D = 0.3, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ };
    private BasicPid _bank2axisSlowPID = new() { P = 15, I = 0, D = 0, MinControl = -1, MaxControl = 1, IntegrationLimit = 1.ToRad() /*rad/sec*/ };

    private SmoothMover _pitch = new(1, -1, 1);
    private SmoothMover _throttle = new(1, 0, 2);
    private double wantedSpeed = 200;

    public double TargetAltitudeFt { get; set; } = 2000;

    public string Status => $"vspd={_vspeed2pitchPID.Integrating}; speed={_speed2axisPID.Integrating};\npitch={_pitch2axisPID.Integrating}; bank={_bank2axisPID.Integrating}; wanted={wantedSpeed:0.0}";

    public void NewSession(BulkData bulk)
    {
        _pitch.Reset(0);
        _throttle.Reset(1);
    }

    public void ProcessBulkUpdate(BulkData bulk)
    {
    }

    public ControlData ProcessFrame(FrameData frame)
    {
        var ctl = new ControlData();

        if (wantedSpeed > 140)
            wantedSpeed -= frame.dT / 1.0;
        if (wantedSpeed > 120)
            wantedSpeed -= frame.dT / 2.0;
        if (wantedSpeed > 110)
            wantedSpeed -= frame.dT / 3.0;
        else if (wantedSpeed > 102)
            wantedSpeed -= frame.dT / 5.0;
        else if (wantedSpeed > 101)
            wantedSpeed -= frame.dT / 20;
        else
            wantedSpeed = 101;
        var wantedHeading = frame.SimTime < 435 ? 233.01 : 227.0;
        var wantedBank = (wantedHeading.ToRad() - frame.Heading).Clip(-1.ToRad(), 1.ToRad());
        var wantedAltitude = frame.SimTime < 100 ? 300 : frame.SimTime < 300 ? linterp(100, 300, 300, 150, frame.SimTime) : frame.SimTime < 380 ? linterp(300, 380, 150, 90, frame.SimTime) : 90;
        var wantedVS = (0.05 * (wantedAltitude.FeetToMeters() - frame.AltitudeAsl)).Clip(-100.FeetToMeters(), 100.FeetToMeters());
        if (wantedSpeed > 101 || false /* true to prevent transition to the afterburner phase */)
        {
            var wantedPitch = _vspeed2pitchPID.Update(wantedVS - frame.SpeedVertical, frame.dT);
            var wantedPitchAxis = _pitch2axisPID.Update(wantedPitch - frame.Pitch, frame.dT);
            ctl.PitchAxis = _pitch.MoveTo(wantedPitchAxis, frame.SimTime);
            ctl.RollAxis = _bank2axisPID.Update(wantedBank - frame.Bank, frame.dT);
            ctl.ThrottleAxis = _speed2axisPID.Update(wantedSpeed.KtsToMs() - frame.SpeedIndicated, frame.dT);
        }
        else
        {
            ctl.ThrottleAxis = 1.65 + _vspeed2axisSlowPID.Update(wantedVS - frame.SpeedVertical, frame.dT);
            ctl.PitchAxis = _pitch.MoveTo(1, frame.SimTime);//_pitch.MoveTo(_pitch2axisSlowPID.Update(50.ToRad() - frame.Pitch, frame.dT), frame.SimTime);
            ctl.RollAxis = _bank2axisSlowPID.Update(wantedBank - frame.Bank, frame.dT);
        }

        return ctl;
    }

    private double linterp(double x1, double x2, double y1, double y2, double x) => y1 + (x - x1) / (x2 - x1) * (y2 - y1);
}

class BasicPid
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
    private double _prevError;
    public bool Integrating { get; private set; }

    public double Update(double error, double dt)
    {
        Derivative = DerivativeSmoothing * Derivative + (1 - DerivativeSmoothing) * (error - _prevError) / dt;
        _prevError = error;

        double output = P * error + I * ErrorIntegral + D * Derivative;

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
