namespace DcsAutopilot;

public class BulkData
{
    public DateTime ReceivedUtc;
    public int Bytes;
    public bool ExportAllowed;
    public string DcsVersion;
}

/// <summary>Values that are not present on every aircraft are initialised to NaN.</summary>
public class FrameData
{
    public DateTime ReceivedUtc;
    public int Bytes;
    public int FrameNum;
    /// <summary>
    ///     Number of times DCS Lua tried to receive a UDP control frame before a tick and either got none, or more than one.</summary>
    public int Underflows, Overflows;
    /// <summary>Seconds between DCS Lua populating the data and DcsAutopilot receiving and parsing it (for this data frame).</summary>
    public double LatencyData;
    /// <summary>Seconds between DcsAutopilot sending Control and DCS Lua receiving and parsing it (for prev control frame).</summary>
    public double LatencyControl;
    public string DataRequestsId;

    public double SimTime, dT;
    /// <summary>
    ///     Angle of attack in degrees; -90 (nose down relative to airflow) .. 0 (boresight aligned with airflow) .. 90 (nose
    ///     up relative to airflow)</summary>
    public double AngleOfAttack;
    /// <summary>
    ///     Angle of sideslip in degrees; -90 (nose to the left of the airflow) .. 0 (aligned) .. 90 (nose to the right of the
    ///     airflow)</summary>
    public double AngleOfSideSlip;
    public double PosX, PosY, PosZ;
    public double AccX, AccY, AccZ;
    /// <summary>True ASL altitude in meters. Not affected by the pressure setting.</summary>
    public double AltitudeAsl;
    /// <summary>
    ///     True AGL altitude in meters. Not affected by the pressure setting. Not affected by buildings. High altitude lakes
    ///     are "ground" for the purpose of this reading.</summary>
    public double AltitudeAgl;
    /// <summary>Barometric altitude in meters. Possibly affected by the pressure setting, but always reads 0 on Hornet.</summary>
    public double AltitudeBaro;
    /// <summary>Radar altitude in meters. Affected by buildings. Not affected by the radar range limit (eg in Hornet).</summary>
    public double AltitudeRadar;
    /// <summary>
    ///     True airspeed in m/s. Generally accurate and trustworthy but untested whether it actually takes wind into account.</summary>
    public double SpeedTrue;
    /// <summary>
    ///     Indicated airspeed in m/s. Reported directly by DCS but does not take weather into account, so is of little use
    ///     except in 15degC / 29.92 weather. Use <see cref="SpeedCalibrated"/> instead. This is actually a calibrated
    ///     airspeed as it equals the expected value and does not appear to have instrument error (not thoroughly verified).</summary>
    public double SpeedIndicatedBad;
    /// <summary>
    ///     Mach number. Reported directly by DCS but does not take sea level temperature into account, so is of little use
    ///     except in 15degC weather. See <see cref="SpeedMach"/>.</summary>
    public double SpeedMachBad;
    public double SpeedVertical; // meters/second; details untested
    public double VelX, VelY, VelZ; // meters/second; details untested
    /// <summary>Pitch angle in degrees relative to the horizon; -90 (down) .. 0 (horizon) .. 90 (up).</summary>
    public double Pitch;
    /// <summary>
    ///     Bank angle in degrees relative to the horizon; -180 (upside down) .. -90 (left wing straight down) .. 0 (level) ..
    ///     90 (right wing straight down) .. 180 (upside down)</summary>
    public double Bank;
    /// <summary>
    ///     Compass heading in degrees. This is the true heading (not magnetic) and is not affected by the FCS setting.
    ///     0..360.</summary>
    public double Heading;
    /// <summary>
    ///     Velocity pitch angle in degrees relative to the horizon; -90 (down) .. 0 (horizon) .. 90 (up). This is where the
    ///     velocity vector points.</summary>
    public double VelPitch => Math.Atan2(VelY, Math.Sqrt(VelX * VelX + VelZ * VelZ)).ToDeg();
    /// <summary>
    ///     Angular pitch rate in degrees/second. Positive is pitching up. Relative to the wing axis: this is not the same as
    ///     the rate of change of <see cref="Pitch"/> over time; it's what a gyro would read. A 90 deg bank turn would have a
    ///     large pitch rate even as the horizon-relative <see cref="Pitch"/> stays constant.</summary>
    public double GyroPitch;
    /// <summary>
    ///     Angular roll rate in degrees/second. Positive is roll to the right. Relative to the boresight axis: this is not
    ///     the same as the rate of change of <see cref="Bank"/> over time; it's what a gyro would read.</summary>
    public double GyroRoll;
    /// <summary>
    ///     Angular yaw rate in degrees/second. Positive is yaw to the right. Relative to the vertical airplane axis: this is
    ///     not the same as the rate of change of <see cref="Heading"/> over time; it's what a gyro would read.</summary>
    public double GyroYaw;
    public double FuelInternal, FuelExternal;
    /// <summary>
    ///     Total fuel flow in pounds/hour. May be read off gauges which cause glitches in the reading as it goes through
    ///     changing decimal places.</summary>
    public double FuelFlow = double.NaN;
    public double Flaps, Airbrakes;
    /// <summary>Position of the landing gear lever: 0 for gear up, 1 for gear down, 0..1 while the lever is moving.</summary>
    public double LandingGear = double.NaN;
    public double AileronL, AileronR, ElevatorL, ElevatorR, RudderL, RudderR;
    public double TrimRoll = double.NaN, TrimPitch = double.NaN, TrimYaw = double.NaN;
    public double WindX, WindY, WindZ;
    public double JoyPitch = double.NaN, JoyRoll = double.NaN, JoyYaw = double.NaN, JoyThrottle1 = double.NaN, JoyThrottle2 = double.NaN;
    public double Test1 = double.NaN, Test2 = double.NaN, Test3 = double.NaN, Test4 = double.NaN;

