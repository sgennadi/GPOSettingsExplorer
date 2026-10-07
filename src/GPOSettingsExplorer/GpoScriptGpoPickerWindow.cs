using System.Windows;
using System.Windows.Controls;

namespace GPOSettingsExplorer;

public sealed record GpoScriptGpoChoice(
    Guid Id,
    string Name,
    int ScriptCount)
{
    public string DisplayText =>
        $"{Name} ({ScriptCount:N0} file(s))";
}

public sealed class GpoScriptGpoPickerWindow : Window
{
    private readonly ListBox _list;

    public IReadOnlyList<Guid> SelectedGpoIds =>
        _list
            .SelectedItems
            .OfType<GpoScriptGpoChoice>()
            .Select(item =>
                item.Id)
            .ToArray();

    public GpoScriptGpoPickerWindow(
        IReadOnlyList<GpoScriptGpoChoice> choices,
        IEnumerable<Guid> selectedIds)
    {
        Title =
            "Select GPOs for Script Search";

        Width =
            760;

        Height =
            560;

        MinWidth =
            560;

        MinHeight =
            380;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(12)
            };

        var footer =
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        DockPanel.SetDock(
            footer,
            Dock.Bottom);

        var selectAll =
            new Button
            {
                Content =
                    "Select all"
            };

        selectAll.Click += (_, _) =>
        {
            _list.SelectAll();
        };

        var clear =
            new Button
            {
                Content =
                    "Clear"
            };

        clear.Click += (_, _) =>
        {
            _list.UnselectAll();
        };

        var apply =
            new Button
            {
                Content =
                    "Use selected",
                IsDefault =
                    true
            };

        apply.Click += (_, _) =>
        {
            DialogResult =
                true;
        };

        footer.Children.Add(
            selectAll);

        footer.Children.Add(
            clear);

        footer.Children.Add(
            apply);

        footer.Children.Add(
            new Button
            {
                Content =
                    "Cancel",
                IsCancel =
                    true
            });

        var header =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        8)
            };

        DockPanel.SetDock(
            header,
            Dock.Top);

        header.Children.Add(
            new TextBlock
            {
                Text =
                    "Select one or more GPOs",
                FontSize =
                    UiStyle.HeadingFontSize,
                FontWeight =
                    FontWeights.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            });

        header.Children.Add(
            new TextBlock
            {
                Text =
                    "Ctrl/Shift selects multiple policies. If files are also selected in GPO Scripts, search is limited to the intersection.",
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        0)
            });

        _list =
            new ListBox
            {
                ItemsSource =
                    choices,
                DisplayMemberPath =
                    nameof(GpoScriptGpoChoice.DisplayText),
                SelectionMode =
                    SelectionMode.Extended
            };

        var selected =
            selectedIds.ToHashSet();

        foreach (var choice in choices)
        {
            if (selected.Contains(
                    choice.Id))
            {
                _list.SelectedItems.Add(
                    choice);
            }
        }

        root.Children.Add(
            footer);

        root.Children.Add(
            header);

        root.Children.Add(
            _list);

        Content =
            root;
    }
}
