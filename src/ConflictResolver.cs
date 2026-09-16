using System;
using System.Threading.Tasks;
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
    ///
    /// Two overloads for two different callers, deliberately NOT unified into one: OnGameStarting
    /// overrides a void SDK method and must decide CancelStartup before returning, so it needs a
    /// method it can call synchronously; SyncNowAction is a genuine on-demand background action and
    /// must NOT freeze the UI thread while conflict details load. A single async method awaited via
    /// GetAwaiter().GetResult() from the UI thread would deadlock the moment its continuation tried
    /// to resume on that same, now-blocked thread — so the synchronous overload stays fully
    /// synchronous (no awaits, no captured SynchronizationContext) rather than being a thin wrapper
    /// around the async one.
    /// </summary>
    internal static class ConflictResolver
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        /// <summary>
        /// Blocking — safe specifically because every fetch call below resolves via
        /// GetAwaiter().GetResult() over LocalApiClient calls that use ConfigureAwait(false)
        /// throughout, so nothing here ever needs to resume on this (or any) captured
        /// SynchronizationContext. Used by OnGameStarting only.
        /// </summary>
        public static bool ResolveInteractively(IPlayniteAPI api, LocalApiClient client, string gameName, Guid conflictId)
        {
            try
            {
                var conflict = client.GetConflictAsync(conflictId).GetAwaiter().GetResult();
                var cloudVersion = client.GetVersionAsync(conflict.VersionAId).GetAwaiter().GetResult();
                var cloudStats = client.GetVersionStatsAsync(conflict.VersionAId).GetAwaiter().GetResult();
                var deviceVersion = client.GetVersionAsync(conflict.VersionBId).GetAwaiter().GetResult();
                var deviceStats = client.GetVersionStatsAsync(conflict.VersionBId).GetAwaiter().GetResult();
                return ShowResolveWindow(api, client, gameName, conflictId, cloudVersion, cloudStats, deviceVersion, deviceStats);
            }
            catch (Exception ex)
            {
                ReportLoadFailure(client, ex);
                return false;
            }
        }

        /// <summary>
        /// Non-blocking — the fetch runs on a background thread (<see cref="Task.Run(Action)"/>), so
        /// the UI thread's Dispatcher keeps pumping messages (the app stays responsive) instead of
        /// freezing for however long four sequential agent calls take, unlike the synchronous overload
        /// above. Only safe when the caller genuinely awaits this rather than blocking on it from the
        /// UI thread — see the class doc comment. Used by SyncNowAction, whose entire point is running
        /// in the background without interrupting the player.
        /// </summary>
        public static async Task<bool> ResolveInteractivelyAsync(IPlayniteAPI api, LocalApiClient client, string gameName, Guid conflictId)
        {
            try
            {
                var fetched = await Task.Run(() =>
                {
                    var conflict = client.GetConflictAsync(conflictId).GetAwaiter().GetResult();
                    return new FetchedDetails
                    {
                        CloudVersion = client.GetVersionAsync(conflict.VersionAId).GetAwaiter().GetResult(),
                        CloudStats = client.GetVersionStatsAsync(conflict.VersionAId).GetAwaiter().GetResult(),
                        DeviceVersion = client.GetVersionAsync(conflict.VersionBId).GetAwaiter().GetResult(),
                        DeviceStats = client.GetVersionStatsAsync(conflict.VersionBId).GetAwaiter().GetResult(),
                    };
                }).ConfigureAwait(true);

                // Back on the UI thread here (this await was genuinely awaited, never blocked on, so
                // the continuation posted to the captured SynchronizationContext runs normally) —
                // CreateWindow/ShowDialog below need the STA UI thread, same reasoning
                // SaveLockerPlugin.GetGameMenuItems documents for not using Task.Run around a WPF
                // window's own creation.
                return ShowResolveWindow(
                    api, client, gameName, conflictId,
                    fetched.CloudVersion, fetched.CloudStats, fetched.DeviceVersion, fetched.DeviceStats);
            }
            catch (Exception ex)
            {
                ReportLoadFailure(client, ex);
                return false;
            }
        }

        // Plain class, not a C# 7 named tuple — this project targets net462 without a
        // System.ValueTuple reference (SaveLocker.Playnite.csproj), same reasoning
        // LinkToSaveLockerWindow's own LinkOption type documents.
        private sealed class FetchedDetails
        {
            public SaveVersionDto CloudVersion;
            public VersionStatsDto CloudStats;
            public SaveVersionDto DeviceVersion;
            public VersionStatsDto DeviceStats;
        }

        private static bool ShowResolveWindow(
            IPlayniteAPI api, LocalApiClient client, string gameName, Guid conflictId,
            SaveVersionDto cloudVersion, VersionStatsDto cloudStats, SaveVersionDto deviceVersion, VersionStatsDto deviceStats)
        {
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

        private static void ReportLoadFailure(LocalApiClient client, Exception ex)
        {
            Logger.Error(ex, "SaveLocker: couldn't load conflict details");
            MessageBox.Show(
                "SaveLocker found a real save conflict for this game but couldn't load its details (" + ex.Message + ").\n\n" +
                "Open the SaveLocker agent at " + client.AgentUrl + " to resolve it, then launch again.",
                "SaveLocker — save conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
