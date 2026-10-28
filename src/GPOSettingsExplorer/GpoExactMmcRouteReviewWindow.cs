using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>
/// An explicit, in-memory, one-time operator-reviewed association. This is
/// NOT a scanner, a persisted mapping, a setting value comparison, or RSoP.
/// Each actual opening re-validates the exact path and native row in a NEW MMC.
/// </summary>
public sealed class GpoExactMmcRouteReviewWindow : Window
{
    private readonly DataGrid _grid = new()
    {
        IsReadOnly = true,
        AutoGenerateColumns = false,
        SelectionMode = DataGridSelectionMode.Single,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        CanUserAddRows = false,
        EnableRowVirtualization = true,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly CheckBox _operatorConfirmed = new()
    {
        Content = new TextBlock
        {
            Text = "I independently checked that this GPMC source entry corresponds " +
                "to the selected native MMC row and exact section (not just a similar name).",
            TextWrapping = TextWrapping.Wrap
        },
        Margin = new Thickness(4, 8, 4, 8)
    };
    private readonly TextBlock _matchStatus = new()
    {
        Foreground = UiStyle.WarningBrush,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly ICollectionView _view;
    private readonly IReadOnlyList<MmcInventoryEntry> _rows;
    private readonly TextBox _search = new() { MinWidth = 220 };
    private readonly Button _open = new()
    {
        Content = "Review and open exact row...",
        IsEnabled = false,
        MinWidth = 180,
        Padding = new Thickness(12, 7, 12, 7)
    };

    public MmcInventoryEntry? SelectedEntry { get; private set; }

    public GpoExactMmcRouteReviewWindow(
        PolicySettingInfo source, IReadOnlyList<MmcInventoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entries);

        Title = "Review exact MMC route (one-time)";
        MinWidth = 750;
        MinHeight = 500;
        Width = 1060;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        UiStyle.ApplyWindowDefaults(this);

        _rows = entries;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        _grid.ItemsSource = _view;

        var container = new DockPanel { Margin = new Thickness(12) };
        Content = container;
        var heading = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(heading, Dock.Top);
        container.Children.Add(heading);

        heading.Children.Add(new TextBlock
        {
            Text = "GPMC setting: " + source.SettingName,
            TextWrapping = TextWrapping.Wrap,
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold
        });
        heading.Children.Add(new TextBlock
        {
            Text = "GPO: " + source.GpoName + " | Scope: " + source.Scope +
                " | Extension: " + source.Extension,
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(new TextBlock
        {
            Text = "No automatic source-to-row mapping was proven. Choose the EXACT " +
                "native policy row only if you recognize its GPMC identity. " +
                "A confirmed route never means a verified stored value or an applied policy. " +
                "No GPO is written by this action.",
            Foreground = UiStyle.WarningBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 5)
        });
        heading.Children.Add(new TextBlock
        {
            Text = "GPMC source value/metadata: " + source.Value,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 75
        });

        var searchPanel = new DockPanel { Margin = new Thickness(0, 7, 0, 7) };
        heading.Children.Add(searchPanel);
        var searchLabel = new TextBlock
        {
            Text = "Filter native MMC rows:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchPanel.Children.Add(searchLabel);
        _search.TextChanged += (_, _) => _view.Refresh();
        searchPanel.Children.Add(_search);

        var footer = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        container.Children.Add(footer);
        footer.Children.Add(_matchStatus);
        footer.Children.Add(_operatorConfirmed);

        var actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right
        };
        footer.Children.Add(actions);
        actions.Children.Add(_open);
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 90,
            Padding = new Thickness(12, 7, 12, 7)
        };
        actions.Children.Add(cancel);
        cancel.Click += (_, _) => DialogResult = false;
        _open.Click += (_, _) =>
        {
            if (_operatorConfirmed.IsChecked != true ||
                _grid.SelectedItem is not MmcInventoryEntry row)
                return;
            SelectedEntry = row;
            DialogResult = true;
        };
        _operatorConfirmed.Checked += (_, _) => UpdateSelection();
        _operatorConfirmed.Unchecked += (_, _) => UpdateSelection();
        _grid.SelectionChanged += (_, _) => UpdateSelection();

        void Column(string title, string binding, double weight)
        {
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = title,
                Binding = new Binding(binding),
                Width = new DataGridLength(weight, DataGridLengthUnitType.Star),
                IsReadOnly = true
            });
        }
        Column("Native row / item", nameof(MmcInventoryEntry.SettingName), 2.1);
        Column("MMC scope", nameof(MmcInventoryEntry.Scope), 0.75);
        Column("Exact MMC section", nameof(MmcInventoryEntry.SectionPath), 3);
        Column("Observed MMC state", nameof(MmcInventoryEntry.MmcState), 1);
        Column("Observed MMC value", nameof(MmcInventoryEntry.MmcValue), 1.8);
        UiStyle.ApplyDataGridDefaults(_grid);
        container.Children.Add(_grid);
        UpdateSelection();
    }

    private bool FilterRow(object entry)
    {
        if (entry is not MmcInventoryEntry row)
            return false;
        var query = _search.Text.Trim();
        return query.Length == 0 ||
            row.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateSelection()
    {
        var item = _grid.SelectedItem as MmcInventoryEntry;
        _open.IsEnabled = item is not null && _operatorConfirmed.IsChecked == true;
        _matchStatus.Text = item is null
            ? $"{_view.Cast<object>().Count():N0} observed native rows in this GPO/scope; no row selected."
            : "Selected route: " + string.Join(" > ", item.TreeSegments) +
                " > " + item.SettingName +
                ". A new MMC editor will re-check its exact literal path/row.";
    }
}
