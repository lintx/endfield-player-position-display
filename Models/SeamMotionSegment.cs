using System;

namespace endfield_player_position_display.Models
{
    public sealed class SeamMotionSegment
    {
        public SeamMotionSegment(
            DateTimeOffset startTime,
            DateTimeOffset endTime,
            double startHeight,
            double endHeight)
            : this(startTime, endTime, 0, startHeight, 0, 0, endHeight, 0, null)
        {
        }

        public SeamMotionSegment(
            DateTimeOffset startTime,
            DateTimeOffset endTime,
            double startX,
            double startHeight,
            double startZ,
            double endX,
            double endHeight,
            double endZ,
            string reason)
        {
            StartTime = startTime;
            EndTime = endTime;
            StartX = startX;
            StartHeight = startHeight;
            StartZ = startZ;
            EndX = endX;
            EndHeight = endHeight;
            EndZ = endZ;
            Reason = reason;
            Distance = Math.Abs(endHeight - startHeight);
            Duration = endTime - startTime;
            Speed = Duration.TotalSeconds > 0
                ? Distance / Duration.TotalSeconds
                : 0;
        }

        public DateTimeOffset StartTime { get; }
        public DateTimeOffset EndTime { get; }
        public double StartX { get; }
        public double StartHeight { get; }
        public double StartZ { get; }
        public double EndX { get; }
        public double EndHeight { get; }
        public double EndZ { get; }
        public string Reason { get; }
        public double Distance { get; }
        public TimeSpan Duration { get; }
        public double Speed { get; }
    }
}
