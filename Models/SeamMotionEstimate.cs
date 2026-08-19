using System;
using System.Collections.Generic;

namespace endfield_player_position_display.Models
{
    public sealed class SeamMotionEstimate
    {
        public SeamMotionEstimate(
            double currentHeight,
            double targetHeight,
            double remainingHeight,
            double estimatedSpeed,
            TimeSpan? remainingTime,
            string message,
            IReadOnlyList<SeamMotionSegment> segments)
        {
            CurrentHeight = currentHeight;
            TargetHeight = targetHeight;
            RemainingHeight = remainingHeight;
            EstimatedSpeed = estimatedSpeed;
            RemainingTime = remainingTime;
            Message = message;
            Segments = segments ?? new List<SeamMotionSegment>();
        }

        public double CurrentHeight { get; }
        public double TargetHeight { get; }
        public double RemainingHeight { get; }
        public double EstimatedSpeed { get; }
        public TimeSpan? RemainingTime { get; }
        public string Message { get; }
        public IReadOnlyList<SeamMotionSegment> Segments { get; }
    }
}
