using DcsAutopilot;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace DcsExperiments;

class ClimbPerfStraightController : FlightControllerBase
{
    public override string Name { get; set; } = "ClimbPerfStraight";
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
    private SmoothMoverFilter _throttle = new(0, 2, Filters.BesselD5);
    private SmoothMoverFilter _roll = new(-1, 1, Filters.BesselD5);
    private SmoothMoverFilter _pitch = new(-1, 1, Filters.BesselD5);

    public override void ProcessBulkUpdate(BulkData bulk)
    {
        Test.DcsVersion = bulk.DcsVersion;
    }

    public StraightClimbTest Test;
    public string Stage { get; private set; } = "prep";
    public double TgtPitch { get; private set; }
    private double _startX, _startZ, _minVelPitch;
    private Queue<(double time, double mach)> _speedHist = new();

    public override ControlData ProcessFrame(FrameData frame)
    {
        var ctl = new ControlData();
        var wantedHeading = 0;
        var wantedBank = _hdg2bankPID.Update(angdiff(wantedHeading - frame.Heading), frame.dT);
        var wasStage = Stage;
        if (Stage != "done" && Stage != "failed" && Stage != "prep")
        {
            Test.Result.MaxAltitudeFt = Math.Max(Test.Result.MaxAltitudeFt, frame.AltitudeAsl.MetersToFeet());
            Test.Result.RawFuelAtEndInt = frame.FuelInternal;
            Test.Result.RawFuelAtEndExt = frame.FuelExternal;
            Test.Result.ClimbDuration = frame.SimTime - 20;
            Test.Result.ClimbDistance = Math.Sqrt(Math.Pow(frame.PosX - _startX, 2) + Math.Pow(frame.PosZ - _startZ, 2));
            Test.Underflows = frame.Underflows;
            Test.Overflows = frame.Overflows;
            Test.EffectiveFps = frame.FrameNum / frame.SimTime;
        }

        _speed2axisPID.MaxControl = Test.Config.Throttle;

        if (Stage == "prep")
        {
            var wantedSpeed = 200;
            var wantedAlt = 200;
            var wantedPitch = _alt2pitchPID.Update(wantedAlt.FeetToMeters() - frame.AltitudeAsl, frame.dT);
            ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(wantedSpeed.KtsToMs() - frame.SpeedIndicated, frame.dT), frame.SimTime);
            ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(wantedPitch - frame.VelPitch, frame.dT), frame.SimTime);
            if (frame.SimTime >= 20)
            {
                Stage = "lowaccel";
                Test.Result.RawFuelAtStartInt = frame.FuelInternal;
                Test.Result.RawFuelAtStartExt = frame.FuelExternal;
                _startX = frame.PosX;
                _startZ = frame.PosZ;
            }
        }
        else if (Stage == "lowaccel")
        {
            var wantedAlt = 200;
            var wantedPitch = _alt2pitchPID.Update(wantedAlt.FeetToMeters() - frame.AltitudeAsl, frame.dT);
            ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(wantedPitch - frame.VelPitch, frame.dT), frame.SimTime);
            ctl.ThrottleAxis = _throttle.MoveTo(Test.Config.Throttle, frame.SimTime);
            if (frame.SpeedIndicated >= Test.Config.PreClimbSpeedKts.KtsToMs())
            {
                Stage = "pitchup";
                TgtPitch = 0;
            }
        }
        else if (Stage == "pitchup")
        {
            TgtPitch += ((Test.Config.ClimbAngle - frame.VelPitch) / 3).Clip(0.1, 3) * frame.dT;
            ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(TgtPitch - frame.VelPitch, frame.dT), frame.SimTime);
            ctl.ThrottleAxis = _throttle.MoveTo(Test.Config.Throttle, frame.SimTime);
            if (TgtPitch >= Test.Config.ClimbAngle)
            {
                Stage = "climb";
                TgtPitch = Test.Config.ClimbAngle;
            }
        }
        else if (Stage == "climb")
        {
            ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(TgtPitch - frame.VelPitch, frame.dT), frame.SimTime);
            ctl.ThrottleAxis = _throttle.MoveTo(Test.Config.Throttle, frame.SimTime);
            //var wantedSpeed = frame.SpeedIndicated / frame.SpeedMach * 0.90;
            //ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(wantedSpeed - frame.SpeedIndicated, frame.dT), frame.SimTime);
            if (frame.AltitudeAsl.MetersToFeet() < Test.Result.MaxAltitudeFt - 100)
            {
                Stage = "failed";
                Test.Result.FailReason = "climb";
            }
            if (frame.AltitudeAsl > Test.LevelOffAltFt.FeetToMeters())
                Stage = "pitchdown";
        }
        else if (Stage == "pitchdown")
        {
            //_tgtPitch -= 1.5 * frame.dT;
            //ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(_tgtPitch - velPitch, frame.dT), frame.SimTime);
            ctl.PitchAxis = _g2pitchPID.Update(0 - frame.AccY, frame.dT);
            var wantedSpeed = frame.SpeedIndicated / frame.SpeedMach * Test.Config.FinalTargetMach + 5.KtsToMs(); // accel to 5kts over M0.9: don't waste fuel with full throttle, but don't creep up on 0.90 mach too slowly either
            ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(wantedSpeed - frame.SpeedIndicated, frame.dT), frame.SimTime);
            wantedBank = 0;
            if (frame.VelPitch <= 4)
            {
                Stage = "highaccel";
                _velpitch2axisSmoothPID.ErrorIntegral = 0;
                _minVelPitch = frame.VelPitch;
            }
        }
        else if (Stage == "highaccel")
        {
            _minVelPitch = Math.Min(_minVelPitch, frame.VelPitch);
            var wantedPitch = 0;
            ctl.PitchAxis = _pitch.MoveTo(_velpitch2axisSmoothPID.Update(wantedPitch - frame.VelPitch, frame.dT), frame.SimTime);
            var wantedSpeed = frame.SpeedIndicated / frame.SpeedMach * Test.Config.FinalTargetMach + 5.KtsToMs(); // accel to 5kts over M0.9: don't waste fuel with full throttle, but don't creep up on 0.90 mach too slowly either
            ctl.ThrottleAxis = _throttle.MoveTo(_speed2axisPID.Update(wantedSpeed - frame.SpeedIndicated, frame.dT), frame.SimTime);
            if (frame.SpeedMach >= Test.Config.FinalTargetMach && _minVelPitch < 0.1) // this phase starts at 4 deg vel.pitch; we don't want to consider the test ended if the aircraft never properly levelled off (as we could then achieve better numbers with a slightly earlier level-off). So require the vel pitch to drop to essentially zero (but don't enforce the exact pitch at end time as we can't control it with absolute precision)
                Stage = "done";
            if (frame.AltitudeAsl.MetersToFeet() < Test.Result.MaxAltitudeFt - 500)
            {
                Stage = "failed";
                Test.Result.FailReason = "drop"; // eg because it just barely made the level-off alt at very slow speed
            }
            // detect failure to accelerate: if we've got 60 sec of speed data and the change over 60 sec is less than 10% of the remaining speed difference
            _speedHist.Enqueue((frame.SimTime, frame.SpeedMach));
            while (_speedHist.Peek().time < frame.SimTime - 61)
                _speedHist.Dequeue();
            if ((frame.SimTime - _speedHist.Peek().time > 59) && (frame.SpeedMach - _speedHist.Peek().mach < 0.1 * (Test.Config.FinalTargetMach - frame.SpeedMach)))
            {
                Stage = "failed";
                Test.Result.FailReason = "finalaccel";
            }
        }
        else
            wantedBank = 45;
        ctl.RollAxis = _roll.MoveTo(_bank2axisSmoothPID.Update(wantedBank - frame.Bank, frame.dT), frame.SimTime);

        if (wasStage != "done" && wasStage != "failed")
        {
            ClimbPerfTests.Log.Enqueue(Ut.FormatCsvRow(frame.SimTime, frame.FuelInternal, frame.FuelExternal, frame.AltitudeAsl.MetersToFeet(), frame.Pitch, frame.VelPitch, frame.AngleOfAttack, frame.SpeedTrue.MsToKts(), frame.SpeedIndicated.MsToKts(), frame.SpeedMach, frame.SpeedVertical.MetersToFeet(), frame.PosX, frame.PosZ, frame.PosY, frame.AccY, ctl.ThrottleAxis, ctl.RollAxis));
            if (Stage == "done")
                ClimbPerfTests.Log.Enqueue("DONE");
            if (Stage == "failed")
                ClimbPerfTests.Log.Enqueue("FAILED");
        }

        return ctl;
    }

    private double angdiff(double diff) => diff < -180 ? diff + 360 : diff > 180 ? diff - 360 : diff;
}



