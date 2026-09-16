using System;
using System.Windows;
using Playnite.SDK;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// The interactive conflict UI, shared by <see cref="SaveLockerPlugin.OnGameStarting"/> (a real,
    /// fail-closed launch block) and <see cref="SyncNowAction"/> (an on-demand sync triggered from the
    /// "Link to SaveLocker" button/menu). Both land here whenever the agent's pre-launch-sync gate
    /// reports <see cref="LaunchDecision.Blocked"/>, and neither may treat that as anything less than
    /// an already-confirmed divergence — extracted from SaveLockerPlugin (was
    /// ResolveConflictInteractively) so there is exactly one copy of that handling instead of a second,
    /// un-hardware-verified one for the sync-now path.
    /// </summary>
    internal static class ConflictResolver
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static bool ResolveInteractively(IPlayniteAPI api, LocalApiClient client, string gameName, Guid conflictId)
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
                var themedWindow = api.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                });
                themedWindow.Owner = api.Dialogs.GetCurrentAppWindow();

                // Fullscreen mode has no equivalent of Desktop's window-chrome/popup theme resources
                // (confirmed against Playnite's own Fullscreen theme source, not assumed) — the Desktop
                // resolve window renders as an unstyled white box there. Routed to a separate,
                // Fullscreen-native overlay instead of trying to make one window serve both.
                bool? result = api.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                    ? new ConflictResolveWindowFullscreen(
                        themedWindow, client, gameName, conflictId, cloudVersion, cloudStats, deviceVersion, deviceStats).ShowDialog()
                    : new ConflictResolveWindow(
                        themedWindow, client, gameName, conflictId, cloudVersion, cloudStats, deviceVersion, deviceStats).ShowDialog();
                return result == true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SaveLocker: couldn't load conflict details");
                MessageBox.Show(
                    "SaveLocker found a real save conflict for this game but couldn't load its details (" + ex.Message + ").\n\n" +
                    "Open the SaveLocker agent at " + client.AgentUrl + " to resolve it, then launch again.",
                    "SaveLocker — save conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
    }
}