    /// <summary>
    ///     Rate of change of <see cref="Bank"/> in degrees/second. Computed directly from <see cref="Bank"/> with a short
    ///     filter. This can be a better metric than gyro roll rate for controllers attempting to control the bank angle,
    ///     especially when trying to keep it steady.</summary>
    public double BankRate;

    /// <summary>
    ///     Sea level pressure, Pascals. Cannot be obtained from DCS API directly. Required to compute the correct <see
    ///     cref="SpeedMach"/> and <see cref="SpeedCalibrated"/>, because the values reported by Lua API are incorrect if the
    ///     weather isn't 29.92 / 15degC.</summary>
    public double SeaLevelPress = Atmospheric.IsaSeaPress;
    /// <summary>
    ///     Sea level temperature, Kelvin. Cannot be obtained from DCS API directly. Required to compute the correct <see
    ///     cref="SpeedMach"/> and <see cref="SpeedCalibrated"/>, because the values reported by Lua API are incorrect if the
    ///     weather isn't 29.92 / 15degC.</summary>
    public double SeaLevelTemp = Atmospheric.IsaSeaTemp;
    /// <summary>Outside air temperature, Kelvin. Requires <see cref="SeaLevelTemp"/>!</summary>
    public double OutsideAirTemp => SeaLevelTemp - 0.0065 * AltitudeAsl;
    /// <summary>Outside air pressure, Pascals. Requires <see cref="SeaLevelTemp"/> and <see cref="SeaLevelPress"/>!</summary>
    public double OutsideAirPress => SeaLevelPress * Math.Pow(OutsideAirTemp / SeaLevelTemp, 5.25588);
    /// <summary>Speed of sound at current altitude, in m/s. Requires <see cref="SeaLevelTemp"/>!</summary>
    public double SpeedOfSound => Math.Sqrt(OutsideAirTemp * 1.4 * 287.053);
    /// <summary>Mach number. Requires <see cref="SeaLevelTemp"/>!</summary>
    public double SpeedMach => SpeedTrue / SpeedOfSound;
    /// <summary>Calibrated airspeed, in m/s. Requires <see cref="SeaLevelTemp"/> and <see cref="SeaLevelPress"/>!</summary>
    public double SpeedCalibrated => 340.27 * Math.Sqrt(5 * (Math.Pow(OutsideAirPress * (Math.Pow(1 + 0.2 * SpeedMach * SpeedMach, 3.5) - 1) / 101325 + 1, 2.0 / 7.0) - 1));

