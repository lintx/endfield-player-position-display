using System;
using System.Collections.Generic;
using endfield_player_position_display.Models;
using endfield_player_position_display.Services;

namespace endfield_player_position_display.Tests
{
    internal static class SeamMotionAnalyzerTests
    {
        public static void AnalyzeIgnoresPauseAndUsesRecentSegmentSpeed()
        {
            var samples = new List<SeamHeightSample>
            {
                Sample(0, 100.000),
                Sample(1, 100.003),
                Sample(2, 100.006),
                Sample(3, 100.006),
                Sample(4, 100.006),
                Sample(5, 100.006),
                Sample(6, 100.032),
                Sample(7, 100.062),
                Sample(8, 100.092)
            };

            SeamMotionEstimate estimate = SeamMotionAnalyzer.Analyze(samples, 100.122);

            TestAssert.AreEqual(1, estimate.Segments.Count);
            TestAssert.AreNear(100.000, estimate.Segments[0].StartHeight, 0.0001);
            TestAssert.AreNear(100.092, estimate.Segments[0].EndHeight, 0.0001);
            TestAssert.AreNear(0.030, estimate.RemainingHeight, 0.001);
            TestAssert.IsTrue(estimate.RemainingTime.HasValue);
            TestAssert.IsTrue(estimate.RemainingTime.Value.TotalSeconds < 2.0);
        }

        public static void BuildReportUsesFirstLastSamplesAndKeepsMovingSegments()
        {
            var samples = new List<SeamHeightSample>
            {
                Sample(0, 100.000),
                Sample(1, 100.003),
                Sample(2, 100.006),
                Sample(3, 100.006),
                Sample(4, 100.006),
                Sample(5, 100.006),
                Sample(6, 100.032),
                Sample(7, 100.062),
                Sample(8, 100.092)
            };

            SeamMotionReport report = SeamMotionAnalyzer.BuildReport(samples);

            TestAssert.AreNear(100.000, report.StartHeight, 0.0001);
            TestAssert.AreNear(100.092, report.EndHeight, 0.0001);
            TestAssert.AreEqual(1, report.Segments.Count);
            TestAssert.AreNear(report.StartHeight, report.Segments[0].StartHeight, 0.0001);
            TestAssert.AreNear(report.EndHeight, report.Segments[0].EndHeight, 0.0001);
            TestAssert.AreNear(0.092, report.TotalDistance, 0.0001);
            TestAssert.AreNear(8.0, report.TotalDuration.TotalSeconds, 0.0001);
        }

        public static void BuildReportKeepsSegmentsContinuousAfterXzBreaks()
        {
            var samples = new List<SeamHeightSample>
            {
                Sample(0, 10.00, 309.806, 20.00),
                Sample(1, 10.05, 310.291, 20.04),
                Sample(2, 10.08, 310.650, 20.02),
                Sample(3, 10.62, 310.982, 20.55),
                Sample(4, 10.66, 311.200, 20.57)
            };

            SeamMotionReport report = SeamMotionAnalyzer.BuildReport(samples);

            TestAssert.AreEqual(2, report.Segments.Count);
            TestAssert.AreNear(309.806, report.Segments[0].StartHeight, 0.0001);
            TestAssert.AreNear(report.Segments[0].EndHeight, report.Segments[1].StartHeight, 0.0001);
            TestAssert.AreNear(311.200, report.Segments[1].EndHeight, 0.0001);
            TestAssert.AreEqual("XZ 波动", report.Segments[0].Reason);
            TestAssert.AreNear(10.62, report.Segments[0].EndX, 0.0001);
            TestAssert.AreNear(20.55, report.Segments[0].EndZ, 0.0001);
        }

        public static void BuildReportKeepsSegmentsContinuousAfterManualBreaks()
        {
            var samples = new List<SeamHeightSample>
            {
                Sample(0, 10.00, 309.806, 20.00),
                Sample(1, 10.02, 310.291, 20.03),
                Sample(2, 10.03, 310.650, 20.02, true),
                Sample(3, 10.04, 310.982, 20.04),
                Sample(4, 10.05, 311.200, 20.05)
            };

            SeamMotionReport report = SeamMotionAnalyzer.BuildReport(samples);

            TestAssert.AreEqual(2, report.Segments.Count);
            TestAssert.AreNear(309.806, report.Segments[0].StartHeight, 0.0001);
            TestAssert.AreNear(report.Segments[0].EndHeight, report.Segments[1].StartHeight, 0.0001);
            TestAssert.AreNear(311.200, report.Segments[1].EndHeight, 0.0001);
            TestAssert.AreEqual("手动打点", report.Segments[0].Reason);
        }

        private static SeamHeightSample Sample(int second, double height)
        {
            return new SeamHeightSample(
                new DateTimeOffset(2026, 6, 30, 12, 0, second, TimeSpan.Zero),
                height);
        }

        private static SeamHeightSample Sample(int second, double x, double height, double z, bool manualBreak = false)
        {
            return new SeamHeightSample(
                new DateTimeOffset(2026, 6, 30, 12, 0, second, TimeSpan.Zero),
                x,
                height,
                z,
                manualBreak);
        }
    }
}
