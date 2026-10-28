using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SaveLocker.Playnite
{
    /// <summary>What the New playthrough window asked for; null from <see cref="NewPlaythroughWindow.ShowDialog"/> means cancelled.</summary>
    internal sealed class NewPlaythroughChoice
    {
        public string Name;
        public bool StartFresh;
        public List<string> KeepFiles = new List<string>();
        public bool PlayHere;
    }

    /// <summary>
    /// "New playthrough…": a name, and a start — <i>Current save</i> (a copy of this PC's playthrough's latest save, the default) or
    /// <i>Start fresh</i> (an empty save folder, keeping the files ticked: a profile or settings file is ticked by default, saves are
    /// not) — and "Play it on this PC now". The window comes from <c>Dialogs.CreateWindow</c> for the theme, and its controls use the
    /// same theme-resource brushes as <see cref="ConflictResolveWindow"/>. Desktop mode only; Fullscreen has no input form to put here.
    /// </summary>
    internal sealed class NewPlaythroughWindow
    {
        private readonly Window window;
        private readonly TextBox nameBox;
        private readonly RadioButton fresh;
        private readonly StackPanel keepPanel;
        private readonly CheckBox playHere;
        private readonly Button create;
        private readonly List<KeyValuePair<CheckBox, string>> keepBoxes = new List<KeyValuePair<CheckBox, string>>();
        private NewPlaythroughChoice result;

        public NewPlaythroughWindow(Window window, string gameName, string currentName, IList<StartFileDto> files)
        {
            this.window = window;
            window.Title = "SaveLocker — new playthrough for \"" + gameName + "\"";
            window.Width = 520;
            window.SizeToContent = SizeToContent.Height;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ResizeMode = ResizeMode.NoResize;
            window.Topmost = true;

            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(Small("Name", new Thickness(0, 0, 0, 4)));
            // SaveLocker/src/Shared/Contracts.cs's Playthroughs.MaxNameLength: the server refuses a longer name.
            nameBox = new TextBox { MaxLength = 60, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 12) };
            nameBox.TextChanged += (s, e) => create.IsEnabled = nameBox.Text.Trim().Length > 0;
            root.Children.Add(nameBox);

            var current = new RadioButton { GroupName = "start", IsChecked = true, Margin = new Thickness(0, 0, 0, 6), Content = Describe("Current save", "A copy of " + (currentName ?? "this playthrough") + "'s latest save.") };
            fresh = new RadioButton { GroupName = "start", Margin = new Thickness(0, 0, 0, 6), Content = Describe("Start fresh", "An empty save folder, so the game starts a new game. Keep:") };
            root.Children.Add(current);
            root.Children.Add(fresh);

            keepPanel = new StackPanel { Margin = new Thickness(24, 0, 0, 6), Visibility = Visibility.Collapsed };
            if (files == null || files.Count == 0)
                keepPanel.Children.Add(Small("No save files found on this PC to keep.", new Thickness(0)));
            else
                foreach (var f in files)
                {
                    var box = new CheckBox { IsChecked = f.KeepByDefault, Margin = new Thickness(0, 2, 0, 2), Content = ConflictResolveWindow.MakeLabel(f.Path) };
                    keepBoxes.Add(new KeyValuePair<CheckBox, string>(box, f.Path));
                    keepPanel.Children.Add(box);
                }
            root.Children.Add(keepPanel);
            fresh.Checked += (s, e) => keepPanel.Visibility = Visibility.Visible;
            fresh.Unchecked += (s, e) => keepPanel.Visibility = Visibility.Collapsed;

            playHere = new CheckBox
            {
                IsChecked = true,
                Margin = new Thickness(0, 10, 0, 0),
                Content = ConflictResolveWindow.MakeLabel("Play it on this PC now (uploads " + (currentName ?? "the current playthrough") + " first)"),
            };
            root.Children.Add(playHere);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = ConflictResolveWindow.MakeLabel("Cancel"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            cancel.Click += (s, e) => { window.DialogResult = false; window.Close(); };
            ConflictResolveWindow.StyleButton(cancel);
            create = new Button { Content = ConflictResolveWindow.MakeLabel("Create"), Padding = new Thickness(12, 5, 12, 5), IsEnabled = false, IsDefault = true };
            create.Click += Create_Click;
            ConflictResolveWindow.StyleButton(create);
            footer.Children.Add(cancel);
            footer.Children.Add(create);
            root.Children.Add(footer);

            window.Content = root;
        }

        public NewPlaythroughChoice ShowDialog()
        {
            nameBox.Focus();
            window.ShowDialog();
            return result;
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            var startFresh = fresh.IsChecked == true;
            result = new NewPlaythroughChoice
            {
                Name = nameBox.Text.Trim(),
                StartFresh = startFresh,
                KeepFiles = startFresh ? keepBoxes.Where(k => k.Key.IsChecked == true).Select(k => k.Value).ToList() : new List<string>(),
                PlayHere = playHere.IsChecked == true,
            };
            window.DialogResult = true;
            window.Close();
        }

        private static TextBlock Small(string text, Thickness margin)
        {
            var t = ConflictResolveWindow.MakeLabel(text);
            t.Opacity = 0.8;
            t.FontSize = 12;
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = margin;
            return t;
        }

        private static StackPanel Describe(string title, string detail)
        {
            var p = new StackPanel();
            var h = ConflictResolveWindow.MakeLabel(title);
            h.FontWeight = FontWeights.SemiBold;
            p.Children.Add(h);
            p.Children.Add(Small(detail, new Thickness(0)));
            return p;
        }
    }
}
