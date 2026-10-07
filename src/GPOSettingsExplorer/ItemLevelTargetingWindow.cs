using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace GPOSettingsExplorer;

public sealed class ItemLevelTargetingWindow : Window
{
    private readonly TreeView _tree;
    private readonly DataGrid _attributesGrid;
    private readonly ComboBox _typeCombo;
    private readonly TextBox _previewBox;
    private TargetingNode _root;
    private TargetingNode? _selected;

    public string FiltersXml { get; private set; }

    public ItemLevelTargetingWindow(
        string filtersXml)
    {
        FiltersXml =
            filtersXml ?? string.Empty;

        Title =
            "Item-level Targeting";

        Width =
            1050;

        Height =
            720;

        MinWidth =
            760;

        MinHeight =
            520;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        _root =
            Parse(
                FiltersXml);

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

        var clear =
            new Button
            {
                Content =
                    "Clear targeting"
            };

        clear.Click +=
            (_, _) =>
            {
                _root =
                    new TargetingNode(
                        "Filters",
                        null);

                RebuildTree();
            };

        var cancel =
            new Button
            {
                Content =
                    "Cancel",
                IsCancel =
                    true
            };

        var save =
            new Button
            {
                Content =
                    "Save",
                IsDefault =
                    true
            };

        save.Click +=
            (_, _) =>
            {
                try
                {
                    var xml =
                        ToElement(
                            _root);

                    if (!xml.Name.LocalName.Equals(
                            "Filters",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "The targeting root must be <Filters>.");
                    }

                    FiltersXml =
                        _root.Children.Count == 0 &&
                        _root.Attributes.Count == 0
                            ? string.Empty
                            : xml.ToString(
                                SaveOptions.DisableFormatting);

                    DialogResult =
                        true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        ex.Message,
                        "Item-level Targeting",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            };

        footer.Children.Add(
            clear);

        footer.Children.Add(
            cancel);

        footer.Children.Add(
            save);

        var toolbar =
            new WrapPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        8)
            };

        DockPanel.SetDock(
            toolbar,
            Dock.Top);

        _typeCombo =
            new ComboBox
            {
                Width =
                    190,
                ItemsSource =
                    new[]
                    {
                        "FilterGroup",
                        "FilterComputer",
                        "FilterUser",
                        "FilterOrgUnit",
                        "FilterSecurityGroup",
                        "FilterIpRange",
                        "FilterOs",
                        "FilterRegistry",
                        "FilterFile",
                        "FilterVariable",
                        "FilterSite",
                        "FilterWmi",
                        "FilterTime",
                        "FilterDate",
                        "FilterLdap"
                    },
                SelectedIndex =
                    0
            };

        toolbar.Children.Add(
            new TextBlock
            {
                Text =
                    "Add:",
                VerticalAlignment =
                    VerticalAlignment.Center
            });

        toolbar.Children.Add(
            _typeCombo);

        var add =
            new Button
            {
                Content =
                    "Add condition"
            };

        add.Click +=
            (_, _) =>
                AddNode();

        var remove =
            new Button
            {
                Content =
                    "Remove"
            };

        remove.Click +=
            (_, _) =>
                RemoveNode();

        var up =
            new Button
            {
                Content =
                    "Move up"
            };

        up.Click +=
            (_, _) =>
                MoveNode(
                    -1);

        var down =
            new Button
            {
                Content =
                    "Move down"
            };

        down.Click +=
            (_, _) =>
                MoveNode(
                    1);

        var addAttribute =
            new Button
            {
                Content =
                    "Add attribute"
            };

        addAttribute.Click +=
            (_, _) =>
            {
                if (_selected is null)
                    return;

                _selected.Attributes.Add(
                    new TargetingAttribute(
                        "name",
                        string.Empty));

                RefreshPreview();
            };

        var removeAttribute =
            new Button
            {
                Content =
                    "Remove attribute"
            };

        removeAttribute.Click +=
            (_, _) =>
            {
                if (_selected is null ||
                    _attributesGrid.SelectedItem is not
                        TargetingAttribute attribute)
                {
                    return;
                }

                _selected.Attributes.Remove(
                    attribute);

                RefreshPreview();
            };

        toolbar.Children.Add(
            add);

        toolbar.Children.Add(
            remove);

        toolbar.Children.Add(
            up);

        toolbar.Children.Add(
            down);

        toolbar.Children.Add(
            addAttribute);

        toolbar.Children.Add(
            removeAttribute);

