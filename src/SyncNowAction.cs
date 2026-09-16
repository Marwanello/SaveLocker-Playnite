using System;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// The "Sync now" a player can opt into right after linking a game (LinkAction's own done-dialog,
    /// LinkToSaveLockerWindow.Finish), rather than waiting for the next real launch. Runs the same
    /// pre-launch-sync gate SaveLockerPlugin.OnGameStarting runs — a toast while it checks, not a modal,
    /// since the player already dismissed one dialog to get here — and hands a genuine Blocked
    /// decision to the same ConflictResolver used there; this is a manual trigger for that gate, not a
    /// separate, weaker check. Both callers fire this without awaiting it, so every path here handles
    /// its own errors rather than leaving them for a caller that isn't watching.
    /// </summary>
    internal static class SyncNowAction
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static async Task RunAsync(IPlayniteAPI api, LocalApiClient client, TrackedGameDto tracked)
        {
            var progressId = "savelocker-syncing-" + tracked.Id;
            try
            {
                api.Notifications.Add(new NotificationMessage(
                    progressId, "SaveLocker: syncing \"" + tracked.Name + "\"…", NotificationType.Info));

                LaunchGateResult gate;
                try
                {
                    gate = await client.PreLaunchSyncAsync(tracked.Id, CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "SaveLocker: sync-now failed, leaving the local save as-is");
                    gate = LaunchGateResult.ProceedFallback;
                }
                finally
                {
                    api.Notifications.Remove(progressId);
                }

                switch (gate.Decision)
                {
                    case LaunchDecision.Proceed:
                        api.Dialogs.ShowMessage(
                            "SaveLocker: \"" + tracked.Name + "\" is up to date.",
                            "SaveLocker — synced", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        return;

                    case LaunchDecision.ProceedSyncPaused:
                        api.Dialogs.ShowMessage(
                            "SaveLocker: couldn't check for updates right now — saves are checked out by '" + gate.HolderMachineName + "'.",
                            "SaveLocker — sync skipped", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        return;

                    case LaunchDecision.Blocked:
                        if (!gate.ConflictId.HasValue) return; // shouldn't happen; nothing to block on
                        // The async overload, not ResolveInteractively — this runs from a plain click,
                        // not a launch block, so the whole point (per this class's own doc comment) is
                        // that it must not freeze the app while conflict details load. A second toast
                        // covers the gap the sync-progress one above already closed: it was removed in
                        // the `finally` before this switch runs, so without this the player would see
                        // nothing at all between it disappearing and the resolve window appearing.
                        var conflictProgressId = "savelocker-conflict-" + tracked.Id;
                        api.Notifications.Add(new NotificationMessage(
                            conflictProgressId, "SaveLocker: loading save details for \"" + tracked.Name + "\"…", NotificationType.Info));
                        try
                        {
                            await ConflictResolver.ResolveInteractivelyAsync(api, client, tracked.Name, gate.ConflictId.Value).ConfigureAwait(true);
                        }
                        finally
                        {
                            api.Notifications.Remove(conflictProgressId);
                        }
                        return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SaveLocker: sync-now failed unexpectedly");
            }
        }
    }
}
