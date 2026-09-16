using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// SaveLocker's Windows pre-launch save gate for Playnite (tasks/playnite-plugin/plan.md, Group 3
    /// — Phases 8-11, in the main SaveLocker repo). <c>OnGameStarting</c> is the Windows-side
    /// structural equivalent of the Linux agent's <c>savelocker run -- %command%</c> wrapper: the one
    /// moment on this platform that can be trusted to pull or block *before* a game's process, and
    /// therefore its save file, exists. Everything here either matches a game or calls the agent's
    /// local API and acts on the result — no sync rule is decided in this class.
    /// </summary>
    public sealed class SaveLockerPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly Guid PluginId = new Guid("4d7017e5-87c0-4011-92c4-83f5dde2ada2");

        private readonly SaveLockerSettingsViewModel settingsViewModel;
        private readonly LocalApiClient client;
        private readonly object gateLock = new object();
        private readonly HashSet<Guid> gamesInFlight = new HashSet<Guid>();

        public override Guid Id => PluginId;

        public SaveLockerPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = true };
            settingsViewModel = new SaveLockerSettingsViewModel(this);
            client = new LocalApiClient(() => settingsViewModel.Settings);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settingsViewModel;
        }

        public override UserControl GetSettingsView(bool firstRunView)
        {
            return new SaveLockerSettingsView(settingsViewModel);
        }

        // Asked for directly, after Tier 4's nudge (below) turned out to be too easy to miss: an
        // always-visible control on the game details view rather than something a player only sees
        // after playing an unlinked game once. Ignores args.Name/Mode and always returns the same
        // control — this plugin exposes exactly one view control, so there is nothing to switch on.
        // THEME-DEPENDENT — confirmed not rendered under Harmony, this project's own verification
        // theme; see LinkStatusButton's own doc comment. GetGameMenuItems below is the reliable
        // equivalent every theme supports, since Playnite owns that menu itself.
        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            return new GameStatusControl(PlayniteApi, client);
        }

        // The theme-independent equivalent of GameStatusControl above — Playnite renders its own
        // right-click menu regardless of what the active theme's XAML does or doesn't wire up.
        // Deliberately no synchronous "what's the current state" check to decide which items to show
        // (tasks/playnite-plugin/plan.md Phase 13's "GetGameMenuItems... run every time a player
        // right-clicks anything"): building this list must not block on a network call, so all three
        // items are always present and each reports its own no-op via a toast (LinkAction already
        // does this for "already tracked"; Sync now/Resolve conflict below do the same for "not
        // linked" and "no open conflict").
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "Link to SaveLocker",
                MenuSection = "SaveLocker",
                // Deliberately NOT Task.Run: LinkAction can end up showing a WPF window (the
                // manual picker) when nothing auto-resolves, and WPF windows can only be created on
                // the STA thread that owns the UI — Task.Run hands this to a threadpool thread
                // instead, so ShowPopup's CreateWindow/ShowDialog silently throws and the whole
                // action looks like it did nothing. An async lambda run directly on this (UI) thread
                // keeps every await's continuation on the same thread instead.
                Action = async a =>
                {
                    foreach (var game in a.Games)
                        await LinkAction.RunAsync(PlayniteApi, client, game).ConfigureAwait(true);
                },
            };
            yield return new GameMenuItem
            {
                Description = "Sync now",
                MenuSection = "SaveLocker",
                Action = async a =>
                {
                    foreach (var game in a.Games)
                        await RunSyncNowAsync(game).ConfigureAwait(true);
                },
            };
            yield return new GameMenuItem
            {
                Description = "Resolve conflict…",
                MenuSection = "SaveLocker",
                // Same not-Task.Run reasoning as above — ResolveInteractivelyAsync ends in a WPF
                // window when a conflict is actually found.
                Action = async a =>
                {
                    foreach (var game in a.Games)
                        await RunResolveConflictAsync(game).ConfigureAwait(true);
                },
            };
        }

        // "Sync now" from the right-click menu — a game not yet linked gets a toast pointing at
        // "Link to SaveLocker" instead of silently doing nothing, since this item has no way to hide
        // itself per-game (see GetGameMenuItems' own doc comment on why the list can't check state
        // first).
        private async Task RunSyncNowAsync(Game game)
        {
            var tracked = FindMatch(game, out var agentReachable);
            if (tracked != null) { await SyncNowAction.RunAsync(PlayniteApi, client, tracked).ConfigureAwait(true); return; }

            PlayniteApi.Notifications.Add(new NotificationMessage(
                "savelocker-syncnow-unlinked-" + game.Id,
                agentReachable
                    ? $"SaveLocker: \"{game.Name}\" isn't linked yet — use \"Link to SaveLocker\" first."
                    : "SaveLocker: couldn't reach the agent.",
                NotificationType.Info));
        }

        // "Resolve conflict…" from the right-click menu — reads the game's current sync-status rather
        // than running a fresh pre-launch-sync, since the point of this item is jumping straight to an
        // ALREADY-confirmed conflict without re-triggering a sync cycle (SyncEngine.GetSyncStatusAsync
        // is a cheap, no-download comparison; see LocalApiClient.GetSyncStatusAsync's own doc comment).
        private async Task RunResolveConflictAsync(Game game)
        {
            var tracked = FindMatch(game, out var agentReachable);
            if (tracked == null)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "savelocker-resolve-unlinked-" + game.Id,
                    agentReachable
                        ? $"SaveLocker: \"{game.Name}\" isn't linked yet."
                        : "SaveLocker: couldn't reach the agent.",
                    NotificationType.Info));
                return;
            }

            SyncStatusDto status;
            try { status = await client.GetSyncStatusAsync(tracked.Id).ConfigureAwait(true); }
            catch (Exception ex)
            {
                Logger.Warn(ex, "SaveLocker: couldn't check sync status for Resolve conflict");
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "savelocker-resolve-error-" + game.Id,
                    "SaveLocker: couldn't reach the agent.", NotificationType.Error));
                return;
            }

            if (!status.HasOpenConflict || !status.ConflictId.HasValue)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "savelocker-resolve-none-" + game.Id,
                    $"SaveLocker: \"{tracked.Name}\" has no open conflict.", NotificationType.Info));
                return;
            }

            await ConflictResolver.ResolveInteractivelyAsync(PlayniteApi, client, tracked.Name, status.ConflictId.Value).ConfigureAwait(true);
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            TrackedGameDto tracked = null;
            var ownsGate = false;
            try
            {
                tracked = FindMatch(args.Game, out var agentReachable);
                if (tracked == null)
                {
                    // Only nudge when the agent actually answered with "no match" — an unreachable
                    // agent means this launch is exactly like an untracked game today, nothing to link.
                    if (agentReachable) MaybeShowLinkNudge(args.Game);
                    return;
                }

                // Backfills the tag for anything matched purely automatically (GameMatcher, never
                // through LinkAction/LinkToSaveLockerWindow) — e.g. games that were already tracked
                // before this tag existed, or that always resolved on their own. Ensure() is a no-op
                // once the tag is already there, so this costs nothing on every later launch.
                LinkedTag.Ensure(PlayniteApi, args.Game);

                // Playnite has occasionally been seen to fire OnGameStarting twice for one launch
                // (e.g. certain emulator/launcher setups) — without this, a second concurrent call for
                // the same game would run its own gate check and could open a second conflict-resolve
                // window over the first one. `ownsGate` (not just "was it added") tells the `finally`
                // below whether THIS call is the one that should clear the entry — a losing, re-entrant
                // call must not remove the winning call's still-in-progress guard.
                lock (gateLock) { ownsGate = gamesInFlight.Add(tracked.Id); }
                if (!ownsGate)
                {
                    Logger.Warn($"SaveLocker: OnGameStarting re-entered for '{tracked.Name}' while already in flight, skipping");
                    return;
                }

                var gate = LaunchGateResult.ProceedFallback;
                var options = new GlobalProgressOptions("SaveLocker: checking for a newer save…", false) { IsIndeterminate = true };
                var result = PlayniteApi.Dialogs.ActivateGlobalProgress(async progressArgs =>
                {
                    try
                    {
                        gate = await client.PreLaunchSyncAsync(tracked.Id, progressArgs.CancelToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Fail open, per this route's own documented contract — a transport failure
                        // is not a confirmed conflict.
                        Logger.Warn(ex, "SaveLocker: pre-launch-sync unreachable, launching anyway");
                        gate = LaunchGateResult.ProceedFallback;
                    }
                }, options);

                if (result.Error != null)
                    Logger.Warn(result.Error, "SaveLocker: pre-launch progress dialog errored");

                switch (gate.Decision)
                {
                    case LaunchDecision.Proceed:
                        return;

                    case LaunchDecision.ProceedSyncPaused:
                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            "savelocker-lease-" + tracked.Id,
                            $"SaveLocker: launched without checking for updates — saves are checked out by '{gate.HolderMachineName}'. A conflict may occur if that machine is also playing.",
                            NotificationType.Info));
                        return;

                    case LaunchDecision.Blocked:
                        if (!gate.ConflictId.HasValue) return; // shouldn't happen; nothing to block on
                        if (!ConflictResolver.ResolveInteractively(PlayniteApi, client, tracked.Name, gate.ConflictId.Value))
                            args.CancelStartup = true;
                        return;
                }
            }
            catch (Exception ex)
            {
                // Fail open, unconditionally — SaveLocker must never be the reason a game won't start.
                Logger.Error(ex, "SaveLocker: OnGameStarting failed, launching anyway");
            }
            finally
            {
                if (ownsGate) lock (gateLock) { gamesInFlight.Remove(tracked.Id); }
            }
        }

        // Asked for directly: a game linked before this tag existed (or matched automatically and
        // never once run through LinkAction/LinkToSaveLockerWindow) would otherwise only pick up
        // LinkedTag the next time it happens to launch — this backfills the whole library once at
        // startup instead, so "already-linked" games show the tag without the player having to launch
        // each one first. Runs off the UI thread since nothing here touches WPF, matching
        // OnGameStopped's own Task.Run below; LinkedTag.Ensure is a no-op for anything already tagged,
        // so repeating this on every startup costs nothing once the library has caught up.
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            Task.Run(() =>
            {
                try
                {
                    var tracked = client.GetGamesAsync().GetAwaiter().GetResult();
                    foreach (var game in PlayniteApi.Database.Games)
                    {
                        if (GameMatcher.FindMatch(game, tracked) != null)
                            LinkedTag.Ensure(PlayniteApi, game);
                    }
                }
                catch (Exception ex)
                {
                    // Same fail-open contract as everywhere else here — an unreachable agent at
                    // startup just means this backfill retries on the next application start.
                    Logger.Warn(ex, "SaveLocker: couldn't backfill linked tags on startup");
                }
            });
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                var tracked = FindMatch(args.Game, out _);
                if (tracked == null) return;

                var gameId = tracked.Id;
                Task.Run(() =>
                {
                    try { client.PostExitSyncAsync(gameId).GetAwaiter().GetResult(); }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "SaveLocker: post-exit-sync failed");
                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            "savelocker-exit-" + gameId,
                            "SaveLocker: couldn't reach the agent to upload this save. It will retry on the next sync.",
                            NotificationType.Error));
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SaveLocker: OnGameStopped failed");
            }
        }

        private TrackedGameDto FindMatch(Game game, out bool agentReachable)
        {
            agentReachable = false;
            try
            {
                var tracked = client.GetGamesAsync().GetAwaiter().GetResult();
                agentReachable = true;
                return GameMatcher.FindMatch(game, tracked);
            }
            catch (Exception ex)
            {
                // Agent unreachable/not running — same as an untracked game, no gate at all.
                Logger.Warn(ex, "SaveLocker: couldn't reach the agent to match this game");
                return null;
            }
        }

        /// <summary>
        /// Tier 4 of GameMatcher's priority chain (tasks/playnite-plugin/plan.md, "Automatic game
        /// matching", point 4): a low-friction, dismissible nudge shown once ever per Playnite game
        /// that never automatically matched, with a one-click path into the Phase 12 picker. Never
        /// blocking — the game already launched normally by the time this fires.
        /// </summary>
        private void MaybeShowLinkNudge(Game game)
        {
            try
            {
                var dataDir = GetPluginUserDataPath();
                if (NudgeState.WasShown(dataDir, game.Id)) return;
                NudgeState.MarkShown(dataDir, game.Id);

                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "savelocker-link-nudge-" + game.Id,
                    $"SaveLocker couldn't automatically match '{game.Name}' — click to link it and sync this game.",
                    NotificationType.Info,
                    () => ShowLinkPopup(game)));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "SaveLocker: link nudge failed");
            }
        }

        private void ShowLinkPopup(Game game)
        {
            try
            {
                var themedWindow = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                });
                themedWindow.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                new LinkToSaveLockerWindow(themedWindow, PlayniteApi, client, game).ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SaveLocker: link popup failed to open");
            }
        }
    }
}
