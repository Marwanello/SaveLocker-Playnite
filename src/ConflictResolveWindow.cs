using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using Playnite.SDK;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// The "this device / the cloud" resolve dialog for a genuine, confirmed conflict — the one case
    /// <c>pre-launch-sync</c> is allowed to block a launch for (tasks/playnite-plugin/plan.md, "Step
    /// by step"). VersionB is always this machine's own diverged push and VersionA is always the
    /// cloud head it diverged from — <c>AgentApiServer.cs</c>'s own comment on
    /// <c>/api/conflicts/{id}/resolve</c> states that convention outright, not a guess made here.
    /// Deliberately built from code rather than a XAML window, and a native window rather than a
    /// WebView2 view of the agent-ui conflicts page — one fewer dependency in the highest-risk phase
    /// of this plugin (docs/Gotchas.md). The window itself must come from
    /// <c>PlayniteApi.Dialogs.CreateWindow</c> (passed in, not created here) — that's what carries
    /// Playnite's current theme (chrome, Background, Foreground); a plain <c>new Window()</c> gets
    /// none of it and renders as a stray white dialog no matter what theme the player has picked
    /// (hardware-found 2026-09-15). The two content brushes below (<c>PopupBackgroundBrush</c>,
    /// <c>PopupBorderBrush</c>) are real keys read off Playnite's own Default theme XAML, the same
    /// ones it uses for its own floating/popup surfaces — not invented here.
    /// </summary>
    internal sealed class ConflictResolveWindow
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Lucide's own "cloud" and "hard-drive" glyphs (24x24 viewBox, MIT/ISC-licensed path data
        // copied verbatim from lucide-icons/lucide) — the same icon set agent-ui's ConflictCard.tsx
        // already uses for these two sides (Cloud / HardDrive from 'lucide-react'), so the native
        // window matches the web one exactly instead of improvising a different pair of glyphs.
        private const string CloudIconData = "M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z";
        private const string HardDriveIconData =
            "M10 16h.01 M2.212 11.577a2 2 0 0 0-.212.896V18a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-5.527a2 2 0 0 0-.212-.896L18.55 5.11A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z M21.946 12.013H2.054 M6 16h.01";

        private readonly Window window;
        private readonly LocalApiClient client;
        private readonly Guid conflictId;
        private readonly SaveVersionDto cloudVersion;
        private readonly SaveVersionDto deviceVersion;
        private Guid? selectedVersionId;
        private Border cloudPanel;
        private Border devicePanel;
        private Button cancelButton;
        private Button resolveButton;
        private TextBlock resolveButtonLabel;
        private bool resolving;

        public ConflictResolveWindow(
            Window window, LocalApiClient client, string gameName, Guid conflictId,
            SaveVersionDto cloudVersion, VersionStatsDto cloudStats,
            SaveVersionDto deviceVersion, VersionStatsDto deviceStats)
        {
            this.window = window;
            this.client = client;
            this.conflictId = conflictId;
            this.cloudVersion = cloudVersion;
            this.deviceVersion = deviceVersion;

            window.Title = "SaveLocker — save conflict";
            window.Width = 560;
            window.SizeToContent = SizeToContent.Height;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ResizeMode = ResizeMode.NoResize;
            window.Topmost = true;

            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(Themed(new TextBlock
            {
                Text = gameName + " changed on this device and the cloud since the last sync.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
                FontWeight = FontWeights.SemiBold,
            }));
            root.Children.Add(Themed(new TextBlock
            {
                Text = "Pick which save to keep. The game will not start until you choose, or cancel.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, 12),
            }));

            var panels = new Grid();
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            devicePanel = BuildPanel("This device", "This is the machine you're using right now.", HardDriveIconData, deviceVersion, deviceStats,
                () => Select(deviceVersion.Id));
            Grid.SetColumn(devicePanel, 0);
            panels.Children.Add(devicePanel);

            cloudPanel = BuildPanel("The cloud", "The save every other machine syncs from.", CloudIconData, cloudVersion, cloudStats,
                () => Select(cloudVersion.Id));
            Grid.SetColumn(cloudPanel, 2);
            panels.Children.Add(cloudPanel);

            root.Children.Add(panels);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            cancelButton = new Button { Content = MakeLabel("Cancel"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
            // Guarded by `resolving`: a resolve call already in flight must finish (or fail) before
            // the window can close, otherwise the continuation below runs against a closed window —
            // Window.DialogResult throws once the window is gone, and the resulting catch block's
            // MessageBox.Show(window, ...) throws a second time from the same closed owner, unhandled
            // inside this async void handler (confirmed 2026-09-15 review).
            cancelButton.Click += (s, e) => { if (resolving) return; window.DialogResult = false; window.Close(); };
            StyleButton(cancelButton);
            resolveButtonLabel = MakeLabel("Resolve");
            resolveButton = new Button { Content = resolveButtonLabel, Padding = new Thickness(12, 5, 12, 5), IsEnabled = false };
            resolveButton.Click += ResolveButton_Click;
            StyleButton(resolveButton);
            footer.Children.Add(cancelButton);
            footer.Children.Add(resolveButton);
            root.Children.Add(footer);

            window.Content = root;
        }

        // Every plain TextBlock in this window goes through Themed(...) rather than trusting
        // Foreground inheritance from the Window — confirmed, by reading Playnite's own Default-theme
        // template (Themes/Desktop/Default/DerivedStyles/StandardWindowStyle.xaml, which Harmony falls
        // back to since it defines no window style of its own), that the WindowBase style there sets
        // Background and BorderBrush but never Foreground. A Window's Foreground therefore never
        // becomes the theme's TextBrush at all; it stays at WPF's own Control default (near-black),
        // which is exactly what a first pass here wrongly assumed would "correctly inherit" — bold/
        // large text partially hid it, but the smaller stat lines (size, file count, newest change)
        // and the intro sentence made it visible enough for the user to report it as unreadable
        // (screenshot 2026-09-15). The button-label fix below predates this and independently reached
        // the same conclusion for Button/ContentPresenter's own broken inheritance path.
        private static TextBlock Themed(TextBlock block)
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            return block;
        }

        // A first attempt set Button.Foreground and relied on the ContentPresenter's
        // TextElement.Foreground='{TemplateBinding Foreground}' to carry it to the rendered text —
        // confirmed NOT to reach it (screenshot 2026-09-15, after a full rebuild + fresh reinstall +
        // ruling out a stale/unmatched agent as the cause: text was still black). Rather than keep
        // guessing why that particular binding doesn't take effect against the active theme (Harmony,
        // per this portable install's config.json — a third-party theme, not the "Default" one this
        // was first checked against), the button's actual text is now a TextBlock built by MakeLabel
        // with Foreground assigned directly on it, not routed through Content/ContentPresenter
        // inheritance at all. Background/BorderBrush still use the theme's own PopupBackgroundBrush/
        // PopupBorderBrush (confirmed working — the card panels below use the same pair and render
        // correctly), so this is still theme-driven, not a hardcoded color; only the Foreground path
        // changed, from "trust inheritance" to "set it directly on the element that renders it."
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

        private static TextBlock MakeLabel(string text) => Themed(new TextBlock { Text = text });

        // Lucide renders with stroke, not fill (currentColor stroke, 2px, round caps/joins) — this
        // mirrors that exactly rather than filling the shape, which is what the raw path data assumes.
        // Stretch=Uniform scales the 24x24 source geometry down to `size` while keeping it square.
        private static Path BuildIcon(string pathData, double size = 15)
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

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            header.Children.Add(BuildIcon(iconData));
            header.Children.Add(Themed(new TextBlock { Text = title, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) }));
            panel.Children.Add(header);

            panel.Children.Add(Themed(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 4, 0, 8) }));
            panel.Children.Add(Themed(new TextBlock { Text = FormatAgo(version.CreatedAt), FontWeight = FontWeights.SemiBold, FontSize = 16 }));
            panel.Children.Add(Themed(new TextBlock { Text = version.CreatedAt.ToLocalTime().ToString("g"), Opacity = 0.6, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) }));
            panel.Children.Add(Themed(new TextBlock { Text = FormatSize(version.Size), FontSize = 12 }));
            if (stats != null)
            {
                panel.Children.Add(Themed(new TextBlock { Text = $"{stats.FileCount} file(s)", FontSize = 12 }));
                if (stats.NewestFileWriteUtc.HasValue)
                    panel.Children.Add(Themed(new TextBlock { Text = "Newest change: " + FormatAgo(stats.NewestFileWriteUtc.Value), FontSize = 12 }));
            }

            var select = new Button { Content = MakeLabel("Keep this"), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left };
            select.Click += (s, e) => onSelect();
            StyleButton(select);
            panel.Children.Add(select);

            // PopupBackgroundBrush / PopupBorderBrush are Playnite's own keys for a floating surface
            // over the window body (its context menus use the same pair) — Border has no built-in
            // theming of its own the way Button does, so it needs an explicit resource reference to
            // pick up the active theme instead of just rendering transparent.
            var border = new Border
            {
                Child = panel,
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
            };
            border.SetResourceReference(Border.BackgroundProperty, "PopupBackgroundBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
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
                panel.BorderBrush = Brushes.DodgerBlue;
                panel.BorderThickness = new Thickness(2);
            }
            else
            {
                panel.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
                panel.BorderThickness = new Thickness(1);
            }
        }

        public bool? ShowDialog() => window.ShowDialog();

        private async void ResolveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!selectedVersionId.HasValue || resolving) return;
            resolving = true;
            resolveButton.IsEnabled = false;
            cancelButton.IsEnabled = false;
            resolveButtonLabel.Text = "Resolving…";
            try
            {
                await client.ResolveConflictAsync(conflictId, selectedVersionId.Value, keepBoth: false).ConfigureAwait(true);
                window.DialogResult = true;
                window.Close();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SaveLocker: conflict resolve failed");
                // Cancel is disabled while resolving, but the window's own system close button isn't —
                // if the user still got it closed out from under us, touching it further would only
                // throw a second, unhandled exception (see the cancelButton handler above).
                if (window.IsVisible)
                {
                    MessageBox.Show(window, "Couldn't resolve the conflict: " + ex.Message, "SaveLocker", MessageBoxButton.OK, MessageBoxImage.Error);
                    resolveButton.IsEnabled = true;
                    cancelButton.IsEnabled = true;
                    resolveButtonLabel.Text = "Resolve";
                }
            }
            finally
            {
                resolving = false;
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
