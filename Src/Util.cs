﻿using System;

namespace DcsAutopilot;

static class Util
{
    public static double ToDeg(this double rad) => rad / Math.PI * 180;
    public static double ToRad(this double deg) => deg / 180.0 * Math.PI;
    public static double ToRad(this int deg) => deg / 180.0 * Math.PI;
    public static double FeetToMeters(this double feet) => feet / 3.2808399;
    public static double MetersToFeet(this double meters) => meters * 3.2808399;
}