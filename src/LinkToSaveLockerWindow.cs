using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// The "Link to SaveLocker" popup (tasks/playnite-plugin/plan.md, Phase 12/Group 4) — the five-tier
    /// flow: search already-tracked games -&gt; automatic manifest lookup -&gt; manual manifest search
    /// -&gt; manual folder browse -&gt; pick an existing tracked game. Reachable today only from the
    /// Tier-4 "couldn't automatically match" nudge (SaveLockerPlugin.MaybeShowLinkNudge) — a right-click
    /// menu entry point is Phase 13/Group 5, not built here. Same code-only-WPF, theme-via-CreateWindow
    /// convention as ConflictResolveWindow (no XAML, no WebView2 — the plan's own suggestion of embedding
    /// the agent-ui Add Games view for the manual-browse tier was dropped in favor of Playnite's own
    /// native IDialogsFactory.SelectFolder(), which needs no new dependency at all); unlike that window
    /// this one has no separate Fullscreen variant — the plan's design for this phase never asked for
    /// one the way Phase 10's resolve dialog explicitly did.
    /// </summary>
    internal sealed class LinkToSaveLockerWindow
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly Window window;
        private readonly IPlayniteAPI api;
        private readonly LocalApiClient client;
        private readonly Game playniteGame;

        private List<TrackedGameDto> trackedGames;
        // Set by the first automatic lookup (tier 2) and refreshed by every later one (a manifest pick,
        // tier 3) — always valid for tier 4's folder override, since /api/candidates/lookup places a
        // candidate in the cache whether or not it resolved a path.
        private int? pendingCandidateId;
        private bool busy;

        public LinkToSaveLockerWindow(Window window, IPlayniteAPI api, LocalApiClient client, Game playniteGame)
        {
            this.window = window;
            this.api = api;
            this.client = client;
            this.playniteGame = playniteGame;

            window.Title = "SaveLocker — link \"" + playniteGame.Name + "\"";
            window.Width = 480;
            window.SizeToContent = SizeToContent.Height;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ResizeMode = ResizeMode.NoResize;
            window.Topmost = true;

            RenderLoading("Looking for a match…");
            Load();
        }

        public bool? ShowDialog() => window.ShowDialog();

        private async void Load()
        {
            try
            {
                trackedGames = await client.GetGamesAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                RenderError("Couldn't reach the SaveLocker agent: " + ex.Message, Load);
                return;
            }

            // Tier 1: the exact chain OnGameStarting already ran, just on demand — cheap (reuses the
            // list just fetched) and catches the case where another machine's plugin already linked
            // this exact game server-side since the launch that triggered this popup's nudge.
            var match = GameMatcher.FindMatch(playniteGame, trackedGames);
            if (match != null) { RenderConfirmLink(match); return; }

            await RunAutomaticLookupAsync(playniteGame.Name).ConfigureAwait(true);
        }

        private async Task RunAutomaticLookupAsync(string name, bool isManifestPick = false)
        {
            RenderLoading(isManifestPick ? ("Checking \"" + name + "\"…") : "Checking SaveLocker's game database…");
            try
            {
                var isSteam = string.Equals(playniteGame.Source?.Name, "Steam", StringComparison.OrdinalIgnoreCase);
                var steamAppId = isSteam && uint.TryParse(playniteGame.GameId, out _) ? playniteGame.GameId : null;
                var result = await client.CandidatesLookupAsync(
                    name, playniteGame.InstallDirectory, steamAppId, MapStore(playniteGame.Source?.Name)).ConfigureAwait(true);
                pendingCandidateId = result.Id;
                var displayName = result.CandidateName ?? name;

                if (result.Resolved)
                    RenderConfirmEnroll(displayName, result.SuggestedPath, result.Id);
                else if (!isManifestPick)
                    RenderManifestSearch(name);
                else
                    RenderUnresolved(displayName);
            }
            catch (Exception ex)
            {
                // Retryable in place, not just a dead-end Close — the nudge that opened this window
                // only ever fires once per game (NudgeState), so a transient failure here must not be
                // the only chance the player gets at this before Phase 13's menu entry exists.
                RenderError("Couldn't check SaveLocker's game database: " + FriendlyError(ex),
                    async () => await RunAutomaticLookupAsync(name, isManifestPick).ConfigureAwait(true));
            }
        }

        // ---- Screens ----

        private void RenderLoading(string text)
        {
            var root = BuildRoot();
            root.Children.Add(Themed(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }));
            SetContent(root);
        }

        private void RenderError(string message, Action retry)
        {
            var root = BuildRoot();
            root.Children.Add(Themed(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }));

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var close = new Button { Content = MakeLabel("Close"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
            close.Click += (s, e) => { window.DialogResult = false; window.Close(); };
            StyleButton(close);
            footer.Children.Add(close);
            if (retry != null)
            {
                var retryButton = new Button { Content = MakeLabel("Try again"), Padding = new Thickness(12, 5, 12, 5) };
                retryButton.Click += (s, e) => retry();
                StyleButton(retryButton);
                footer.Children.Add(retryButton);
            }
            root.Children.Add(footer);
            SetContent(root);
        }

        private void RenderConfirmLink(TrackedGameDto match)
        {
            var root = BuildRoot();
            root.Children.Add(Themed(new TextBlock
            {
                Text = "SaveLocker is already tracking a game that matches \"" + playniteGame.Name + "\".",
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8),
            }));
            root.Children.Add(Themed(new TextBlock
            {
                Text = "Link this Playnite entry to the tracked game \"" + match.Name + "\"?",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
            }));

            root.Children.Add(BuildConfirmFooter("Link", async () =>
            {
                await client.SetAliasAsync(match.Id, playniteGame.Name).ConfigureAwait(true);
                Finish("SaveLocker: linked \"" + playniteGame.Name + "\" to \"" + match.Name + "\".", match);
            }));
            root.Children.Add(BuildSecondaryLinks(new LinkOption("Pick a different tracked game instead", RenderPickTracked)));
            SetContent(root);
        }

        private void RenderConfirmEnroll(string displayName, string savePath, int candidateId)
        {
            var root = BuildRoot();
            root.Children.Add(Themed(new TextBlock
            {
                Text = "Track \"" + playniteGame.Name + "\" as " + displayName + "?",
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8),
            }));
            root.Children.Add(Themed(new TextBlock
            {
                Text = "Save found at: " + savePath,
                TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 12, Margin = new Thickness(0, 0, 0, 16),
            }));

            root.Children.Add(BuildConfirmFooter("Enroll", async () =>
            {
                var result = await client.EnrollAsync(candidateId).ConfigureAwait(true);
                if (result.Enrolled < 1)
                {
                    RenderError("SaveLocker couldn't track this game — the folder may already be tracked, or excluded for this game.",
                        () => RenderConfirmEnroll(displayName, savePath, candidateId));
                    return;
                }
                await BackfillAliasAsync(displayName).ConfigureAwait(true);
                var games = await client.GetGamesAsync().ConfigureAwait(true);
                var linked = games.FirstOrDefault(g => string.Equals(g.Name, displayName, StringComparison.OrdinalIgnoreCase));
                Finish("SaveLocker: now tracking \"" + displayName + "\".", linked);
            }));

            root.Children.Add(BuildSecondaryLinks(
                new LinkOption("Search a different name", () => RenderManifestSearch(playniteGame.Name)),
                new LinkOption("Browse for the folder myself", () => BrowseManually(displayName)),
                new LinkOption("Pick an existing tracked game instead", RenderPickTracked)));
            SetContent(root);
        }

        private void RenderUnresolved(string displayName)
        {
            var root = BuildRoot();
            root.Children.Add(Themed(new TextBlock
            {
                Text = "SaveLocker knows \"" + displayName + "\" but hasn't found a save folder — this usually " +
                       "means it hasn't been run yet. Launch it once, then try Link to SaveLocker again, or browse " +
                       "manually if you already know the folder.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
            }));
            root.Children.Add(BuildSecondaryLinks(
                new LinkOption("Browse for the folder myself", () => BrowseManually(displayName)),
                new LinkOption("Search a different name", () => RenderManifestSearch(playniteGame.Name)),
                new LinkOption("Pick an existing tracked game instead", RenderPickTracked)));

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var close = new Button { Content = MakeLabel("Close"), Padding = new Thickness(12, 5, 12, 5) };
            close.Click += (s, e) => { window.DialogResult = false; window.Close(); };
            StyleButton(close);
            footer.Children.Add(close);
            root.Children.Add(footer);
            SetContent(root);
        }

        private void RenderManifestSearch(string initialQuery)
        {
            var root = BuildRoot();
            root.Children.Add(Themed(new TextBlock
            {
                Text = "SaveLocker couldn't automatically find \"" + playniteGame.Name + "\" — search its game database by name.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            }));

            var searchRow = new Grid();
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var searchBox = new TextBox { Text = initialQuery, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(searchBox, 0);
            searchRow.Children.Add(searchBox);
            var searchButton = new Button { Content = MakeLabel("Search"), Padding = new Thickness(10, 4, 10, 4) };
            StyleButton(searchButton);
            Grid.SetColumn(searchButton, 1);
            searchRow.Children.Add(searchButton);
            root.Children.Add(searchRow);

            var resultsBox = new ListBox { Margin = new Thickness(0, 10, 0, 10), MaxHeight = 220 };
            root.Children.Add(resultsBox);

            async void RunSearch()
            {
                var query = searchBox.Text?.Trim();
                resultsBox.Items.Clear();
                if (string.IsNullOrWhiteSpace(query)) return;
                try
                {
                    var results = await client.ManifestSearchAsync(query).ConfigureAwait(true);
                    foreach (var name in results.Take(100)) resultsBox.Items.Add(name);
                    if (results.Count == 0) resultsBox.Items.Add("(no matches)");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "SaveLocker: manifest search failed");
                    resultsBox.Items.Add("(search failed: " + FriendlyError(ex) + ")");
                }
            }

            searchButton.Click += (s, e) => RunSearch();
            searchBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) RunSearch(); };
            resultsBox.MouseDoubleClick += async (s, e) =>
            {
                var chosen = resultsBox.SelectedItem as string;
                if (string.IsNullOrEmpty(chosen) || chosen.StartsWith("(")) return;
                await RunAutomaticLookupAsync(chosen, isManifestPick: true).ConfigureAwait(true);
            };

            root.Children.Add(BuildSecondaryLinks(
                new LinkOption("Browse for the folder myself", () => BrowseManually(playniteGame.Name)),
                new LinkOption("Pick an existing tracked game instead", RenderPickTracked)));

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var cancel = new Button { Content = MakeLabel("Cancel"), Padding = new Thickness(12, 5, 12, 5) };
            cancel.Click += (s, e) => { window.DialogResult = false; window.Close(); };
            StyleButton(cancel);
            footer.Children.Add(cancel);
            root.Children.Add(footer);

            SetContent(root);
            RunSearch();
        }

        private sealed class TrackedGamePickItem
        {
            public TrackedGameDto Game;
            public override string ToString() =>
                string.IsNullOrEmpty(Game.Alias) || string.Equals(Game.Alias, Game.Name, StringComparison.OrdinalIgnoreCase)
                    ? Game.Name
                    : Game.Name + " (alias: " + Game.Alias + ")";
        }

        private void RenderPickTracked()
        {
            var root = BuildRoot();
            root.Children.Add(Themed(new TextBlock
            {
                Text = "Pick an already-tracked SaveLocker game to link \"" + playniteGame.Name + "\" to.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            }));

            var filterBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(filterBox);

            var list = new ListBox { MaxHeight = 220, Margin = new Thickness(0, 0, 0, 6) };
            root.Children.Add(list);

            root.Children.Add(Themed(new TextBlock
            {
                Text = "Double-click a game to link it.", Opacity = 0.65, FontSize = 11, Margin = new Thickness(0, 0, 0, 10),
            }));

            void Refresh()
            {
                list.Items.Clear();
                var filter = filterBox.Text?.Trim() ?? "";
                var items = (trackedGames ?? new List<TrackedGameDto>())
                    .Where(t => string.IsNullOrEmpty(filter) ||
                                (t.Name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                                (t.Alias?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    .OrderBy(t => t.Name)
                    .ToList();
                foreach (var t in items) list.Items.Add(new TrackedGamePickItem { Game = t });
                if (items.Count == 0) list.Items.Add("(no tracked games match)");
            }
            filterBox.TextChanged += (s, e) => Refresh();
            Refresh();

            list.MouseDoubleClick += (s, e) =>
            {
                if (list.SelectedItem is TrackedGamePickItem picked) RenderConfirmLink(picked.Game);
            };

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = MakeLabel("Cancel"), Padding = new Thickness(12, 5, 12, 5) };
            cancel.Click += (s, e) => { window.DialogResult = false; window.Close(); };
            StyleButton(cancel);
            footer.Children.Add(cancel);
            root.Children.Add(footer);

            SetContent(root);
        }

        // ---- Actions ----

        private void BrowseManually(string displayName)
        {
            if (!pendingCandidateId.HasValue)
            {
                RenderError("Nothing to attach a folder to yet — try searching again.", () => RenderManifestSearch(playniteGame.Name));
                return;
            }

            string picked;
            try
            {
                picked = !string.IsNullOrWhiteSpace(playniteGame.InstallDirectory)
                    ? api.Dialogs.SelectFolder(playniteGame.InstallDirectory)
                    : api.Dialogs.SelectFolder();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "SaveLocker: folder browse failed");
                return;
            }
            if (string.IsNullOrEmpty(picked)) return; // cancelled — stay on the current screen

            SetFolderAndConfirm(pendingCandidateId.Value, picked, displayName);
        }

        private async void SetFolderAndConfirm(int candidateId, string path, string displayName)
        {
            RenderLoading("Checking that folder…");
            try
            {
                await client.SetCandidateFolderAsync(candidateId, path).ConfigureAwait(true);
                RenderConfirmEnroll(displayName, path, candidateId);
            }
            catch (Exception ex)
            {
                RenderError(FriendlyError(ex), () => BrowseManually(displayName));
            }
        }

        private async Task BackfillAliasAsync(string candidateName)
        {
            // Only needed when the enrolled name differs from Playnite's own title — e.g. a manifest
            // pick ("Sid Meier's Civilization VII" for Playnite's "Civ VII"). Without this, tier 3's
            // name/Alias matching would never recognise this game on a future launch, silently undoing
            // the whole point of linking it.
            if (string.Equals(candidateName, playniteGame.Name, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var games = await client.GetGamesAsync().ConfigureAwait(true);
                var created = games.FirstOrDefault(g => string.Equals(g.Name, candidateName, StringComparison.OrdinalIgnoreCase));
                if (created != null)
                    await client.SetAliasAsync(created.Id, playniteGame.Name).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "SaveLocker: couldn't backfill alias after enroll");
            }
        }

        // `linked` is the TrackedGameDto the player just linked to, so this can offer to sync it right
        // away — same "linked, sync now?" dialog LinkAction shows after an automatic link, asked for
        // so the manual tiers (search/browse/pick) end the same way as the automatic one instead of a
        // toast that's easy to miss.
        private void Finish(string message, TrackedGameDto linked)
        {
            window.DialogResult = true;
            window.Close();

            if (linked == null)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    api.Notifications.Add(new NotificationMessage(
                        "savelocker-link-done-" + playniteGame.Id, message, NotificationType.Info));
                }
                return;
            }

            LinkedTag.Ensure(api, playniteGame);

            var choice = api.Dialogs.ShowMessage(
                message + "\n\nSync now to pull the latest save?",
                "SaveLocker — linked", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (choice == MessageBoxResult.Yes) _ = SyncNowAction.RunAsync(api, client, linked); // intentionally not awaited
        }

        // ---- Layout helpers (duplicated from ConflictResolveWindow rather than shared — this
        // codebase's own established convention, see ConflictResolveWindowFullscreen). ----

        private static StackPanel BuildRoot() => new StackPanel { Margin = new Thickness(16) };

        private void SetContent(UIElement content)
        {
            busy = false;
            window.Content = content;
        }

        private StackPanel BuildConfirmFooter(string actionLabel, Func<Task> onConfirm)
        {
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            var cancel = new Button { Content = MakeLabel("Cancel"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
            // Guarded by `busy`, same reasoning as ConflictResolveWindow's own cancel guard: an action
            // already in flight must finish before the window can close out from under its continuation.
            cancel.Click += (s, e) => { if (busy) return; window.DialogResult = false; window.Close(); };
            StyleButton(cancel);
            footer.Children.Add(cancel);
            footer.Children.Add(BuildActionButton(actionLabel, onConfirm));
            return footer;
        }

        private Button BuildActionButton(string label, Func<Task> onClick)
        {
            var labelBlock = MakeLabel(label);
            var button = new Button { Content = labelBlock, Padding = new Thickness(12, 5, 12, 5) };
            StyleButton(button);
            button.Click += async (s, e) =>
            {
                if (busy) return;
                busy = true;
                button.IsEnabled = false;
                labelBlock.Text = "Working…";
                try
                {
                    await onClick().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "SaveLocker: link action failed");
                    if (window.IsVisible)
                        RenderError("Something went wrong: " + FriendlyError(ex), null);
                }
                finally
                {
                    busy = false;
                }
            };
            return button;
        }

        // Plain (string, Action) pairs rather than C# 7 named tuples deliberately — this project
        // targets net462 without a System.ValueTuple reference (docs/Gotchas.md's own stance on
        // avoiding an extra NuGet dependency just to pack), and nothing else in this codebase uses
        // tuple syntax either.
        private sealed class LinkOption
        {
            public readonly string Text;
            public readonly Action OnClick;
            public LinkOption(string text, Action onClick) { Text = text; OnClick = onClick; }
        }

        private StackPanel BuildSecondaryLinks(params LinkOption[] links)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var link in links)
            {
                var action = link.OnClick;
                var button = new Button
                {
                    Content = MakeLabel(link.Text),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(0, 2, 0, 2),
                    Margin = new Thickness(0, 2, 0, 2),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                };
                button.Click += (s, e) => { if (!busy) action(); };
                panel.Children.Add(button);
            }
            return panel;
        }

        private static TextBlock Themed(TextBlock block)
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            return block;
        }

        private static TextBlock MakeLabel(string text) => Themed(new TextBlock { Text = text });

        private static readonly ControlTemplate ButtonTemplate = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                  xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                  TargetType='Button'>
  <Border x:Name='Bd' Background='{TemplateBinding Background}'
          BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'
          CornerRadius='4' SnapsToDevicePixels='True'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'
                       Margin='{TemplateBinding Padding}' RecognizesAccessKey='True'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='Bd' Property='Opacity' Value='0.85'/>
    </Trigger>
    <Trigger Property='IsPressed' Value='True'>
      <Setter TargetName='Bd' Property='Opacity' Value='0.7'/>
    </Trigger>
    <Trigger Property='IsEnabled' Value='False'>
      <Setter TargetName='Bd' Property='Opacity' Value='0.4'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>");

        private static void StyleButton(Button button)
        {
            button.Template = ButtonTemplate;
            button.SetResourceReference(Control.BackgroundProperty, "PopupBackgroundBrush");
            button.SetResourceReference(Control.BorderBrushProperty, "PopupBorderBrush");
            button.BorderThickness = new Thickness(1);
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

        // The agent's error responses are a plain {"error":"..."} JSON body, but LocalApiClient throws
        // that raw text wrapped in an HttpRequestException — pulling the field back out here surfaces
        // the actual SavePathGuard/enroll refusal reason instead of a raw status-code-and-JSON string
        // (tasks/playnite-plugin/plan.md, "Link to SaveLocker" problem 4: "the popup needs to surface
        // that specific failure reason too, not just silently drop back to nothing found").
        private static string FriendlyError(Exception ex)
        {
            var msg = ex.Message;
            var idx = msg.IndexOf('{');
            if (idx < 0) return msg;
            try
            {
                var obj = Json.AsObject(Json.Parse(msg.Substring(idx)));
                var err = Json.GetString(obj, "error");
                return string.IsNullOrEmpty(err) ? msg : err;
            }
            catch { return msg; }
        }
    }
}
