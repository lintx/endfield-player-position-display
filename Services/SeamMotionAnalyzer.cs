using System;
using System.Collections.Generic;
using System.Linq;
using endfield_player_position_display.Models;

namespace endfield_player_position_display.Services
{
    public static class SeamMotionAnalyzer
    {
        private const double MovingSpeedThreshold = 0.0015;
        private const double StoppingSpeedThreshold = 0.0007;
        private const int StopStreakToClose = 3;
        private const double MinimumSegmentDistance = 0.003;
        private const double MinimumSegmentSeconds = 1.0;
        private const double XzRangeBreakThreshold = 0.45;
        private const int XzBreakMinimumSamples = 3;

        public static SeamMotionEstimate Analyze(IReadOnlyList<SeamHeightSample> samples, double targetHeight)
        {
            if (samples == null || samples.Count == 0)
            {
                return new SeamMotionEstimate(0, targetHeight, Math.Abs(targetHeight), 0, null, "等待更多数据", new List<SeamMotionSegment>());
            }

            List<SeamMotionSegment> reportSegments = BuildReportSegments(samples);
            List<SeamMotionSegment> movingSegments = BuildMovingSegments(samples);
            List<SeamMotionSegment> usableSegments = movingSegments
                .Where(segment => segment.Distance >= MinimumSegmentDistance && segment.Duration.TotalSeconds >= MinimumSegmentSeconds)
                .ToList();

            double currentHeight = samples[samples.Count - 1].Height;
            double remainingHeight = Math.Abs(targetHeight - currentHeight);
            if (remainingHeight <= 0.000001)
            {
                return new SeamMotionEstimate(currentHeight, targetHeight, 0, 0, TimeSpan.Zero, "已到达目标高度", reportSegments);
            }

            double activeSpeed = GetRecentWindowSpeed(samples);
            double recentSpeed = GetRecentSegmentSpeed(usableSegments);
            double overallSpeed = GetOverallSpeed(usableSegments);
            double estimatedSpeed = CombineSpeed(activeSpeed, recentSpeed, overallSpeed);
            if (estimatedSpeed <= 0)
            {
                return new SeamMotionEstimate(currentHeight, targetHeight, remainingHeight, 0, null, "等待更多运动数据", reportSegments);
            }

            TimeSpan remainingTime = TimeSpan.FromSeconds(remainingHeight / estimatedSpeed);
            return new SeamMotionEstimate(currentHeight, targetHeight, remainingHeight, estimatedSpeed, remainingTime, "估算中", reportSegments);
        }

        public static SeamMotionReport BuildReport(IReadOnlyList<SeamHeightSample> samples)
        {
            if (samples == null || samples.Count == 0)
            {
                return null;
            }

            List<SeamMotionSegment> segments = BuildReportSegments(samples);
            SeamHeightSample first = samples[0];
            SeamHeightSample last = samples[samples.Count - 1];
            TimeSpan totalDuration = last.Timestamp - first.Timestamp;
            double totalDistance = Math.Abs(last.Height - first.Height);
            double averageSpeed = totalDuration.TotalSeconds > 0
                ? totalDistance / totalDuration.TotalSeconds
                : 0;

            return new SeamMotionReport(
                first.Height,
                first.X,
                first.Z,
                first.Timestamp,
                last.Height,
                last.X,
                last.Z,
                last.Timestamp,
                totalDistance,
                totalDuration,
                averageSpeed,
                segments);
        }

        private static List<SeamMotionSegment> BuildReportSegments(IReadOnlyList<SeamHeightSample> samples)
        {
            var segments = new List<SeamMotionSegment>();
            if (samples == null || samples.Count < 2)
            {
                return segments;
            }

            int startIndex = 0;
            for (int i = 1; i < samples.Count; i++)
            {
                // 报告分段必须首尾相接，避免把起始点、暂停段或结束点从日志中丢掉。
                if (samples[i].ManualBreak)
                {
                    AddReportSegment(samples, startIndex, i, "手动打点", segments);
                    startIndex = i;
                    continue;
                }

                if (i - startIndex >= XzBreakMinimumSamples && HasXzRangeBreak(samples, startIndex, i))
                {
                    AddReportSegment(samples, startIndex, i, "XZ 波动", segments);
                    startIndex = i;
                }
            }

            AddReportSegment(samples, startIndex, samples.Count - 1, null, segments);
            return segments;
        }

        private static bool HasXzRangeBreak(IReadOnlyList<SeamHeightSample> samples, int startIndex, int endIndex)
        {
            double minX = samples[startIndex].X;
            double maxX = samples[startIndex].X;
            double minZ = samples[startIndex].Z;
            double maxZ = samples[startIndex].Z;

            for (int i = startIndex + 1; i <= endIndex; i++)
            {
                minX = Math.Min(minX, samples[i].X);
                maxX = Math.Max(maxX, samples[i].X);
                minZ = Math.Min(minZ, samples[i].Z);
                maxZ = Math.Max(maxZ, samples[i].Z);
            }

            double dx = maxX - minX;
            double dz = maxZ - minZ;
            return Math.Sqrt(dx * dx + dz * dz) >= XzRangeBreakThreshold;
        }

