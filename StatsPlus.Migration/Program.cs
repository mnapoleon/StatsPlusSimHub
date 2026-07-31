using System;

namespace StatsPlus.Migration
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: StatsPlus.Migration.exe --source <StatsPlus.laps.db> --target <StatsPlus.laps.ldb> [--overwrite]");
                return 2;
            }

            string source = null;
            string target = null;
            bool overwrite = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--source", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    source = args[++i];
                }
                else if (string.Equals(args[i], "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    target = args[++i];
                }
                else if (string.Equals(args[i], "--overwrite", StringComparison.OrdinalIgnoreCase))
                {
                    overwrite = true;
                }
            }

            try
            {
                var result = new SqliteToLiteDbMigrator().Migrate(source, target, overwrite);
                Console.WriteLine($"Migrated {result.TrackHistoryCount} track histories and {result.LapCount} laps.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
