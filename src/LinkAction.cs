using System;
using System.Linq;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// The "click and link" action shared by <see cref="LinkStatusButton"/> (GetGameViewControl —
    /// theme-dependent, see its own doc comment) and <see cref="SaveLockerPlugin.GetGameMenuItems"/>
    /// (the right-click menu — works regardless of theme, since Playnite owns that menu itself
    /// rather than handing rendering to the theme's own XAML). Tries the same automatic match/resolve
    /// chain <see cref="LinkToSaveLockerWindow"/> runs for tiers 1-2 and enrolls immediately with no
    /// extra confirmation click if that resolves a save folder; only falls back to the full popup
    /// (manual search/browse/pick) when it doesn't.
    ///
    /// Asked for directly: the automatic check must run in the background without taking over the
    /// screen — a toast while it's working, not a modal that blocks browsing the library — and only
    /// the outcome (linked, offering to sync) should actually stop the player for a decision. Plain
    /// async/await keeps the UI thread free for that; ActivateGlobalProgress (used deliberately by
    /// SaveLockerPlugin.OnGameStarting, where blocking is the point — nothing should launch before
    /// that check finishes) is exactly the modal behaviour this needs to avoid.
    /// </summary>
    internal static class LinkAction
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static async Task RunAsync(IPlayniteAPI api, LocalApiClient client, Game game)
        {
            var progressId = "savelocker-linking-" + game.Id;
            api.Notifications.Add(new NotificationMessage(
                progressId, "SaveLocker: linking \"" + game.Name + "\"…", NotificationType.Info));

            TrackedGameDto linked = null;
            try
            {
                var tracked = await client.GetGamesAsync().ConfigureAwait(true);
                linked = GameMatcher.FindMatch(game, tracked);
                if (linked == null)
                    linked = await TryAutomaticEnrollAsync(client, game).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "SaveLocker: automatic link check failed, falling back to the picker");
            }
            finally
            {
                api.Notifications.Remove(progressId);
            }

            if (linked != null)
            {
                LinkedTag.Ensure(api, game);
                OfferSync(api, client, linked);
            }
            else
            {
                ShowPopup(api, client, game);
            }
        }

        private static void OfferSync(IPlayniteAPI api, LocalApiClient client, TrackedGameDto tracked)
        {
            var choice = api.Dialogs.ShowMessage(
                "SaveLocker is now linked to \"" + tracked.Name + "\".\n\nSync now to pull the latest save?",
                "SaveLocker — linked", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
            if (choice == System.Windows.MessageBoxResult.Yes)
                _ = SyncNowAction.RunAsync(api, client, tracked); // intentionally not awaited — runs in the background
        }

        /// <summary>
        /// Tiers 1-2 of <see cref="LinkToSaveLockerWindow"/>'s own chain, headless: an automatic
        /// manifest lookup that enrolls immediately when it resolves a real save folder, with no
        /// confirm screen — the one click IS the confirmation. Tier 1 (already-tracked) is checked by
        /// the caller before this runs. Returns null for anything this can't resolve on its own, which
        /// the caller then hands to the interactive popup instead.
        /// </summary>
        private static async Task<TrackedGameDto> TryAutomaticEnrollAsync(LocalApiClient client, Game game)
        {
            var isSteam = string.Equals(game.Source?.Name, "Steam", StringComparison.OrdinalIgnoreCase);
            var steamAppId = isSteam && uint.TryParse(game.GameId, out _) ? game.GameId : null;
            var lookup = await client.CandidatesLookupAsync(
                game.Name, game.InstallDirectory, steamAppId, MapStore(game.Source?.Name)).ConfigureAwait(false);
            if (!lookup.Resolved) return null;

            var enrolled = await client.EnrollAsync(lookup.Id).ConfigureAwait(false);
            if (enrolled.Enrolled < 1) return null;

            var displayName = lookup.CandidateName ?? game.Name;
            // Same alias backfill LinkToSaveLockerWindow.BackfillAliasAsync does, inlined rather than
            // shared — that method is private to a window built around a different (interactive)
            // control flow, and this is the same two calls from a different entry point.
            if (!string.Equals(displayName, game.Name, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var games = await client.GetGamesAsync().ConfigureAwait(false);
                    var created = games.FirstOrDefault(g => string.Equals(g.Name, displayName, StringComparison.OrdinalIgnoreCase));
                    if (created != null) await client.SetAliasAsync(created.Id, game.Name).ConfigureAwait(false);
                }
                catch (Exception ex) { Logger.Warn(ex, "SaveLocker: couldn't backfill alias after automatic enroll"); }
            }

            var refreshed = await client.GetGamesAsync().ConfigureAwait(false);
            return refreshed.FirstOrDefault(g => string.Equals(g.Name, displayName, StringComparison.OrdinalIgnoreCase));
        }

        private static void ShowPopup(IPlayniteAPI api, LocalApiClient client, Game game)
        {
            try
            {
                var themedWindow = api.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                });
                themedWindow.Owner = api.Dialogs.GetCurrentAppWindow();
                new LinkToSaveLockerWindow(themedWindow, api, client, game).ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SaveLocker: link popup failed to open");
            }
        }

        private static string MapStore(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName)) return null;
            if (sourceName.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0) return "Steam";
            if (sourceName.IndexOf("gog", StringComparison.OrdinalIgnoreCase) >= 0) return "Gog";
            if (sourceName.IndexOf("epic", StringComparison.OrdinalIgnoreCase) >= 0) return "Epic";
            if (sourceName.IndexOf("amazon", StringComparison.OrdinalIgnoreCase) >= 0) return "Amazon";
            return null;
        }
    }
}
