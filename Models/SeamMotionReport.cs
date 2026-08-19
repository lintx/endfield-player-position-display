using System;
using System.Collections.Generic;

namespace endfield_player_position_display.Models
{
    public sealed class SeamMotionReport
    {
        public SeamMotionReport(
            double startHeight,
            double startX,
            double startZ,
            DateTimeOffset startTime,
            double endHeight,
            double endX,
            double endZ,
            DateTimeOffset endTime,
            double totalDistance,
            TimeSpan totalDuration,
            double averageSpeed,
            IReadOnlyList<SeamMotionSegment> segments)
        {
            StartHeight = startHeight;
            StartX = startX;
            StartZ = startZ;
            StartTime = startTime;
            EndHeight = endHeight;
            EndX = endX;
            EndZ = endZ;
            EndTime = endTime;
            TotalDistance = totalDistance;
            TotalDuration = totalDuration;
            AverageSpeed = averageSpeed;
            Segments = segments ?? new List<SeamMotionSegment>();
        }

        public double StartHeight { get; }
        public double StartX { get; }
        public double StartZ { get; }
        public DateTimeOffset StartTime { get; }
        public double EndHeight { get; }
        public double EndX { get; }
        public double EndZ { get; }
        public DateTimeOffset EndTime { get; }
        public double TotalDistance { get; }
        public TimeSpan TotalDuration { get; }
        public double AverageSpeed { get; }
        public IReadOnlyList<SeamMotionSegment> Segments { get; }
    }
}
