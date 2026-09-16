using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// Built in code rather than XAML/BAML — this project has no Visual Studio WPF designer support
    /// in its toolchain, and hand-built markup would be one more untested thing in the highest-risk
    /// phase of this plugin (docs/Gotchas.md). Deliberately plain: two fields and a status line,
    /// mirroring the three-state shape SaveLocker-Decky's own DeckyPluginCard.tsx already settled on.
    /// </summary>
    internal sealed class SaveLockerSettingsView : UserControl
    {
        public SaveLockerSettingsView(SaveLockerSettingsViewModel viewModel)
        {
            DataContext = viewModel;

            var grid = new Grid { Margin = new Thickness(4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var version = new TextBlock { Opacity = 0.85 };
            version.SetBinding(TextBlock.TextProperty, new Binding("PluginVersionDisplay"));
            AddRow(grid, 0, "Plugin version", version);

            AddRow(grid, 1, "Agent URL", CreateTextBox("AgentUrl"));
            AddRow(grid, 2, "State directory", CreateTextBox("StateDir"));

            var testButton = new Button
            {
                Content = "Test connection",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 8, 0, 4),
            };
            testButton.Click += (s, e) => viewModel.RefreshConnectionStatus();
            Grid.SetRow(testButton, 3);
            Grid.SetColumn(testButton, 1);
            grid.Children.Add(testButton);

            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
            status.SetBinding(TextBlock.TextProperty, new Binding("ConnectionStatus"));
            Grid.SetRow(status, 4);
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);

            Content = grid;

            viewModel.RefreshConnectionStatus();
        }

        private static void AddRow(Grid grid, int row, string label, FrameworkElement field)
        {
            var text = new TextBlock { Text = label, Margin = new Thickness(0, 0, 8, 6), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            field.Margin = new Thickness(0, 0, 0, 6);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
        }

        private static TextBox CreateTextBox(string bindingPath)
        {
            var box = new TextBox();
            box.SetBinding(TextBox.TextProperty, new Binding("Settings." + bindingPath) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            return box;
        }
    }
}
