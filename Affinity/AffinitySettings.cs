namespace Affinity
{
    public class AffinitySettings
    {
        public bool EnablePlugin { get; set; } = true;

        public bool DisplayInMiles { get; set; }

        public void Reset()
        {
            EnablePlugin = true;
            DisplayInMiles = false;
        }
    }
}
