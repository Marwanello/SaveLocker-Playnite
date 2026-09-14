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

            var isSteam = string.Equals(game.Source?.Name, "Steam", StringComparison.OrdinalIgnoreCase);
            if (isSteam && uint.TryParse(game.GameId, out var appId))
            {
                var byAppId = tracked.FirstOrDefault(t => t.SteamAppId.HasValue && t.SteamAppId.Value == appId);
                if (byAppId != null) return byAppId;
            }

            if (!string.IsNullOrWhiteSpace(game.InstallDirectory))
            {
                var normalizedGameDir = NormalizeDir(game.InstallDirectory);
                var byDir = tracked.FirstOrDefault(t =>
                    !string.IsNullOrWhiteSpace(t.InstallDir) && NormalizeDir(t.InstallDir) == normalizedGameDir);
                if (byDir != null) return byDir;
            }

            if (!string.IsNullOrWhiteSpace(game.Name))
            {
                var byName = tracked.FirstOrDefault(t =>
                    (!string.IsNullOrWhiteSpace(t.Alias) && string.Equals(t.Alias, game.Name, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(t.Name, game.Name, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName;
            }

            return null;
        }

        private static string NormalizeDir(string path)
        {
            return path.TrimEnd('\\', '/').ToLowerInvariant();
        }
    }
}
