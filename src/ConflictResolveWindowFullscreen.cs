using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Playnite.SDK;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// Fullscreen-mode counterpart to <see cref="ConflictResolveWindow"/> — same data, deliberately
    /// different chrome. Confirmed on hardware (2026-09-15) that the Desktop version's theme keys
    /// (<c>PopupBackgroundBrush</c>/<c>PopupBorderBrush</c>) simply don't exist in Playnite's
    /// Fullscreen theme resource set (<c>Playnite.FullscreenApp/Themes/Fullscreen/Default/Constants.xaml</c>,
    /// checked against the real Playnite source) — a plain <see cref="ConflictResolveWindow"/> shown
    /// there renders as an unstyled white box with no controller-friendly interaction. Fullscreen mode
    /// also merges no window-chrome resource dictionary the way Desktop's <c>StandardWindowStyle.xaml</c>
    /// does, so a normal OS-chrome dialog would look out of place floating over Playnite's own
    /// borderless big-picture UI. This window is instead a borderless, dimmed full-screen overlay with
    /// a centered card, matching the shape of the Decky plugin's own conflict modal (a floating card
    /// over a dimmed backdrop, no title bar) and using Fullscreen's real theme keys —
    /// <c>ControlBackgroundBrush</c>/<c>OverlayMenuBackgroundBrush</c> (card), <c>OverlayBrush</c>
    /// (backdrop dim), <c>GlyphBrush</c> (accent/selection), <c>TextBrush</c> (text, confirmed to exist
    /// under the same key name in both theme sets) — not invented, all read off Playnite's own
    /// Fullscreen theme source.
    /// </summary>
    internal sealed class ConflictResolveWindowFullscreen
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private const string CloudIconData = "M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z";
        private const string HardDriveIconData =
            "M10 16h.01 M2.212 11.577a2 2 0 0 0-.212.896V18a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-5.527a2 2 0 0 0-.212-.896L18.55 5.11A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z M21.946 12.013H2.054 M6 16h.01";

        private readonly Window window;
        private readonly LocalApiClient client;
        private readonly Guid conflictId;
        private readonly SaveVersionDto cloudVersion;
        private readonly SaveVersionDto deviceVersion;
        private Guid? selectedVersionId;
        private Border devicePanel;
        private Border cloudPanel;
        private Button resolveButton;
        private TextBlock resolveButtonLabel;

        public ConflictResolveWindowFullscreen(
            Window window, LocalApiClient client, string gameName, Guid conflictId,
            SaveVersionDto cloudVersion, VersionStatsDto cloudStats,
            SaveVersionDto deviceVersion, VersionStatsDto deviceStats)
        {
            this.window = window;
            this.client = client;
            this.conflictId = conflictId;
            this.cloudVersion = cloudVersion;
            this.deviceVersion = deviceVersion;

            // Chromeless full-screen overlay rather than a floating OS-chrome dialog — Fullscreen mode
            // has no equivalent of Desktop's window-chrome theme resources to draw one from anyway.
            window.Title = "SaveLocker";
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;
            window.ResizeMode = ResizeMode.NoResize;
            window.Topmost = true;
            window.ShowInTaskbar = false;

            // Match the owner (Playnite's own Fullscreen window) exactly rather than WindowState.Maximized
            // — AllowsTransparency + Maximized is known to misbehave across multi-monitor setups.
            if (window.Owner != null)
            {
                window.Left = window.Owner.Left;
                window.Top = window.Owner.Top;
                window.Width = window.Owner.ActualWidth > 0 ? window.Owner.ActualWidth : SystemParameters.PrimaryScreenWidth;
                window.Height = window.Owner.ActualHeight > 0 ? window.Owner.ActualHeight : SystemParameters.PrimaryScreenHeight;
            }
            else
            {
                window.WindowState = WindowState.Maximized;
            }

            var backdrop = new Grid();
            backdrop.SetResourceReference(Panel.BackgroundProperty, "OverlayBrush");

            var card = new StackPanel
            {
                Margin = new Thickness(28),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var cardBorder = new Border
            {
                Child = card,
                Width = 820,
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black },
            };
            cardBorder.SetResourceReference(Border.BackgroundProperty, "OverlayMenuBackgroundBrush");
            card.Margin = new Thickness(32);

            card.Children.Add(Themed(new TextBlock
            {
                Text = gameName + " changed on this device and the cloud since the last sync.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
            }));
            card.Children.Add(Themed(new TextBlock
            {
                Text = "Pick which save to keep. The game will not start until you choose, or cancel.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 20),
            }));

            var panels = new Grid();
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            devicePanel = BuildPanel("This device", "This is the machine you're using right now.", HardDriveIconData, deviceVersion, deviceStats,
                () => Select(deviceVersion.Id));
            Grid.SetColumn(devicePanel, 0);
            panels.Children.Add(devicePanel);

            cloudPanel = BuildPanel("The cloud", "The save every other machine syncs from.", CloudIconData, cloudVersion, cloudStats,
                () => Select(cloudVersion.Id));
            Grid.SetColumn(cloudPanel, 2);
            panels.Children.Add(cloudPanel);

            card.Children.Add(panels);

            card.Children.Add(Themed(new TextBlock
            {
                Text = "← / → to choose · Enter to confirm · Esc to cancel",
                Opacity = 0.55,
                FontSize = 13,
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            }));

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            var cancelButton = new Button { Content = MakeLabel("Cancel", 16), Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 0, 10, 0) };
            cancelButton.Click += (s, e) => Cancel();
            StyleButton(cancelButton);
            resolveButtonLabel = MakeLabel("Resolve", 16);
            resolveButton = new Button { Content = resolveButtonLabel, Padding = new Thickness(18, 9, 18, 9), IsEnabled = false };
            resolveButton.Click += ResolveButton_Click;
            StyleButton(resolveButton, accent: true);
            footer.Children.Add(cancelButton);
            footer.Children.Add(resolveButton);
            card.Children.Add(footer);

            backdrop.Children.Add(cardBorder);
            window.Content = backdrop;

            // Arrow-key/Enter/Escape handling at the window level: this is a real, separate OS window,
            // and whether Playnite's own gamepad-to-input translation (built into the Fullscreen app)
            // reaches a plugin-created secondary window at all is unverified — wiring plain keyboard
            // input here covers a real keyboard and any controller-to-keyboard emulation without
            // depending on WPF's default Tab-focus chain, which cycles ALL focusable elements rather
            // than just toggling device/cloud the way a 10-foot UI modal should. NOT yet hardware-
            // verified with a real controller — flagged for confirmation, not assumed working.
            window.PreviewKeyDown += Window_PreviewKeyDown;
            window.Loaded += (s, e) => devicePanel.Focus();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                    Select(deviceVersion.Id);
                    e.Handled = true;
                    break;
                case Key.Right:
                    Select(cloudVersion.Id);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (resolveButton.IsEnabled) ResolveButton_Click(resolveButton, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Cancel();
                    e.Handled = true;
                    break;
            }
        }

        private void Cancel()
        {
            window.DialogResult = false;
            window.Close();
        }

        private static TextBlock Themed(TextBlock block)
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            return block;
        }

        private static TextBlock MakeLabel(string text, double fontSize = 14) =>
            Themed(new TextBlock { Text = text, FontSize = fontSize });

        private static void StyleButton(Button button, bool accent = false)
        {
            button.BorderThickness = new Thickness(0);
            button.SetResourceReference(Control.BackgroundProperty, accent ? "GlyphBrush" : "ControlBackgroundBrush");
        }

        private static Path BuildIcon(string pathData, double size = 20)
        {
            var path = new Path
            {
                Data = Geometry.Parse(pathData),
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                VerticalAlignment = VerticalAlignment.Center,
            };
            path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return path;
        }

        private Border BuildPanel(string title, string caption, string iconData, SaveVersionDto version, VersionStatsDto stats, Action onSelect)
        {
            var panel = new StackPanel();

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            header.Children.Add(BuildIcon(iconData));
            header.Children.Add(Themed(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }));
            panel.Children.Add(header);

            panel.Children.Add(Themed(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 13, Margin = new Thickness(0, 4, 0, 10) }));
            panel.Children.Add(Themed(new TextBlock { Text = FormatAgo(version.CreatedAt), FontWeight = FontWeights.SemiBold, FontSize = 20 }));
            panel.Children.Add(Themed(new TextBlock { Text = version.CreatedAt.ToLocalTime().ToString("g"), Opacity = 0.55, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) }));
            panel.Children.Add(Themed(new TextBlock { Text = FormatSize(version.Size), FontSize = 14 }));
            if (stats != null)
            {
                panel.Children.Add(Themed(new TextBlock { Text = $"{stats.FileCount} file(s)", FontSize = 14 }));
                if (stats.NewestFileWriteUtc.HasValue)
                    panel.Children.Add(Themed(new TextBlock { Text = "Newest change: " + FormatAgo(stats.NewestFileWriteUtc.Value), FontSize = 14 }));
            }

            var select = new Button { Content = MakeLabel("Keep this", 15), Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Left, Focusable = true };
            select.Click += (s, e) => onSelect();
            StyleButton(select);
            panel.Children.Add(select);

            var border = new Border
            {
                Child = panel,
                Padding = new Thickness(18),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Focusable = true,
            };
            border.SetResourceReference(Border.BackgroundProperty, "ControlBackgroundBrush");
            SetPanelSelected(border, false);
            border.PreviewMouseLeftButtonUp += (s, e) => onSelect();
            return border;
        }

        private void Select(Guid versionId)
        {
            selectedVersionId = versionId;
            resolveButton.IsEnabled = true;
            SetPanelSelected(devicePanel, versionId == deviceVersion.Id);
            SetPanelSelected(cloudPanel, versionId == cloudVersion.Id);
        }

        private static void SetPanelSelected(Border panel, bool selected)
        {
            if (selected)
            {
                panel.SetResourceReference(Border.BorderBrushProperty, "GlyphBrush");
            }
            else
            {
                panel.BorderBrush = Brushes.Transparent;
            }
        }

        public bool? ShowDialog() => window.ShowDialog();

        private async void ResolveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!selectedVersionId.HasValue) return;
            resolveButton.IsEnabled = false;
            resolveButtonLabel.Text = "Resolving…";
            try
            {
                await client.ResolveConflictAsync(conflictId, selectedVersionId.Value, keepBoth: false).ConfigureAwait(true);
                window.DialogResult = true;
                window.Close();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SaveLocker: conflict resolve failed (fullscreen)");
                MessageBox.Show(window, "Couldn't resolve the conflict: " + ex.Message, "SaveLocker", MessageBoxButton.OK, MessageBoxImage.Error);
                resolveButton.IsEnabled = true;
                resolveButtonLabel.Text = "Resolve";
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
