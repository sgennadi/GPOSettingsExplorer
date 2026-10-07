using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class StructuredGppEditorWindow : Window
{
    private readonly GppDocumentInfo _document;
    private readonly TreeView _tree;
    private readonly DataGrid _attributes;
    private readonly TextBox _elementNameBox;
    private readonly TextBox _textValueBox;
    private readonly TextBox _newChildNameBox;
    private readonly TextBox _previewBox;

    private XmlNodeModel _root;
    private XmlNodeModel? _selected;

    public string Xml { get; private set; }

    public StructuredGppEditorWindow(
        GppDocumentInfo document,
        string xml)
    {
        _document =
            document;

        Xml =
            xml;

        _root =
            FromElement(
                XDocument.Parse(
                    xml,
                    LoadOptions.PreserveWhitespace)
                .Root
                ?? throw new InvalidOperationException(
                    "The XML document has no root element."),
                null);

        Title =
            $"Structured GPP Editor - {document.PreferenceType}";

        Width =
            1120;

        Height =
            760;

        MinWidth =
            800;

        MinHeight =
            540;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(12)
            };

        var header =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        10)
            };

        DockPanel.SetDock(
            header,
            Dock.Top);

        header.Children.Add(
            new TextBlock
            {
                Text =
                    $"{document.GpoName} | {document.Scope} | {document.PreferenceType}",
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
                    "Structured tree/attribute editor. Unknown elements, attributes, and XML namespaces are preserved. Use the raw XML editor for comments, mixed-content documents, or advanced manual repair.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        0)
            });

        var footer =
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        DockPanel.SetDock(
            footer,
            Dock.Bottom);

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
                    CommitSelectedEditors();

                    Xml =
                        new XDocument(
                            new XDeclaration(
                                "1.0",
                                "utf-8",
                                null),
                            ToElement(
                                _root))
                        .ToString();

                    DialogResult =
                        true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        ex.Message,
                        "Structured GPP Editor",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            };

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

        toolbar.Children.Add(
            new TextBlock
            {
                Text =
                    "New child:",
                VerticalAlignment =
                    VerticalAlignment.Center
            });

        _newChildNameBox =
            new TextBox
            {
                Width =
                    180,
                Text =
                    "Item"
            };

        toolbar.Children.Add(
            _newChildNameBox);

        var addChild =
            new Button
            {
                Content =
                    "Add child"
            };

        addChild.Click +=
            (_, _) =>
                AddChild();

        var remove =
            new Button
            {
                Content =
                    "Remove element"
            };

        remove.Click +=
            (_, _) =>
                RemoveSelected();

        var up =
            new Button
            {
                Content =
                    "Move up"
            };

        up.Click +=
            (_, _) =>
                MoveSelected(
                    -1);

        var down =
            new Button
            {
                Content =
                    "Move down"
            };

        down.Click +=
            (_, _) =>
                MoveSelected(
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
                    new XmlAttributeModel(
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
                    _attributes.SelectedItem is not
                        XmlAttributeModel attribute)
                {
                    return;
                }

                _selected.Attributes.Remove(
                    attribute);

                RefreshPreview();
            };

        toolbar.Children.Add(
            addChild);

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

        var grid =
            new Grid();

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        0.9,
                        GridUnitType.Star)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1.35,
                        GridUnitType.Star)
            });

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        0.75,
                        GridUnitType.Star)
            });

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        0.45,
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
                        0)
            };

        _tree.SelectedItemChanged +=
            (_, e) =>
            {
                CommitSelectedEditors();

                if (e.NewValue is
                    TreeViewItem item &&
                    item.Tag is
                    XmlNodeModel node)
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
            3);

        grid.Children.Add(
            _tree);

        var elementGrid =
            new Grid
            {
                Margin =
                    new Thickness(
                        8,
                        0,
                        0,
                        8)
            };

        elementGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        120)
            });

        elementGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        elementGrid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        elementGrid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        var nameLabel =
            new TextBlock
            {
                Text =
                    "Element:",
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(4)
            };

        _elementNameBox =
            new TextBox
            {
                Margin =
                    new Thickness(4)
            };

        var textLabel =
            new TextBlock
            {
                Text =
                    "Text value:",
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(4)
            };

        _textValueBox =
            new TextBox
            {
                Margin =
                    new Thickness(4),
                AcceptsReturn =
                    true,
                MinHeight =
                    54
            };

        Grid.SetRow(
            nameLabel,
            0);

        Grid.SetColumn(
            nameLabel,
            0);

        Grid.SetRow(
            _elementNameBox,
            0);

        Grid.SetColumn(
            _elementNameBox,
            1);

        Grid.SetRow(
            textLabel,
            1);

        Grid.SetColumn(
            textLabel,
            0);

        Grid.SetRow(
            _textValueBox,
            1);

        Grid.SetColumn(
            _textValueBox,
            1);

        elementGrid.Children.Add(
            nameLabel);

        elementGrid.Children.Add(
            _elementNameBox);

        elementGrid.Children.Add(
            textLabel);

        elementGrid.Children.Add(
            _textValueBox);

        Grid.SetColumn(
            elementGrid,
            1);

        Grid.SetRow(
            elementGrid,
            0);

        grid.Children.Add(
            elementGrid);

        _attributes =
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

        _attributes.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Attribute",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(XmlAttributeModel.Name))
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

        _attributes.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Value",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(XmlAttributeModel.Value))
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

        Grid.SetColumn(
            _attributes,
            1);

        Grid.SetRow(
            _attributes,
            1);

        grid.Children.Add(
            _attributes);

        _previewBox =
            new TextBox
            {
                IsReadOnly =
                    true,
                AcceptsReturn =
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
            2);

        grid.Children.Add(
            _previewBox);

        root.Children.Add(
            header);

        root.Children.Add(
            toolbar);

        root.Children.Add(
            footer);

        root.Children.Add(
            grid);

        Content =
            root;

        RebuildTree(
            _root);
    }

    private void AddChild()
    {
        CommitSelectedEditors();

        var parent =
            _selected
            ?? _root;

        var name =
            _newChildNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                name))
        {
            return;
        }

        try
        {
            _ =
                XName.Get(
                    name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Element Name",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var child =
            new XmlNodeModel(
                name,
                parent);

        parent.Children.Add(
            child);

        RebuildTree(
            child);
    }

    private void RemoveSelected()
    {
        if (_selected?.Parent is null)
            return;

        var parent =
            _selected.Parent;

        parent.Children.Remove(
            _selected);

        RebuildTree(
            parent);
    }

    private void MoveSelected(
        int delta)
    {
        if (_selected?.Parent is null)
            return;

        var list =
            _selected.Parent.Children;

        var index =
            list.IndexOf(
                _selected);

        var next =
            index +
            delta;

        if (index < 0 ||
            next < 0 ||
            next >=
            list.Count)
        {
            return;
        }

        list.Move(
            index,
            next);

        RebuildTree(
            _selected);
    }

    private void CommitSelectedEditors()
    {
        if (_selected is null)
            return;

        var name =
            _elementNameBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(
                name))
        {
            _ =
                XName.Get(
                    name);

            _selected.Name =
                name;
        }

        _selected.TextValue =
            _textValueBox.Text;
    }

    private void SelectNode(
        XmlNodeModel node)
    {
        _selected =
            node;

        _elementNameBox.Text =
            node.Name;

        _textValueBox.Text =
            node.TextValue;

        _attributes.ItemsSource =
            node.Attributes;

        RefreshPreview();
    }

    private void RebuildTree(
        XmlNodeModel select)
    {
        _tree.Items.Clear();

        var root =
            BuildTreeItem(
                _root);

        _tree.Items.Add(
            root);

        root.IsExpanded =
            true;

        SelectTreeItem(
            root,
            select);

        RefreshPreview();
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

    private static TreeViewItem BuildTreeItem(
        XmlNodeModel node)
    {
        var item =
            new TreeViewItem
            {
                Header =
                    node.Name,
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
        XmlNodeModel target)
    {
        if (ReferenceEquals(
                item.Tag,
                target))
        {
            item.IsSelected =
                true;

            item.BringIntoView();

            return true;
        }

        foreach (var child in item.Items)
        {
            if (child is TreeViewItem nested &&
                SelectTreeItem(
                    nested,
                    target))
            {
                item.IsExpanded =
                    true;

                return true;
            }
        }

        return false;
    }

    private static XmlNodeModel FromElement(
        XElement element,
        XmlNodeModel? parent)
    {
        var node =
            new XmlNodeModel(
                element.Name.ToString(),
                parent)
            {
                TextValue =
                    element.HasElements
                        ? string.Empty
                        : element.Value
            };

        foreach (var attribute in element.Attributes())
        {
            node.Attributes.Add(
                new XmlAttributeModel(
                    attribute.Name.ToString(),
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
        XmlNodeModel node)
    {
        if (string.IsNullOrWhiteSpace(
                node.Name))
        {
            throw new InvalidOperationException(
                "Element names cannot be empty.");
        }

        var element =
            new XElement(
                node.Name);

        var names =
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

            if (!names.Add(
                    name))
            {
                throw new InvalidOperationException(
                    $"Duplicate attribute '{name}' on <{node.Name}>.");
            }

            element.SetAttributeValue(
                name,
                attribute.Value);
        }

        if (node.Children.Count ==
            0)
        {
            if (!string.IsNullOrEmpty(
                    node.TextValue))
            {
                element.Value =
                    node.TextValue;
            }
        }
        else
        {
            foreach (var child in node.Children)
            {
                element.Add(
                    ToElement(
                        child));
            }
        }

        return element;
    }

    private sealed class XmlNodeModel
    {
        public XmlNodeModel(
            string name,
            XmlNodeModel? parent)
        {
            Name =
                name;

            Parent =
                parent;
        }

        public string Name { get; set; }

        public string TextValue { get; set; } =
            string.Empty;

        public XmlNodeModel? Parent { get; }

        public ObservableCollection<XmlAttributeModel> Attributes { get; } =
            new();

        public ObservableCollection<XmlNodeModel> Children { get; } =
            new();
    }

    public sealed class XmlAttributeModel
    {
        public XmlAttributeModel(
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
