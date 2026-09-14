using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// The "this device / the cloud" resolve dialog for a genuine, confirmed conflict — the one case
    /// <c>pre-launch-sync</c> is allowed to block a launch for (tasks/playnite-plugin/plan.md, "Step
    /// by step"). VersionB is always this machine's own diverged push and VersionA is always the
    /// cloud head it diverged from — <c>AgentApiServer.cs</c>'s own comment on
    /// <c>/api/conflicts/{id}/resolve</c> states that convention outright, not a guess made here.
    /// Deliberately a native WPF window rather than a WebView2 view of the agent-ui conflicts page —
    /// one fewer dependency in the highest-risk phase of this plugin (docs/Gotchas.md).
    /// </summary>
    internal sealed class ConflictResolveWindow : Window
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly LocalApiClient client;
        private readonly Guid conflictId;
        private readonly SaveVersionDto cloudVersion;
        private readonly SaveVersionDto deviceVersion;
        private Guid? selectedVersionId;
        private Border cloudPanel;
        private Border devicePanel;
        private Button resolveButton;

        public ConflictResolveWindow(
            LocalApiClient client, string gameName, Guid conflictId,
            SaveVersionDto cloudVersion, VersionStatsDto cloudStats,
            SaveVersionDto deviceVersion, VersionStatsDto deviceStats)
        {
            this.client = client;
            this.conflictId = conflictId;
            this.cloudVersion = cloudVersion;
            this.deviceVersion = deviceVersion;

            Title = "SaveLocker — save conflict";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;

            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(new TextBlock
            {
                Text = gameName + " changed on this device and the cloud since the last sync.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
                FontWeight = FontWeights.SemiBold,
            });
            root.Children.Add(new TextBlock
            {
                Text = "Pick which save to keep. The game will not start until you choose, or cancel.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, 12),
            });

            var panels = new Grid();
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            devicePanel = BuildPanel("This device", "This is the machine you're using right now.", deviceVersion, deviceStats,
                () => Select(deviceVersion.Id));
            Grid.SetColumn(devicePanel, 0);
            panels.Children.Add(devicePanel);

            cloudPanel = BuildPanel("The cloud", "The save every other machine syncs from.", cloudVersion, cloudStats,
                () => Select(cloudVersion.Id));
            Grid.SetColumn(cloudPanel, 2);
            panels.Children.Add(cloudPanel);

            root.Children.Add(panels);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancelButton = new Button { Content = "Cancel", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            resolveButton = new Button { Content = "Resolve", Padding = new Thickness(12, 5, 12, 5), IsEnabled = false };
            resolveButton.Click += ResolveButton_Click;
            footer.Children.Add(cancelButton);
            footer.Children.Add(resolveButton);
            root.Children.Add(footer);

            Content = root;
        }

        private Border BuildPanel(string title, string caption, SaveVersionDto version, VersionStatsDto stats, Action onSelect)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
            panel.Children.Add(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(new TextBlock { Text = FormatAgo(version.CreatedAt), FontWeight = FontWeights.SemiBold, FontSize = 16 });
            panel.Children.Add(new TextBlock { Text = version.CreatedAt.ToLocalTime().ToString("g"), Opacity = 0.6, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            panel.Children.Add(new TextBlock { Text = FormatSize(version.Size), FontSize = 12 });
            if (stats != null)
            {
                panel.Children.Add(new TextBlock { Text = $"{stats.FileCount} file(s)", FontSize = 12 });
                if (stats.NewestFileWriteUtc.HasValue)
                    panel.Children.Add(new TextBlock { Text = "Newest change: " + FormatAgo(stats.NewestFileWriteUtc.Value), FontSize = 12 });
            }

            var select = new Button { Content = "Keep this", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left };
            select.Click += (s, e) => onSelect();
            panel.Children.Add(select);

            var border = new Border
            {
                Child = panel,
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                CornerRadius = new CornerRadius(4),
            };
            return border;
        }

        private void Select(Guid versionId)
        {
            selectedVersionId = versionId;
            resolveButton.IsEnabled = true;
            devicePanel.BorderBrush = versionId == deviceVersion.Id ? Brushes.DodgerBlue : Brushes.Gray;
            devicePanel.BorderThickness = new Thickness(versionId == deviceVersion.Id ? 2 : 1);
            cloudPanel.BorderBrush = versionId == cloudVersion.Id ? Brushes.DodgerBlue : Brushes.Gray;
            cloudPanel.BorderThickness = new Thickness(versionId == cloudVersion.Id ? 2 : 1);
        }

        private async void ResolveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!selectedVersionId.HasValue) return;
            resolveButton.IsEnabled = false;
            resolveButton.Content = "Resolving…";
            try
            {
                await client.ResolveConflictAsync(conflictId, selectedVersionId.Value, keepBoth: false).ConfigureAwait(true);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SaveLocker: conflict resolve failed");
                MessageBox.Show(this, "Couldn't resolve the conflict: " + ex.Message, "SaveLocker", MessageBoxButton.OK, MessageBoxImage.Error);
                resolveButton.IsEnabled = true;
                resolveButton.Content = "Resolve";
            }
        }

        private static string FormatSize(long bytes)
        {
            double b = bytes;
            string[] units = { "B", "KB", "MB", "GB" };
            var i = 0;
            while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
            return b.ToString("0.#") + " " + units[i];
        }

        private static string FormatAgo(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
            return $"{(int)span.TotalDays}d ago";
        }
    }
}
