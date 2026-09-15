using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// Matches a Playnite <see cref="Game"/> to a SaveLocker tracked game — the priority chain from
    /// tasks/playnite-plugin/plan.md's "Automatic game matching": Steam AppID (exact, automatic) →
    /// normalized install directory (automatic) → name/Alias (automatic, reuses the same field
    /// Decky's own plugin already writes through <c>/api/games/{id}/alias</c>). Tier 4 (the
    /// dismissible "couldn't match, link it" nudge) needs the picker Phase 12 builds and is not here.
    /// </summary>
    internal static class GameMatcher
    {
        public static TrackedGameDto FindMatch(Game game, IList<TrackedGameDto> tracked)
        {
            if (game == null || tracked == null || tracked.Count == 0) return null;

            // Each tier below stops at the first ambiguous match rather than falling through to
            // FirstOrDefault's arbitrary pick — if two tracked games tie on a signal this specific,
            // guessing risks binding a save to the wrong game entirely, and a weaker tier (e.g. name)
            // is no more likely to disambiguate them correctly than this one was.
            var isSteam = string.Equals(game.Source?.Name, "Steam", StringComparison.OrdinalIgnoreCase);
            if (isSteam && uint.TryParse(game.GameId, out var appId))
            {
                var byAppId = tracked.Where(t => t.SteamAppId.HasValue && t.SteamAppId.Value == appId).ToList();
                if (byAppId.Count > 0) return byAppId.Count == 1 ? byAppId[0] : null;
            }

            if (!string.IsNullOrWhiteSpace(game.InstallDirectory))
            {
                var normalizedGameDir = NormalizeDir(game.InstallDirectory);
                var byDir = tracked.Where(t =>
                    !string.IsNullOrWhiteSpace(t.InstallDir) && NormalizeDir(t.InstallDir) == normalizedGameDir).ToList();
                if (byDir.Count > 0) return byDir.Count == 1 ? byDir[0] : null;
            }

            if (!string.IsNullOrWhiteSpace(game.Name))
            {
                var byName = tracked.Where(t =>
                    (!string.IsNullOrWhiteSpace(t.Alias) && string.Equals(t.Alias, game.Name, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(t.Name, game.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (byName.Count > 0) return byName.Count == 1 ? byName[0] : null;
            }

            return null;
        }

        private static string NormalizeDir(string path)
        {
            return path.TrimEnd('\\', '/').ToLowerInvariant();
        }
    }
}
