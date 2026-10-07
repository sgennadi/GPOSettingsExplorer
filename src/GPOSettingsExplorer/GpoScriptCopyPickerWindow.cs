using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GpoScriptCopyPickerWindow : Window
{
    private readonly DataGrid _grid;

    public GpoScriptInfo? SelectedScript =>
        _grid.SelectedItem as GpoScriptInfo;

    public GpoScriptCopyPickerWindow(
        IReadOnlyList<GpoScriptInfo> copies)
    {
        Title = "Choose GPO Script Copy";
        Width = 920;
        Height = 520;
        MinWidth = 640;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root =
            new DockPanel
            {
                Margin = new Thickness(12)
            };

        var footer =
            new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right
            };

        DockPanel.SetDock(
            footer,
            Dock.Bottom);

        var open =
            new Button
            {
                Content = "Edit selected",
                IsDefault = true
            };

        open.Click += (_, _) =>
        {
            if (SelectedScript is not null)
            {
                DialogResult = true;
            }
        };

        footer.Children.Add(open);
        footer.Children.Add(
            new Button
            {
                Content = "Cancel",
                IsCancel = true
            });

        var header =
            new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

        DockPanel.SetDock(
            header,
            Dock.Top);

        header.Children.Add(
            new TextBlock
            {
                Text = "This same script content exists in more than one GPO.",
                FontSize = UiStyle.HeadingFontSize,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

        header.Children.Add(
            new TextBlock
            {
                Text = "Choose the physical copy to edit. Search results stay deduplicated.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

        _grid =
            new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                ItemsSource = copies
            };

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "GPO",
                Binding = new System.Windows.Data.Binding(nameof(GpoScriptInfo.GpoName)),
                Width = new DataGridLength(1.4, DataGridLengthUnitType.Star)
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Scope",
                Binding = new System.Windows.Data.Binding(nameof(GpoScriptInfo.Scope)),
                Width = 90
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Event",
                Binding = new System.Windows.Data.Binding(nameof(GpoScriptInfo.EventName)),
                Width = 100
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "File",
                Binding = new System.Windows.Data.Binding(nameof(GpoScriptInfo.FileName)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Path",
                Binding = new System.Windows.Data.Binding(nameof(GpoScriptInfo.FullPath)),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });

        _grid.MouseDoubleClick += (_, _) =>
        {
            if (SelectedScript is not null)
            {
                DialogResult = true;
            }
        };

        if (copies.Count > 0)
        {
            _grid.SelectedIndex = 0;
        }

        root.Children.Add(footer);
        root.Children.Add(header);
        root.Children.Add(_grid);

        Content = root;
    }
}
