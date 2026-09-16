using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// The always-visible status control for a game's details view
    /// (<c>GenericPlugin.GetGameViewControl</c>, tasks/playnite-plugin/plan.md Phase 13's "status chip
    /// + action buttons"): a compact chip reading Not linked / Agent offline / Not synced yet /
    /// In sync / Conflict, plus one action button whose label and effect follow the chip. Successor to
    /// Group 4's <c>LinkStatusButton</c> (link-only, binary state) — the button-based alternative to
    /// the passive Tier-4 nudge (<see cref="SaveLockerPlugin.MaybeShowLinkNudge"/>), broadened here
    /// into a real status surface once a game is already linked.
    ///
    /// <b>One deliberate deviation from plan.md, worth knowing:</b> the plan describes three
    /// independent buttons — Push now, Pull now, Resolve conflict. The agent's local API
    /// (SaveLocker/src/Agent.Core/AgentApiServer.cs) has no per-game push-only or pull-only route to
    /// call — only <c>pre-launch-sync</c> (push-then-pull-or-block, what <see cref="SyncNowAction"/>
    /// already wraps as "Sync now") and <c>post-exit-sync</c> (push only, fired automatically on
    /// <c>OnGameStopped</c>, not something a button should re-trigger on demand). Adding separate
    /// push/pull routes is an agent-side change outside this group's scope
    /// (`implementation-grouping.md`'s Group 5 is plugin-side only), so this control offers one
    /// "Sync now" button instead — it already does both halves in the right order — plus a distinct
    /// "Resolve conflict" button that appears only once a conflict is confirmed, using the conflict id
    /// <c>sync-status</c> already returns rather than re-running a sync to discover it again.
    ///
    /// <b>Theme-dependent — confirmed NOT rendered by the Harmony theme</b>, same caveat
    /// <c>LinkStatusButton</c> documented: Harmony's DetailsViewGameOverview.xaml wires a fixed,
    /// hand-picked list of plugin buttons that does not include SaveLocker, so Playnite never calls
    /// this class's constructor under it at all. <see cref="SaveLockerPlugin.GetGameMenuItems"/> (the
    /// right-click menu) is the theme-independent equivalent and should be treated as the primary
    /// surface until every theme this project cares about is actually checked.
    /// </summary>
    internal sealed class GameStatusControl : ContentControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IPlayniteAPI api;
        private readonly LocalApiClient client;
        private readonly Border chip;
        private readonly TextBlock chipText;
        private readonly Button actionButton;

        // Bumped on every DataContext change so a slow lookup for a game the player has since
        // navigated away from can't overwrite the control's state for whichever game is showing now —
        // the same guard LinkStatusButton used.
        private int generation;
        private Game currentGame;
        private TrackedGameDto currentTracked;
        private Guid? currentConflictId;

        // ContentControl, not StackPanel directly: GetGameViewControl's return type is
        // System.Windows.Controls.Control, which Panel (StackPanel's own base) does not derive from —
        // the panel below is this control's Content instead of the control itself.
        public GameStatusControl(IPlayniteAPI api, LocalApiClient client)
        {
            this.api = api;
            this.client = client;

            // Right-aligned within whatever container the active theme gives this control — a plugin
            // has no way to place it at a fixed corner of the whole application window, only within
            // the region the theme's own layout defines for GetGameViewControl.
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(4);

            chipText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            chip = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = chipText,
            };
            actionButton = new Button { Padding = new Thickness(10, 4, 10, 4) };
            actionButton.Click += async (s, e) => await OnActionClickAsync().ConfigureAwait(true);

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(chip);
            panel.Children.Add(actionButton);
            Content = panel;

            DataContextChanged += (s, e) => Refresh(e.NewValue as Game);
        }

        private async void Refresh(Game game)
        {
            var myGeneration = ++generation;
            currentGame = game;
            if (game == null) { Visibility = Visibility.Collapsed; return; }
            Visibility = Visibility.Visible;
            SetChip("Checking…", Brushes.Gray);
            actionButton.Visibility = Visibility.Collapsed;

            System.Collections.Generic.List<TrackedGameDto> tracked;
            try { tracked = await client.GetGamesAsync().ConfigureAwait(true); }
            catch (Exception ex)
            {
                Logger.Warn(ex, "SaveLocker: couldn't reach the agent to check status");
                if (myGeneration != generation) return;
                ApplyOffline();
                return;
            }
            if (myGeneration != generation) return;

            var match = GameMatcher.FindMatch(game, tracked);
            if (match == null) { ApplyNotLinked(); return; }

            currentTracked = match;
            SyncStatusDto status;
            try { status = await client.GetSyncStatusAsync(match.Id).ConfigureAwait(true); }
            catch (Exception ex)
            {
                Logger.Warn(ex, "SaveLocker: couldn't reach the agent to check sync status");
                if (myGeneration != generation) return;
                ApplyOffline();
                return;
            }
            if (myGeneration != generation) return;

            if (status.HasOpenConflict) ApplyConflict(status.ConflictId);
            else if (status.InSync) ApplyInSync();
            else ApplyNotSyncedYet();
        }

        private void ApplyOffline()
        {
            SetChip("Agent offline", Brushes.Gray);
            actionButton.Content = "Retry";
            actionButton.Visibility = Visibility.Visible;
        }

        private void ApplyNotLinked()
        {
            currentTracked = null;
            SetChip("Not linked", Brushes.Gray);
            actionButton.Content = "Link to SaveLocker";
            actionButton.Visibility = Visibility.Visible;
        }

        private void ApplyConflict(Guid? conflictId)
        {
            currentConflictId = conflictId;
            SetChip("Conflict", Brushes.IndianRed);
            actionButton.Content = "Resolve conflict";
            actionButton.Visibility = Visibility.Visible;
        }

        private void ApplyInSync()
        {
            SetChip("In sync", Brushes.MediumSeaGreen);
            actionButton.Content = "Sync now";
            actionButton.Visibility = Visibility.Visible;
        }

        private void ApplyNotSyncedYet()
        {
            SetChip("Not synced yet", Brushes.DarkGoldenrod);
            actionButton.Content = "Sync now";
            actionButton.Visibility = Visibility.Visible;
        }

        private void SetChip(string text, Brush background)
        {
            chipText.Text = text;
            chip.Background = background;
        }

        private async Task OnActionClickAsync()
        {
            var game = currentGame;
            if (game == null) return;
            var myGeneration = generation;

            actionButton.IsEnabled = false;
            try
            {
                if (currentTracked == null)
                {
                    // Covers both "Not linked" and "Agent offline" (Retry) — LinkAction itself
                    // re-checks the agent and falls back to the manual picker if nothing auto-resolves,
                    // so retrying a transient offline blip and linking fresh share one code path.
                    await LinkAction.RunAsync(api, client, game).ConfigureAwait(true);
                }
                else if (currentConflictId.HasValue)
                {
                    await ConflictResolver.ResolveInteractivelyAsync(api, client, currentTracked.Name, currentConflictId.Value).ConfigureAwait(true);
                }
                else
                {
                    await SyncNowAction.RunAsync(api, client, currentTracked).ConfigureAwait(true);
                }
            }
            finally
            {
                if (myGeneration == generation)
                {
                    actionButton.IsEnabled = true;
                    Refresh(game);
                }
            }
        }
    }
}
