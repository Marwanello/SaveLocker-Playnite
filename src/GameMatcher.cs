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

        /// <summary>
        /// Finds the game a just-completed enroll created, preferring its save-directory <paramref
        /// name="path"/> over <paramref name="name"/> — the server names a newly-tracked game after
        /// the manifest's canonical spelling when one resolves (Enroller.EnrollAsync: "the MANIFEST's
        /// spelling, not the shortcut's, is the server-side identity"), which is frequently NOT the
        /// name a Playnite-side lookup searched with (e.g. Playnite's "Civ VII" vs. the manifest's
        /// "Sid Meier's Civilization VII"). A name-only lookup right after enrolling would then find
        /// nothing despite the enroll having genuinely succeeded. The save directory the candidate
        /// resolved to is not rewritten that way, so it is the reliable signal; name is only a
        /// fallback for a caller that has no path to check.
        /// </summary>
        public static TrackedGameDto FindByPathOrName(IList<TrackedGameDto> tracked, string path, string name)
        {
            if (tracked == null) return null;

            if (!string.IsNullOrWhiteSpace(path))
            {
                var normalizedPath = NormalizeDir(path);
                var byPath = tracked.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Path) && NormalizeDir(t.Path) == normalizedPath);
                if (byPath != null) return byPath;
            }

            return !string.IsNullOrWhiteSpace(name)
                ? tracked.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        /// <summary>Caller-supplied Playnite source name to the agent's <c>GameStore</c> string —
        /// shared by every caller that resolves a candidate (LinkAction, LinkToSaveLockerWindow)
        /// rather than each keeping its own copy.</summary>
        public static string MapStore(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName)) return null;
            if (sourceName.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0) return "Steam";
            if (sourceName.IndexOf("gog", StringComparison.OrdinalIgnoreCase) >= 0) return "Gog";
            if (sourceName.IndexOf("epic", StringComparison.OrdinalIgnoreCase) >= 0) return "Epic";
            if (sourceName.IndexOf("amazon", StringComparison.OrdinalIgnoreCase) >= 0) return "Amazon";
            return null;
        }

        private static string NormalizeDir(string path)
        {
            return path.TrimEnd('\\', '/').ToLowerInvariant();
        }
    }
}
