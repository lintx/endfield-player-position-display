using System;

namespace endfield_player_position_display.Tests
{
    internal static class TestAssert
    {
        public static void AreEqual<T>(T expected, T actual)
        {
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException("Expected <" + expected + "> but got <" + actual + ">.");
            }
        }

        public static void AreNear(double expected, double actual, double tolerance)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException("Expected <" + expected + "> but got <" + actual + ">.");
            }
        }

        public static void IsTrue(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Expected condition to be true.");
            }
        }

        public static T Throws<T>(Action action)
            where T : Exception
        {
            try
            {
                action();
            }
            catch (T ex)
            {
                return ex;
            }

            throw new InvalidOperationException("Expected exception " + typeof(T).Name + ".");
        }
    }
}
