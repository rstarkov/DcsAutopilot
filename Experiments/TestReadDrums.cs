using DcsAutopilot;
using RT.Util.ExtensionMethods;

namespace DcsExperiments;

static class TestReadDrums
{
    public static void ReadDrumsTests()
    {
        testRand(2949.4, true, 9.4, 4.4, 9.0, 2.0);
        testRand(1999.01489, true, 9.01489, 9.01489, 9.01489, 1.01489);
        testRand(4659.9931, true, 9.9931, 5.9931, 6.0, 4.0);
        var drumz = new double[4];
        for (int i = 0; i < 50000; i++)
        {
            Console.Title = i.ToString();
            var num = Random.Shared.NextDouble(0, 10000);
            makeDrums(num, drumz);
            testRand(num, false, drumz);
        }

        static void assertEqual(double expected, double actual) { if (Math.Abs(expected - actual) > 0.00001) throw new Exception(); }

        static void testMakeDrums(double num, params double[] expected)
        {
            var dr = new double[expected.Length];
            makeDrums(num, dr);
            for (int i = 0; i < dr.Length; i++)
                assertEqual(expected[i], dr[i]);
        }

        static void testRand(double expected, bool manual, params double[] drums)
        {
            if (manual) // test makeDrums itself on this manual example
                testMakeDrums(expected, drums);
            assertEqual(expected, Util.ReadDrums(drums));
            for (int i = 0; i < 2000; i++)
            {
                var dr = drums.Select(d => d + Random.Shared.NextDouble(-0.005, 0.005)).ToArray();
                var exp = expected + dr[0] - drums[0]; // correct for change to first drum's fractional part
                if (exp < 10000)
                    assertEqual(exp, Util.ReadDrums(dr));
            }
        }

        static void makeDrums(double num, double[] drums)
        {
            drums[0] = num % 10.0;
            var transitionPos = drums[0] - Math.Floor(drums[0]);
            var scale = 1.0;
            bool all9 = drums[0] >= 9;
            for (int d = 1; d < drums.Length; d++)
            {
                scale *= 10;
                var digit = Math.Floor(num / scale % 10);
                if (all9)
                    digit += transitionPos;
                if (digit < 9)
                    all9 = false;
                drums[d] = digit;
            }
        }
    }
}
