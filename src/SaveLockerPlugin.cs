using System;
using System.Threading.Tasks;
using System.Windows;
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

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            try
            {
                var tracked = FindMatch(args.Game);
                if (tracked == null) return;

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
                        if (!ResolveConflictInteractively(tracked, gate.ConflictId.Value))
                            args.CancelStartup = true;
                        return;
                }
            }
            catch (Exception ex)
            {
                // Fail open, unconditionally — SaveLocker must never be the reason a game won't start.
                Logger.Error(ex, "SaveLocker: OnGameStarting failed, launching anyway");
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                var tracked = FindMatch(args.Game);
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

        /// <summary>
        /// Blocks (this is already inside a confirmed <see cref="LaunchDecision.Blocked"/> — the one
        /// decision allowed to be fail-closed) until the player resolves the conflict or cancels.
        /// Unlike the pre-launch call above, a failure fetching the conflict's own details must NOT
        /// fail open: the agent already told us a genuine divergence exists, so launching over it
        /// would risk the exact overwrite this whole feature exists to prevent.
        /// </summary>
        private bool ResolveConflictInteractively(TrackedGameDto tracked, Guid conflictId)
        {
            try
            {
                var conflict = client.GetConflictAsync(conflictId).GetAwaiter().GetResult();
                var cloudVersion = client.GetVersionAsync(conflict.VersionAId).GetAwaiter().GetResult();
                var cloudStats = client.GetVersionStatsAsync(conflict.VersionAId).GetAwaiter().GetResult();
                var deviceVersion = client.GetVersionAsync(conflict.VersionBId).GetAwaiter().GetResult();
                var deviceStats = client.GetVersionStatsAsync(conflict.VersionBId).GetAwaiter().GetResult();

                // CreateWindow (not `new Window()`) is what makes this follow whatever theme the
                // player has picked — it returns a Window already carrying Playnite's own chrome and
                // StandardWindowStyle, so Background/Foreground resolve from the active theme instead
                // of WPF's plain-white default. A hand-built Window never picks that up (hardware-found
                // 2026-09-15: it rendered as a stray white dialog against a dark Playnite theme).
                var themedWindow = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                });
                themedWindow.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();

                // Fullscreen mode has no equivalent of Desktop's window-chrome/popup theme resources
                // (confirmed against Playnite's own Fullscreen theme source, not assumed) — the Desktop
                // resolve window renders as an unstyled white box there. Routed to a separate,
                // Fullscreen-native overlay instead of trying to make one window serve both.
                bool? result = PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                    ? new ConflictResolveWindowFullscreen(
                        themedWindow, client, tracked.Name, conflictId, cloudVersion, cloudStats, deviceVersion, deviceStats).ShowDialog()
                    : new ConflictResolveWindow(
                        themedWindow, client, tracked.Name, conflictId, cloudVersion, cloudStats, deviceVersion, deviceStats).ShowDialog();
                return result == true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SaveLocker: couldn't load conflict details");
                MessageBox.Show(
                    "SaveLocker found a real save conflict for this game but couldn't load its details (" + ex.Message + ").\n\n" +
                    "Open the SaveLocker agent at " + settingsViewModel.Settings.AgentUrl + " to resolve it, then launch again.",
                    "SaveLocker — save conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private TrackedGameDto FindMatch(Game game)
        {
            try
            {
                var tracked = client.GetGamesAsync().GetAwaiter().GetResult();
                return GameMatcher.FindMatch(game, tracked);
            }
            catch (Exception ex)
            {
                // Agent unreachable/not running — same as an untracked game, no gate at all.
                Logger.Warn(ex, "SaveLocker: couldn't reach the agent to match this game");
                return null;
            }
        }
    }
}
