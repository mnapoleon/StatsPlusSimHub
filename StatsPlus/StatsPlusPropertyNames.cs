using System.Text.RegularExpressions;

namespace StatsPlus
{
    internal static class StatsPlusPropertyNames
    {
        internal static string BuildPersonalBestPropertyName(string gameName, string carModel, string trackVariation)
        {
            return $"StatsPlus.PersonalBest.{SanitizePropertySegment(gameName)}.{SanitizePropertySegment(carModel)}.{SanitizePropertySegment(trackVariation)}";
        }

        private static string SanitizePropertySegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            string sanitized = Regex.Replace(value.Trim(), @"[^A-Za-z0-9]+", "_").Trim('_');
            return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
        }
    }
}