    /// <summary>Airspeed, m/s, as indicated by the cockpit instrument, without calibration.</summary>
    public double DialSpeedIndicated = double.NaN;
    /// <summary>Airspeed, m/s, as indicated by the cockpit instrument, after calibration.</summary>
    public double DialSpeedCalibrated = double.NaN;
    /// <summary>Mach number as indicated by the cockpit instrument. May have range limits (eg minimum 0.5 for Viper).</summary>
    public double DialSpeedMach = double.NaN;
    /// <summary>Barometric altitude, meters, as indicated by the cockpit instrument. Affected by QNH setting.</summary>
    public double DialAltitudeBaro = double.NaN;
    /// <summary>Altimeter setting, Pascals, as selected on cockpit instruments.</summary>
    public double DialQnh = double.NaN;
}

public class ControlData
{
    /// <summary>
    ///     Pitch input: -1.0 (max pitch down), 0 (neutral), 1.0 (max pitch up). Controls the stick position. The motion range
    ///     varies by plane. F-18: -0.5 to 1.0.</summary>
    public double? PitchAxis;
    /// <summary>Roll input: -1.0 (max roll left), 0 (neutral), 1.0 (max roll right).</summary>
    public double? RollAxis;
    /// <summary>Yaw input: -1.0 (max yaw left), 0 (neutral), 1.0 (max yaw right).</summary>
    public double? YawAxis;
    /// <summary>
    ///     Overall throttle setting; implementation varies by plane. F-16: 0.0-1.5 normal power range; 1.50-1.58 no change;
    ///     1.59-2.00 afterburner. F-18: same but no-change range is 1.50-1.57. Normal power range seems fully proportional
    ///     while afterburner range appears to be stepped.</summary>
    public double? ThrottleAxis;
    /// <summary>
    ///     Absolute pitch trim setting: -1.0 (max trim down), 0 (neutral), 1.0 (max trim up). Note that it can take a while
    ///     for the plane to achieve the specified setting after a large change. Supported: F-16. Not supported: F-18.</summary>
    public double? PitchTrim;
    /// <summary>
    ///     Absolute roll trim setting: -1.0 (max trim left), 0 (neutral), 1.0 (max trim right). Note that it can take a while
    ///     for the plane to achieve the specified setting after a large change. Supported: F-16. Not supported: F-18.</summary>
    public double? RollTrim;
    /// <summary>
    ///     Absolute yaw trim setting: -1.0 (max trim left), 0 (neutral), 1.0 (max trim right). Note that it can take a while
    ///     for the plane to achieve the specified setting after a large change. Supported: F-16. Not supported: F-18.</summary>
    public double? YawTrim;
    /// <summary>
    ///     Rate of change for pitch trim: -1.0 (max rate trim down), 0 (no change), 1.0 (max rate trim up). This typically
    ///     controls the HOTAS trim switch, with -1/1 being full down, and smaller values implemented as PWM (pressing and
    ///     releasing the switch).</summary>
    public double? PitchTrimRate;
    /// <summary>
    ///     Rate of change for roll trim: -1.0 (max rate trim left), 0 (no change), 1.0 (max rate trim right). This typically
    ///     controls the HOTAS trim switch, with -1/1 being full down, and smaller values implemented as PWM (pressing and
    ///     releasing the switch).</summary>
    public double? RollTrimRate;
    public double? SpeedBrakeRate; // 1=more brake, -1=less brake

    /// <summary>
    ///     Merges <paramref name="other"/> into this instance. Returns <c>true</c> if none of the properties are set in both
    ///     instances. Otherwise this instance takes priority and the method returns <c>false</c>.</summary>
    public bool Merge(ControlData other)
    {
        bool ok = true;
        PitchAxis = merge(PitchAxis, other.PitchAxis);
        RollAxis = merge(RollAxis, other.RollAxis);
        YawAxis = merge(YawAxis, other.YawAxis);
        ThrottleAxis = merge(ThrottleAxis, other.ThrottleAxis);
        PitchTrim = merge(PitchTrim, other.PitchTrim);
        RollTrim = merge(RollTrim, other.RollTrim);
        YawTrim = merge(YawTrim, other.YawTrim);
        PitchTrimRate = merge(PitchTrimRate, other.PitchTrimRate);
        RollTrimRate = merge(RollTrimRate, other.RollTrimRate);
        SpeedBrakeRate = merge(SpeedBrakeRate, other.SpeedBrakeRate);
        return ok;

        double? merge(double? a, double? b)
        {
            if (a != null && b != null)
                ok = false;
            return a ?? b;
        }
    }
}
