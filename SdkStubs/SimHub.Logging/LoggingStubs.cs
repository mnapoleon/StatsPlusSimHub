using log4net;

namespace SimHub
{
    public static class Logging
    {
        private static readonly ILog NullLog = LogManager.GetLogger(typeof(Logging));

        public static ILog Current => NullLog;

        public static void FlushBuffers()
        {
        }

        public static void Reset()
        {
        }

        public static void AddDebugChartPoint(string name, double value)
        {
        }

        public static void AddDebugString(string name, string value)
        {
        }
    }
}
