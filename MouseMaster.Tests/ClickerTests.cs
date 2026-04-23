using System;
using System.Collections.Generic;
using System.Linq;
using MouseMaster;
using Xunit;
using Xunit.Abstractions;
namespace MouseMaster.Tests
{
    public class ClickerTests
    {
        private static readonly (int cps, int seed)[] TestCases = { (5, 1), (10, 2), (20, 3), (50, 4), (400, 5) };
        private readonly ITestOutputHelper _out;
        public ClickerTests(ITestOutputHelper output) => _out = output;
        private static List<double> GenerateIntervals(AppSettings settings, int count, int seed)
        {
            var engine = new ClickerEngine(settings, seed);
            var intervals = new List<double>(count);
            for (int i = 0; i < count; i++)
                intervals.Add(engine.Next().IntervalMs);
            return intervals;
        }
        private static double StdDev(List<double> values)
        {
            double avg = values.Average();
            return Math.Sqrt(values.Average(x => (x - avg) * (x - avg)));
        }
        private static double Autocorrelation(List<double> values, int lag)
        {
            double avg = values.Average();
            double num = 0, den = 0;
            for (int i = 0; i < values.Count - lag; i++)
                num += (values[i] - avg) * (values[i + lag] - avg);
            for (int i = 0; i < values.Count; i++)
                den += (values[i] - avg) * (values[i] - avg);
            return num / den;
        }
        [Theory]
        [InlineData(5, 1)]
        [InlineData(10, 2)]
        [InlineData(20, 3)]
        [InlineData(50, 4)]
        [InlineData(400, 5)]
        public void ClickIntervals_ShouldNotHaveExactDuplicates(int cps, int seed)
        {
            var settings = new AppSettings
            {
                IsManualInterval = false,
                TargetCPS = cps,
                Randomize = true,
                RandomStrength = 8,
                Jitter = true,
                JitterX = 3,
                JitterY = 3
            };
            var intervals = GenerateIntervals(settings, 100, seed);
            double mean = intervals.Average();
            double stddev = StdDev(intervals);
            var exactGroups = intervals.GroupBy(x => BitConverter.DoubleToInt64Bits(x)).Where(g => g.Count() > 1).ToList();
            int nearFloor = intervals.Count(x => Math.Abs(x - 2.0) < 0.1);
            Console.WriteLine($"target: cps {cps} | real: {1000.0 / mean:F1} | mean: {mean:F2}ms | stddev: {stddev:F2}ms | range: {intervals.Min():F2}–{intervals.Max():F2}");
            if (exactGroups.Count > 0)
            {
                foreach (var g in exactGroups.Take(3))
                    Console.WriteLine($"  bitwise dupe: {g.Count()} times");
            }
            Assert.True(exactGroups.Count == 0, $"bitwise dupes found at cps {cps}");
        }
        [Theory]
        [InlineData(5, 1)]
        [InlineData(10, 2)]
        [InlineData(20, 3)]
        [InlineData(50, 4)]
        [InlineData(400, 5)]
        public void ClickIntervals_ShouldNotBeTooCorrelated(int cps, int seed)
        {
            var settings = new AppSettings
            {
                IsManualInterval = false,
                TargetCPS = cps,
                Randomize = true,
                RandomStrength = 8
            };
            var intervals = GenerateIntervals(settings, 100, seed);
            double ac = Autocorrelation(intervals, 1);
            Console.WriteLine($"cps {cps} autocorr: {ac:F3}");
            Assert.True(Math.Abs(ac) < 0.5, $"autocorr {ac:F3} too high at cps {cps}");
        }
        [Theory]
        [InlineData(5, 1)]
        [InlineData(10, 2)]
        [InlineData(20, 3)]
        [InlineData(50, 4)]
        [InlineData(400, 5)]
        public void ClickIntervals_ShouldNotClusterAtIntegers(int cps, int seed)
        {
            var settings = new AppSettings
            {
                IsManualInterval = false,
                TargetCPS = cps,
                Randomize = true,
                RandomStrength = 8
            };
            var intervals = GenerateIntervals(settings, 100, seed);
            int intCluster = intervals.Count(x => Math.Abs(x - Math.Round(x)) < 0.01);
            double pct = intCluster * 100.0 / intervals.Count;
            Console.WriteLine($"cps {cps} int cluster: {pct:F1}%");
            Assert.True(pct < 10.0, $"{pct:F1}% int cluster at cps {cps}");
        }
        [Theory]
        [InlineData(5, 1)]
        [InlineData(10, 2)]
        [InlineData(20, 3)]
        [InlineData(50, 4)]
        [InlineData(400, 5)]
        public void ClickIntervals_ShouldNotHitHardFloor(int cps, int seed)
        {
            var settings = new AppSettings
            {
                IsManualInterval = false,
                TargetCPS = cps,
                Randomize = true,
                RandomStrength = 8
            };
            var intervals = GenerateIntervals(settings, 100, seed);
            int nearFloor = intervals.Count(x => Math.Abs(x - 2.0) < 0.1);
            int floorLimit = cps >= 100 ? 40 : 15;
            Console.WriteLine($"cps {cps} near floor: {nearFloor}/100");
            Assert.True(nearFloor < floorLimit, $"{nearFloor} near 2ms floor at cps {cps}");
        }
        [Theory]
        [InlineData(5, 1)]
        [InlineData(10, 2)]
        [InlineData(20, 3)]
        [InlineData(50, 4)]
        [InlineData(400, 5)]
        public void ClickIntervals_ShouldHaveReasonablePeriodicity(int cps, int seed)
        {
            var settings = new AppSettings
            {
                IsManualInterval = false,
                TargetCPS = cps,
                Randomize = true,
                RandomStrength = 8
            };
            var intervals = GenerateIntervals(settings, 100, seed);
            double period2Diff = 0;
            for (int i = 2; i < intervals.Count; i++)
                period2Diff += Math.Abs(intervals[i] - intervals[i - 2]);
            period2Diff /= (intervals.Count - 2);
            double avgInterval = intervals.Average();
            double relativePeriod2 = period2Diff / avgInterval;
            Console.WriteLine($"cps {cps} period-2: {relativePeriod2:F4}");
            Assert.True(relativePeriod2 > 0.02, $"period-2 diff {relativePeriod2:F4} too low at cps {cps}");
        }
        [Fact]
        public void Summary()
        {
            var cpsValues = TestCases;
            Console.WriteLine("\nsum:");
            foreach (var (cps, seed) in cpsValues)
            {
                var settings = new AppSettings { IsManualInterval = false, TargetCPS = cps, Randomize = true, RandomStrength = 8, Jitter = true, JitterX = 3, JitterY = 3 };
                var intervals = GenerateIntervals(settings, 100, seed);
                double mean = intervals.Average();
                double stddev = StdDev(intervals);
                var exactGroups = intervals.GroupBy(x => BitConverter.DoubleToInt64Bits(x)).Where(g => g.Count() > 1).ToList();
                int nearFloor = intervals.Count(x => Math.Abs(x - 2.0) < 0.1);
                Console.WriteLine($"target: {cps} | real: {1000.0 / mean:F1} | mean: {mean:F2}ms | stddev: {stddev:F2}ms | bitwise dupes: {exactGroups.Count} | near floor: {nearFloor}");
            }
        }
    }
}