class ClimbPerfTuneController : FlightControllerBase
{
    public override string Name { get; set; } = "ClimbPerfTune";
    private BasicPid _speed2axisPID = new BasicPid { MinControl = 0, MaxControl = 2, IntegrationLimit = 1 /*m/s / sec*/ }.SetZiNiNone(2.0, 2.1); // at 10k ft
    private BasicPid _hdg2bankPID = new BasicPid { P = 20, I = 0.01, MinControl = -10, MaxControl = 10, IntegrationLimit = 0.1 /*deg/sec*/ }; // at 300 ias 5000ft
    private BasicPid _bank2axisSmoothPID = new BasicPid { MinControl = -1, MaxControl = 1, IntegrationLimit = 5 /*deg/sec*/, DerivativeSmoothing = 0 }.SetZiNiNone(0.05, 3); // at 280 ias kts 5000ft
    private BasicPid _velpitch2axisHighPID = new BasicPid { MinControl = -0.5, MaxControl = 0.5, IntegrationLimit = 0.1, DerivativeSmoothing = 0.8 }.SetZiNiNone(0.22, 2.857);
    private BasicPid _g2pitchPID = new BasicPid { P = 0.10, I = 0.20, D = 0, MinControl = -0.5, MaxControl = 0.5, IntegrationLimit = 1 };
    private SmoothMoverFilter _throttle = new(0, 2, Filters.BesselD5);
    private SmoothMoverFilter _roll = new(-1, 1, Filters.BesselD5);

    private string _stage = "prep";
    private double _tgtPitch;
    private int _curTestAngle = 30;
    private Dictionary<int, double> _lvloffAltByPitch = new();
    private double _actualVelPitch, _actualIAS, _actualTAS, _actualFuel;

    public override ControlData ProcessFrame(FrameData frame)
    {
        var ctl = new ControlData();
        var wantedHeading = 0;
        var wantedBank = _hdg2bankPID.Update(angdiff(wantedHeading - frame.Heading), frame.dT);
        var velPitch = Math.Atan2(frame.VelY, Math.Sqrt(frame.VelX * frame.VelX + frame.VelZ * frame.VelZ)).ToDeg();

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

        ctl.RollAxis = _roll.MoveTo(_bank2axisSmoothPID.Update(wantedBank - frame.Bank, frame.dT), frame.SimTime);
        Status = $"{_stage}; testangle={_curTestAngle}";

        return ctl;
    }

    private double angdiff(double diff) => diff < -180 ? diff + 360 : diff > 180 ? diff - 360 : diff;
}