        private static void AddReportSegment(
            IReadOnlyList<SeamHeightSample> samples,
            int startIndex,
            int endIndex,
            string reason,
            ICollection<SeamMotionSegment> segments)
        {
            if (startIndex < 0 || endIndex <= startIndex || endIndex >= samples.Count)
            {
                return;
            }

            SeamHeightSample start = samples[startIndex];
            SeamHeightSample end = samples[endIndex];
            segments.Add(new SeamMotionSegment(
                start.Timestamp,
                end.Timestamp,
                start.X,
                start.Height,
                start.Z,
                end.X,
                end.Height,
                end.Z,
                reason));
        }

        private static List<SeamMotionSegment> BuildMovingSegments(IReadOnlyList<SeamHeightSample> samples)
        {
            var segments = new List<SeamMotionSegment>();
            if (samples == null || samples.Count < 2)
            {
                return segments;
            }

            int startIndex = -1;
            int stopStreak = 0;

            for (int i = 1; i < samples.Count; i++)
            {
                SeamHeightSample previous = samples[i - 1];
                SeamHeightSample current = samples[i];
                double seconds = (current.Timestamp - previous.Timestamp).TotalSeconds;
                if (seconds <= 0)
                {
                    continue;
                }

                double speed = Math.Abs(current.Height - previous.Height) / seconds;
                bool moving = speed >= MovingSpeedThreshold;

                if (startIndex < 0)
                {
                    if (moving)
                    {
                        startIndex = i - 1;
                    }

                    stopStreak = 0;
                    continue;
                }

                if (moving)
                {
                    stopStreak = 0;
                    continue;
                }

                if (speed <= StoppingSpeedThreshold)
                {
                    stopStreak++;
                    if (stopStreak >= StopStreakToClose)
                    {
                        AddMovingSegment(samples, startIndex, i - stopStreak, segments);
                        startIndex = -1;
                        stopStreak = 0;
                    }
                }
                else
                {
                    stopStreak = 0;
                }
            }

            if (startIndex >= 0)
            {
                AddMovingSegment(samples, startIndex, samples.Count - 1, segments);
            }

            return segments;
        }

        private static void AddMovingSegment(IReadOnlyList<SeamHeightSample> samples, int startIndex, int endIndex, ICollection<SeamMotionSegment> segments)
        {
            if (startIndex < 0 || endIndex <= startIndex || endIndex >= samples.Count)
            {
                return;
            }

            SeamHeightSample start = samples[startIndex];
            SeamHeightSample end = samples[endIndex];
            SeamMotionSegment segment = new SeamMotionSegment(
                start.Timestamp,
                end.Timestamp,
                start.X,
                start.Height,
                start.Z,
                end.X,
                end.Height,
                end.Z,
                null);
            if (segment.Distance < MinimumSegmentDistance || segment.Duration.TotalSeconds < MinimumSegmentSeconds)
            {
                return;
            }

            segments.Add(segment);
        }

        private static double GetRecentWindowSpeed(IReadOnlyList<SeamHeightSample> samples)
        {
            if (samples == null || samples.Count < 2)
            {
                return 0;
            }

            int startIndex = Math.Max(0, samples.Count - 5);
            SeamHeightSample start = samples[startIndex];
            SeamHeightSample end = samples[samples.Count - 1];
            double seconds = (end.Timestamp - start.Timestamp).TotalSeconds;
            if (seconds <= 0)
            {
                return 0;
            }

            double speed = Math.Abs(end.Height - start.Height) / seconds;
            return speed >= MovingSpeedThreshold ? speed : 0;
        }

        private static double GetRecentSegmentSpeed(IReadOnlyList<SeamMotionSegment> segments)
        {
            if (segments == null || segments.Count == 0)
            {
                return 0;
            }

            int startIndex = Math.Max(0, segments.Count - 3);
            double weightedDistance = 0;
            double weightedTime = 0;
            for (int i = startIndex; i < segments.Count; i++)
            {
                SeamMotionSegment segment = segments[i];
                weightedDistance += segment.Distance * (i - startIndex + 1);
                weightedTime += segment.Duration.TotalSeconds * (i - startIndex + 1);
            }

            return weightedTime > 0 ? weightedDistance / weightedTime : 0;
        }

        private static double GetOverallSpeed(IReadOnlyList<SeamMotionSegment> segments)
        {
            if (segments == null || segments.Count == 0)
            {
                return 0;
            }

            double distance = 0;
            double seconds = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                distance += segments[i].Distance;
                seconds += segments[i].Duration.TotalSeconds;
            }

            return seconds > 0 ? distance / seconds : 0;
        }

        private static double CombineSpeed(double activeSpeed, double recentSpeed, double overallSpeed)
        {
            if (activeSpeed > 0 && recentSpeed > 0 && overallSpeed > 0)
            {
                return activeSpeed * 0.5 + recentSpeed * 0.3 + overallSpeed * 0.2;
            }

            if (activeSpeed > 0 && recentSpeed > 0)
            {
                return activeSpeed * 0.6 + recentSpeed * 0.4;
            }

            if (recentSpeed > 0 && overallSpeed > 0)
            {
                return recentSpeed * 0.7 + overallSpeed * 0.3;
            }

            if (activeSpeed > 0)
            {
                return activeSpeed;
            }

            if (recentSpeed > 0)
            {
                return recentSpeed;
            }

            return overallSpeed;
        }
    }
}
