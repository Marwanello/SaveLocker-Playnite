using System;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// The "click and link" action behind <see cref="SaveLockerPlugin.GetGameMenuItems"/>'s "Link to
    /// SaveLocker" item — the reliable, theme-independent surface, since Playnite owns that menu
    /// itself rather than handing rendering to the theme's own XAML. (A GetGameViewControl-based
    /// button also called into this action, but was removed: no stock theme's XAML ever renders it —
    /// see the comment above <see cref="SaveLockerPlugin.GetGameMenuItems"/> for why.) Tries the same
    /// automatic match/resolve
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
            var alreadyLinked = false;
            try
            {
                var tracked = await client.GetGamesAsync().ConfigureAwait(true);
                linked = GameMatcher.FindMatch(game, tracked);
                alreadyLinked = linked != null;
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
                if (alreadyLinked)
                {
                    // A genuine no-op — game.Name was already matched to `linked` before this click
                    // did anything. A lightweight toast, not the "sync now?" dialog OfferSync shows
                    // for an actually-new link, so right-clicking Link on an already-synced game
                    // (the menu item has no enabled/disabled state the way the button does) doesn't
                    // nag with the same Yes/No prompt every time.
                    api.Notifications.Add(new NotificationMessage(
                        "savelocker-already-linked-" + game.Id,
                        "SaveLocker: \"" + game.Name + "\" is already linked to \"" + linked.Name + "\".",
                        NotificationType.Info));
                }
                else
                {
                    OfferSync(api, client, linked);
                }
            }
            else
            {
                ShowPopup(api, client, game);
            }
        }

        private static void OfferSync(IPlayniteAPI api, LocalApiClient client, TrackedGameDto tracked)
        {
            try
            {
                var choice = api.Dialogs.ShowMessage(
                    "SaveLocker is now linked to \"" + tracked.Name + "\".\n\nSync now to pull the latest save?",
                    "SaveLocker — linked", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
                if (choice == System.Windows.MessageBoxResult.Yes)
                    _ = SyncNowAction.RunAsync(api, client, tracked); // intentionally not awaited — runs in the background
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SaveLocker: couldn't show the linked/sync dialog");
            }
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
                game.Name, game.InstallDirectory, steamAppId, GameMatcher.MapStore(game.Source?.Name)).ConfigureAwait(false);
            if (!lookup.Resolved) return null;

            var enrolled = await client.EnrollAsync(lookup.Id).ConfigureAwait(false);
            if (enrolled.Enrolled < 1) return null;

            // Find the game the enroll above just created by its save-directory PATH, not by name —
            // the server names it after the manifest's canonical spelling when one resolves
            // (Enroller.EnrollAsync), which commonly differs from game.Name (the search query this
            // lookup used), so a name-based search here would silently miss a genuinely successful
            // enroll. See GameMatcher.FindByPathOrName's own doc comment for the full reasoning.
            var refreshed = await client.GetGamesAsync().ConfigureAwait(false);
            var linked = GameMatcher.FindByPathOrName(refreshed, lookup.SuggestedPath, lookup.CandidateName ?? game.Name);

            // Same alias backfill LinkToSaveLockerWindow.BackfillAliasAsync does, inlined rather than
            // shared — that method is private to a window built around a different (interactive)
            // control flow, and this is the same one call from a different entry point. Compares
            // against the SERVER's actual name for `linked` (not the search query above), since that
            // is what may have diverged from game.Name in the first place.
            if (linked != null && !string.Equals(linked.Name, game.Name, StringComparison.OrdinalIgnoreCase))
            {
                try { await client.SetAliasAsync(linked.Id, game.Name).ConfigureAwait(false); }
                catch (Exception ex) { Logger.Warn(ex, "SaveLocker: couldn't backfill alias after automatic enroll"); }
            }

            return linked;
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
    }
}
