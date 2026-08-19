using System;

namespace endfield_player_position_display.Models
{
    public sealed class SeamHeightSample
    {
        public SeamHeightSample(DateTimeOffset timestamp, double height)
            : this(timestamp, 0, height, 0, false)
        {
        }

        public SeamHeightSample(DateTimeOffset timestamp, double x, double height, double z)
            : this(timestamp, x, height, z, false)
        {
        }

        public SeamHeightSample(DateTimeOffset timestamp, double x, double height, double z, bool manualBreak)
        {
            Timestamp = timestamp;
            X = x;
            Height = height;
            Z = z;
            ManualBreak = manualBreak;
        }

        public DateTimeOffset Timestamp { get; }
        public double X { get; }
        public double Height { get; }
        public double Z { get; }
        public bool ManualBreak { get; }
    }
}
