using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>
/// Shows every matching line for ONE physical script in ONE GPO. The
/// caller navigates only to this file/line; identical scripts in other GPOs
/// are never silently selected.
/// </summary>
public sealed class GpoScriptSearchMatchesWindow : Window
{
    private readonly DataGrid _matchesGrid = new()
    {
        IsReadOnly = true,
        CanUserAddRows = false,
        AutoGenerateColumns = false,
        SelectionMode = DataGridSelectionMode.Single,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    public int? SelectedLineNumber { get; private set; }

    public GpoScriptSearchMatchesWindow(GpoScriptSearchDisplayRow selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        Title = "Script search matches - " + selected.FileName;
        Width = 1020;
        Height = 610;
        MinWidth = 690;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        UiStyle.ApplyWindowDefaults(this);

        var root = new DockPanel { Margin = new Thickness(10) };
        Content = root;

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        heading.Children.Add(new TextBlock
        {
            Text = selected.GpoHeader,
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(new TextBlock
        {
            Text = $"File: {selected.FileName}  |  Scope: {selected.Scope}  |  " +
                $"Matches: {selected.MatchesLabel}",
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(new TextBlock
        {
            Text = selected.FilePath,
            Foreground = UiStyle.MutedBrush,
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Double-click a line or select it and choose Open at line. " +
                "Only this physical file in this GPO will open.",
            Foreground = UiStyle.WarningBrush,
            TextWrapping = TextWrapping.Wrap
        });

        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var open = new Button
        {
            Content = "Open at selected line",
            MinWidth = 160,
            Style = (Style)FindResource("UiPrimaryButton")
        };
        var cancel = new Button
        {
            Content = "Close", IsCancel = true,
            MinWidth = 80
        };
        open.Click += (_, _) => SelectMatch();
        cancel.Click += (_, _) => DialogResult = false;
        footer.Children.Add(open);
        footer.Children.Add(cancel);

        _matchesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Line", Binding = new Binding(nameof(GpoScriptSearchOccurrence.LineDisplay)),
            Width = new DataGridLength(0.65, DataGridLengthUnitType.Star)
        });
        _matchesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Match type",
            Binding = new Binding(nameof(GpoScriptSearchOccurrence.MatchType)),
            Width = new DataGridLength(1.1, DataGridLengthUnitType.Star)
        });
        _matchesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Matching text / metadata",
            Binding = new Binding(nameof(GpoScriptSearchOccurrence.Text)),
            Width = new DataGridLength(5, DataGridLengthUnitType.Star)
        });
        UiStyle.ApplyDataGridDefaults(_matchesGrid);
        _matchesGrid.ItemsSource = selected.Occurrences;
        _matchesGrid.SelectedItem = selected.Occurrences.FirstOrDefault(item =>
            item.LineNumber == selected.LineNumber) ?? selected.Occurrences.FirstOrDefault();
        _matchesGrid.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject origin &&
                !IsWithinRow(origin))
                return;
            SelectMatch();
        };
        _matchesGrid.SelectionChanged += (_, _) =>
            open.IsEnabled = _matchesGrid.SelectedItem is GpoScriptSearchOccurrence;

        root.Children.Add(_matchesGrid);
    }

    private bool IsWithinRow(DependencyObject element)
    {
        for (var current = element; current is not null;
             current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is DataGridRow row)
                return ReferenceEquals(
                    row.Item, _matchesGrid.SelectedItem);
            if (ReferenceEquals(current, _matchesGrid))
                break;
        }
        return false;
    }

    private void SelectMatch()
    {
        if (_matchesGrid.SelectedItem is not GpoScriptSearchOccurrence match)
            return;
        SelectedLineNumber = match.LineNumber;
        DialogResult = true;
    }
}
