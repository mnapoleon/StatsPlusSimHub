using System.Collections.Generic;

namespace Affinity
{
    public class AffinitySettings
    {
        public bool EnablePlugin { get; set; } = true;

        public bool DisplayInMiles { get; set; }

        public bool EnableDebugLogging { get; set; }

        public Dictionary<string, bool> GameDebugLogging { get; set; } = new Dictionary<string, bool>();

        public void Reset()
        {
            EnablePlugin = true;
            DisplayInMiles = false;
            EnableDebugLogging = false;
            GameDebugLogging = new Dictionary<string, bool>();
        }
    }
}