        var help =
            new TextBlock
            {
                Text =
                    "Visual tree editor for Group Policy Preferences item-level targeting. Existing and unknown filter nodes/attributes are preserved. Select a node to edit its attributes; AND/OR and NOT are represented by the native bool/not attributes.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(
                        4,
                        0,
                        4,
                        8)
            };

        DockPanel.SetDock(
            help,
            Dock.Top);

        var grid =
            new Grid();

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        0.95,
                        GridUnitType.Star)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1.3,
                        GridUnitType.Star)
            });

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        0.65,
                        GridUnitType.Star)
            });

        _tree =
            new TreeView
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        8)
            };

        _tree.SelectedItemChanged +=
            (_, e) =>
            {
                if (e.NewValue is
                    TreeViewItem item &&
                    item.Tag is
                    TargetingNode node)
                {
                    SelectNode(
                        node);
                }
            };

        Grid.SetColumn(
            _tree,
            0);

        Grid.SetRowSpan(
            _tree,
            2);

        grid.Children.Add(
            _tree);

        _attributesGrid =
            new DataGrid
            {
                AutoGenerateColumns =
                    false,
                CanUserAddRows =
                    false,
                CanUserDeleteRows =
                    false,
                Margin =
                    new Thickness(
                        8,
                        0,
                        0,
                        8)
            };

        _attributesGrid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Attribute",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(TargetingAttribute.Name))
                    {
                        Mode =
                            System.Windows.Data.BindingMode.TwoWay,
                        UpdateSourceTrigger =
                            System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                    },
                Width =
                    new DataGridLength(
                        0.8,
                        DataGridLengthUnitType.Star)
            });

        _attributesGrid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Value",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(TargetingAttribute.Value))
                    {
                        Mode =
                            System.Windows.Data.BindingMode.TwoWay,
                        UpdateSourceTrigger =
                            System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                    },
                Width =
                    new DataGridLength(
                        1.4,
                        DataGridLengthUnitType.Star)
            });

        _attributesGrid.CellEditEnding +=
            (_, _) =>
                Dispatcher.BeginInvoke(
                    RefreshPreview);

        Grid.SetColumn(
            _attributesGrid,
            1);

        Grid.SetRow(
            _attributesGrid,
            0);

        grid.Children.Add(
            _attributesGrid);

        _previewBox =
            new TextBox
            {
                IsReadOnly =
                    true,
                AcceptsReturn =
                    true,
                AcceptsTab =
                    true,
                FontFamily =
                    UiStyle.MonospaceFontFamily,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                Margin =
                    new Thickness(
                        8,
                        8,
                        0,
                        0)
            };

        Grid.SetColumn(
            _previewBox,
            1);

        Grid.SetRow(
            _previewBox,
            1);

        grid.Children.Add(
            _previewBox);

        root.Children.Add(
            footer);

        root.Children.Add(
            toolbar);

        root.Children.Add(
            help);

        root.Children.Add(
            grid);

        Content =
            root;

        RebuildTree();
    }

    private void AddNode()
    {
        var name =
            Convert.ToString(
                _typeCombo.SelectedItem)
            ?? "FilterGroup";

        var parent =
            _selected is not null &&
            CanContainChildren(
                _selected.Name)
                ? _selected
                : _selected?.Parent
                  ?? _root;

        if (!CanContainChildren(
                parent.Name))
        {
            parent =
                _root;
        }

        var node =
            new TargetingNode(
                name,
                parent);

        node.Attributes.Add(
            new TargetingAttribute(
                "bool",
                "AND"));

        node.Attributes.Add(
            new TargetingAttribute(
                "not",
                "0"));

        parent.Children.Add(
            node);

        RebuildTree(
            node);
    }

    private void RemoveNode()
    {
        if (_selected is null ||
            ReferenceEquals(
                _selected,
                _root) ||
            _selected.Parent is null)
        {
            return;
        }

        var parent =
            _selected.Parent;

        parent.Children.Remove(
            _selected);

        RebuildTree(
            parent);
    }

    private void MoveNode(
        int delta)
    {
        if (_selected?.Parent is null)
            return;

        var list =
            _selected.Parent.Children;

        var index =
            list.IndexOf(
                _selected);

        var target =
            index +
            delta;

        if (index < 0 ||
            target < 0 ||
            target >=
            list.Count)
        {
            return;
        }

        list.Move(
            index,
            target);

        RebuildTree(
            _selected);
    }

    private void SelectNode(
        TargetingNode node)
    {
        _selected =
            node;

        _attributesGrid.ItemsSource =
            node.Attributes;

        RefreshPreview();
    }

    private void RebuildTree(
        TargetingNode? select = null)
    {
        _tree.Items.Clear();

        var rootItem =
            BuildTreeItem(
                _root);

        _tree.Items.Add(
            rootItem);

        rootItem.IsExpanded =
            true;

        if (select is not null)
        {
            SelectTreeItem(
                rootItem,
                select);
        }
        else
        {
            rootItem.IsSelected =
                true;
        }

        RefreshPreview();
    }

    private static TreeViewItem BuildTreeItem(
        TargetingNode node)
    {
        var item =
            new TreeViewItem
            {
                Header =
                    BuildNodeHeader(
                        node),
                Tag =
                    node,
                IsExpanded =
                    true
            };

        foreach (var child in node.Children)
        {
            item.Items.Add(
                BuildTreeItem(
                    child));
        }

        return item;
    }

    private static bool SelectTreeItem(
        TreeViewItem item,
        TargetingNode node)
    {
        if (ReferenceEquals(
                item.Tag,
                node))
        {
            item.IsSelected =
                true;

            item.BringIntoView();

            return true;
        }

        foreach (var child in item.Items)
        {
            if (child is TreeViewItem treeChild &&
                SelectTreeItem(
                    treeChild,
                    node))
            {
                item.IsExpanded =
                    true;

                return true;
            }
        }

        return false;
    }

    private static string BuildNodeHeader(
        TargetingNode node)
    {
        var summary =
            string.Join(
                ", ",
                node.Attributes
                    .Take(
                        4)
                    .Select(
                        attribute =>
                            $"{attribute.Name}={attribute.Value}"));

        return string.IsNullOrWhiteSpace(
                summary)
            ? node.Name
            : $"{node.Name}  [{summary}]";
    }

    private void RefreshPreview()
    {
        try
        {
            _previewBox.Text =
                ToElement(
                    _root)
                .ToString();
        }
        catch (Exception ex)
        {
            _previewBox.Text =
                ex.Message;
        }
    }

    private static bool CanContainChildren(
        string name) =>
        name.Equals(
            "Filters",
            StringComparison.OrdinalIgnoreCase) ||
        name.Equals(
            "FilterGroup",
            StringComparison.OrdinalIgnoreCase);

    private static TargetingNode Parse(
        string xml)
    {
        if (string.IsNullOrWhiteSpace(
                xml))
        {
            return new TargetingNode(
                "Filters",
                null);
        }

        var root =
            XElement.Parse(
                xml,
                LoadOptions.PreserveWhitespace);

        if (!root.Name.LocalName.Equals(
                "Filters",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Item-level targeting XML must have a <Filters> root element.");
        }

        return FromElement(
            root,
            null);
    }

    private static TargetingNode FromElement(
        XElement element,
        TargetingNode? parent)
    {
        var node =
            new TargetingNode(
                element.Name.LocalName,
                parent);

        foreach (var attribute in element.Attributes())
        {
            node.Attributes.Add(
                new TargetingAttribute(
                    attribute.Name.LocalName,
                    attribute.Value));
        }

        foreach (var child in element.Elements())
        {
            node.Children.Add(
                FromElement(
                    child,
                    node));
        }

        return node;
    }

    private static XElement ToElement(
        TargetingNode node)
    {
        var element =
            new XElement(
                node.Name);

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in node.Attributes)
        {
            var name =
                attribute.Name.Trim();

            if (string.IsNullOrWhiteSpace(
                    name))
            {
                continue;
            }

            if (!seen.Add(
                    name))
            {
                throw new InvalidOperationException(
                    $"Duplicate attribute '{name}' on {node.Name}.");
            }

            element.SetAttributeValue(
                name,
                attribute.Value);
        }

        foreach (var child in node.Children)
        {
            element.Add(
                ToElement(
                    child));
        }

        return element;
    }

    private sealed class TargetingNode
    {
        public TargetingNode(
            string name,
            TargetingNode? parent)
        {
            Name =
                name;

            Parent =
                parent;
        }

        public string Name { get; }

        public TargetingNode? Parent { get; }

        public ObservableCollection<TargetingAttribute> Attributes { get; } =
            new();

        public ObservableCollection<TargetingNode> Children { get; } =
            new();
    }

    public sealed class TargetingAttribute
    {
        public TargetingAttribute(
            string name,
            string value)
        {
            Name =
                name;

            Value =
                value;
        }

        public string Name { get; set; }

        public string Value { get; set; }
    }
}
