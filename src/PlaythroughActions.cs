using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// The right-click menu's playthrough actions (SaveLocker tasks/save-playthroughs Phase 9). Like every other action here
    /// this holds no rules: the agent refuses a switch while the game runs or a conflict is open, pushes the outgoing save
    /// to its own playthrough first, and restores the other. What this adds is the explicit dialogs around it — a confirm,
    /// a progress dialog that shows the agent's own steps while it works, and a result dialog that always says how it
    /// ended — not a toast, which is gone before it can be read (the plugin's feedback rule for asynchronous actions).
    /// </summary>
    internal static class PlaythroughActions
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string Title = "SaveLocker — playthrough";

        public static async Task SwitchAsync(
            IPlayniteAPI api, LocalApiClient client, PlaythroughCache cache, TrackedGameDto tracked, string fromName, PlaythroughDto to)
        {
            if (tracked.PlaythroughId == to.Id || (tracked.PlaythroughId == null && to.IsDefault))
            {
                api.Dialogs.ShowMessage("This PC already plays \"" + to.Name + "\" for \"" + tracked.Name + "\".", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (to.HasOpenConflict)
            {
                api.Dialogs.ShowMessage("\"" + to.Name + "\" has an unresolved save conflict. Resolve it first, then switch.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var go = new MessageBoxOption("Switch", true, false);
            var cancel = new MessageBoxOption("Cancel", false, true);
            var choice = api.Dialogs.ShowMessage(
                "Play \"" + to.Name + "\" on this PC?\n\n" +
                (string.IsNullOrEmpty(fromName) ? "" : "\"" + fromName + "\" is uploaded to its own playthrough first, so nothing is lost. ") +
                "Your other devices keep their playthroughs.",
                Title + " — " + tracked.Name, MessageBoxImage.Question, new List<MessageBoxOption> { go, cancel });
            if (choice != go) return;

            var result = RunWithProgress(api, client, "Switching \"" + tracked.Name + "\" to \"" + to.Name + "\"…", () => client.SwitchPlaythroughAsync(tracked.Id, to.Id));
            cache.Forget(tracked.Id);
            Report(api, result, tracked.Name, "Now playing \"" + to.Name + "\"", "Couldn't switch");
            await cache.RefreshAsync().ConfigureAwait(true);
        }

        /// <summary>The menu's fallback while its memory has no list yet: fetch it now, let the player pick, then switch as above.</summary>
        public static async Task PickAndSwitchAsync(IPlayniteAPI api, LocalApiClient client, PlaythroughCache cache, TrackedGameDto tracked)
        {
            GamePlaythroughsDto list = null;
            api.Dialogs.ActivateGlobalProgress(async a =>
            {
                try { list = await client.GetPlaythroughsAsync(tracked.Id, a.CancelToken).ConfigureAwait(false); }
                catch (Exception ex) { Logger.Warn(ex, "SaveLocker: couldn't list playthroughs"); }
            }, new GlobalProgressOptions("SaveLocker: loading playthroughs…", false) { IsIndeterminate = true });

            if (list == null || list.Offline || list.Choices().Count == 0)
            {
                api.Dialogs.ShowMessage("Couldn't list the playthroughs for \"" + tracked.Name + "\". The SaveLocker agent or the server isn't reachable.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var options = list.Choices().Select(p => new GenericItemOption(
                (p.Id == list.SelectedId ? "✓ " : "") + p.Name, p.HeadAt.HasValue ? "Latest " + Ago(p.HeadAt.Value) + " on " + p.HeadMachine : "No save yet") { Name = p.Id.ToString() }).ToList();
            var picked = api.Dialogs.ChooseItemWithSearch(options, q => options.Where(o => string.IsNullOrEmpty(q) || o.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList(), "", "SaveLocker — playthrough for " + tracked.Name);
            if (picked == null) return;
            var to = list.Choices().FirstOrDefault(p => p.Id.ToString() == picked.Name);
            if (to != null) await SwitchAsync(api, client, cache, tracked, list.SelectedName, to).ConfigureAwait(true);
        }

        public static async Task NewAsync(IPlayniteAPI api, LocalApiClient client, PlaythroughCache cache, TrackedGameDto tracked)
        {
            if (api.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                api.Dialogs.ShowMessage("Creating a playthrough needs a keyboard. Do it from Playnite's desktop mode, the SaveLocker app, or the console.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<StartFileDto> files = null;
            try { files = await client.GetStartFilesAsync(tracked.Id).ConfigureAwait(true); }
            catch (Exception ex) { Logger.Warn(ex, "SaveLocker: couldn't list the save files for Start fresh"); }

            var window = api.Dialogs.CreateWindow(new WindowCreationOptions { ShowMinimizeButton = false, ShowMaximizeButton = false });
            window.Owner = api.Dialogs.GetCurrentAppWindow();
            var choice = new NewPlaythroughWindow(window, tracked.Name, tracked.PlaythroughName, files).ShowDialog();
            if (choice == null) return;

            var result = RunWithProgress(api, client, "Creating \"" + choice.Name + "\" for \"" + tracked.Name + "\"…",
                () => client.NewPlaythroughAsync(tracked.Id, choice.Name, choice.StartFresh, choice.KeepFiles, choice.PlayHere));
            cache.Forget(tracked.Id);
            Report(api, result, tracked.Name, "Created \"" + choice.Name + "\"", "Couldn't create the playthrough");
            await cache.RefreshAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Runs <paramref name="call"/> behind Playnite's own progress dialog, whose text follows the agent's activity feed (the
        /// steps of the switch) once a second. A transport failure becomes a failed result with its reason, never an exception.
        /// </summary>
        private static SwitchPlaythroughResult RunWithProgress(IPlayniteAPI api, LocalApiClient client, string heading, Func<Task<SwitchPlaythroughResult>> call)
        {
            SwitchPlaythroughResult result = null;
            api.Dialogs.ActivateGlobalProgress(async a =>
            {
                var startedAt = DateTime.UtcNow;
                a.Text = heading;
                var task = call();
                try
                {
                    while (await Task.WhenAny(task, Task.Delay(1000)).ConfigureAwait(false) != task)
                    {
                        try
                        {
                            var steps = await client.GetActivitySinceAsync(startedAt.AddSeconds(-2)).ConfigureAwait(false);
                            if (steps.Count > 0) a.Text = heading + "\n\n" + string.Join("\n", steps.Skip(Math.Max(0, steps.Count - 4)));
                        }
                        catch (Exception ex) { Logger.Debug(ex, "SaveLocker: activity feed not read"); }
                    }
                    result = await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "SaveLocker: playthrough call failed");
                    result = new SwitchPlaythroughResult { Ok = false, Message = Explain(ex) };
                }
            }, new GlobalProgressOptions(heading, false) { IsIndeterminate = true });
            return result ?? new SwitchPlaythroughResult { Ok = false, Message = "The SaveLocker agent gave no answer." };
        }

        private static void Report(IPlayniteAPI api, SwitchPlaythroughResult r, string game, string okTitle, string failTitle)
        {
            api.Dialogs.ShowMessage(
                r.Message + (r.Ok && r.Offline ? "\n\nThe server wasn't reachable, so this PC used its own copy. Your outgoing save is uploaded when it is back." : ""),
                "SaveLocker — " + (r.Ok ? okTitle : failTitle) + " (" + game + ")",
                MessageBoxButton.OK, r.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        /// <summary>Why a call failed, in words. An agent that predates playthroughs has no such route, which is a 404.</summary>
        internal static string Explain(Exception ex)
        {
            var text = ex.Message ?? "";
            if (text.StartsWith("404")) return "This SaveLocker agent is too old to switch playthroughs. Update it first.";
            if (ex is OperationCanceledException) return "The agent did not answer in time. Check its activity before trying again.";
            if (ex is System.Net.Http.HttpRequestException && text.Length > 0 && !char.IsDigit(text[0])) return "The SaveLocker agent isn't running.";
            return "The agent answered with an error: " + text;
        }

        internal static string Ago(DateTime utc)
        {
            var span = DateTime.UtcNow - utc.ToUniversalTime();
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + " min ago";
            if (span.TotalDays < 2) return (int)span.TotalHours + " h ago";
            return (int)span.TotalDays + " d ago";
        }
    }
}
