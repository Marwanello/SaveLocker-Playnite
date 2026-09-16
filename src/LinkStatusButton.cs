using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// The always-visible "Link to SaveLocker" control for a game's details view
    /// (<c>GenericPlugin.GetGameViewControl</c>) — the button-based alternative to the passive
    /// Tier-4 nudge (SaveLockerPlugin.MaybeShowLinkNudge), asked for directly: a player should not
    /// have to launch a game first to discover it isn't linked.
    ///
    /// <b>Theme-dependent — confirmed NOT rendered by the Harmony theme</b> (this project's own
    /// hardware-verification theme, tasks/playnite-plugin/plan.md/docs/CONTEXT.md): Harmony's
    /// DetailsViewGameOverview.xaml wires a fixed, hand-picked list of {PluginName}_PluginButton
    /// elements (HowLongToBeat, SuccessStory, GameActivity, etc.) rather than looping over every
    /// installed plugin generically, and SaveLocker is not on that list — so Playnite never calls
    /// this class's constructor at all under Harmony. Left in for themes that DO support a generic
    /// GetGameViewControl (the stock Default theme is expected to; not yet confirmed on hardware).
    /// <see cref="SaveLockerPlugin.GetGameMenuItems"/> (the right-click menu) is the reliable,
    /// theme-independent surface and should be treated as the primary one until every theme this
    /// project cares about is actually checked.
    /// </summary>
    internal sealed class LinkStatusButton : Button
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IPlayniteAPI api;
        private readonly LocalApiClient client;
        // Bumped on every DataContext change so a slow lookup for a game the player has since
        // navigated away from can't overwrite the button state of whichever game is showing now.
        private int generation;

        public LinkStatusButton(IPlayniteAPI api, LocalApiClient client)
        {
            this.api = api;
            this.client = client;

            // Right-aligned within whatever container the active theme gives this control — a
            // plugin has no way to place it at a fixed corner of the whole application window, only
            // within the region the theme's own layout defines for GetGameViewControl.
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(4);
            Padding = new Thickness(10, 4, 10, 4);
            Content = "Link to SaveLocker";

            DataContextChanged += (s, e) => Refresh(e.NewValue as Game);
            Click += async (s, e) => await OnClickAsync().ConfigureAwait(true);
        }

        private async void Refresh(Game game)
        {
            var myGeneration = ++generation;
            if (game == null) { Visibility = Visibility.Collapsed; return; }
            Visibility = Visibility.Visible;

            System.Collections.Generic.List<TrackedGameDto> tracked;
            try { tracked = await client.GetGamesAsync().ConfigureAwait(true); }
            catch (Exception ex)
            {
                Logger.Warn(ex, "SaveLocker: couldn't reach the agent to check link status");
                if (myGeneration != generation) return;
                // Fail open toward "let them try" rather than hiding the control on a transient
                // blip — clicking it while the agent is down just fails the same way below.
                ApplyMatchState(false);
                return;
            }
            if (myGeneration != generation) return;
            ApplyMatchState(GameMatcher.FindMatch(game, tracked) != null);
        }

        private void ApplyMatchState(bool linked)
        {
            Content = linked ? "✓ Synced" : "Link to SaveLocker";
            IsEnabled = !linked;
        }

        private async Task OnClickAsync()
        {
            var game = DataContext as Game;
            if (game == null) return;
            var myGeneration = generation;

            IsEnabled = false;
            Content = "Checking…";
            await LinkAction.RunAsync(api, client, game).ConfigureAwait(true);
            if (myGeneration == generation) Refresh(game);
        }
    }
}
