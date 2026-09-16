using System;
using System.IO;
using System.Linq;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// Tracks which Playnite games have already been offered the Tier-4 "couldn't automatically
    /// match, link it" nudge (tasks/playnite-plugin/plan.md's "Automatic game matching", point 4) —
    /// shown once ever per game, then silently skippable forever. A flat one-Guid-per-line file is
    /// all this needs (no JSON, no concurrent-writer concerns — one Playnite process, occasional
    /// appends), stored under the plugin's own data directory so it survives a settings reset.
    /// </summary>
    internal static class NudgeState
    {
        private const string FileName = "shown-link-nudges.txt";

        public static bool WasShown(string dataDir, Guid gameId)
        {
            var path = Path.Combine(dataDir, FileName);
            if (!File.Exists(path)) return false;
            return File.ReadAllLines(path).Any(line => string.Equals(line.Trim(), gameId.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        public static void MarkShown(string dataDir, Guid gameId)
        {
            Directory.CreateDirectory(dataDir);
            File.AppendAllLines(Path.Combine(dataDir, FileName), new[] { gameId.ToString() });
        }
    }
}
