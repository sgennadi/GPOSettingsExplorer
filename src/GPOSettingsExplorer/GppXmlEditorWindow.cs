using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppXmlEditorWindow : Window
{
    private readonly TextBox _xmlBox;
    private readonly TextBlock _validationText;

    public string Xml => _xmlBox.Text;

    public GppXmlEditorWindow(
        GppDocumentInfo document,
        string xml)
    {
        Title = $"GPP XML Editor - {document.PreferenceType}";
        Width = 1200;
        Height = 820;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var footer = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);

        _validationText = new TextBlock
        {
            Margin = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = System.Windows.Media.Brushes.DimGray
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(buttons, Dock.Right);

        var validate = new Button { Content = "Validate XML" };
        validate.Click += Validate_Click;

        var format = new Button { Content = "Format XML" };
        format.Click += Format_Click;

        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var save = new Button { Content = "Save", IsDefault = true };
        save.Click += Save_Click;

        buttons.Children.Add(validate);
        buttons.Children.Add(format);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        footer.Children.Add(buttons);
        footer.Children.Add(_validationText);

        var header = new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(header, Dock.Top);

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rows = new[]
        {
            ("GPO", document.GpoName),
            ("Scope", document.Scope),
            ("Type", document.PreferenceType),
            ("Path", document.XmlPath),
            ("CSE GUID", document.CseGuidText)
        };

        for (var i = 0; i < rows.Length; i++)
        {
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var left = new TextBlock
            {
                Text = rows[i].Item1 + ":",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2)
            };

            var right = new TextBlock
            {
                Text = rows[i].Item2,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2)
            };

            Grid.SetRow(left, i);
            Grid.SetColumn(left, 0);
            Grid.SetRow(right, i);
            Grid.SetColumn(right, 1);

            headerGrid.Children.Add(left);
            headerGrid.Children.Add(right);
        }

        header.Child = headerGrid;

        _xmlBox = new TextBox
        {
            Text = xml,
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 13,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap
        };

        root.Children.Add(footer);
        root.Children.Add(header);
        root.Children.Add(_xmlBox);
        Content = root;

        Loaded += (_, _) =>
        {
            _xmlBox.Focus();
            ValidateXml(showDialog: false);
        };
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        ValidateXml(showDialog: true);
    }

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var document = XDocument.Parse(_xmlBox.Text, LoadOptions.None);
            _xmlBox.Text = document.ToString();
            _validationText.Text = "XML formatted successfully.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Invalid XML",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateXml(showDialog: true))
            return;

        DialogResult = true;
    }

    private bool ValidateXml(bool showDialog)
    {
        try
        {
            var document = XDocument.Parse(
                _xmlBox.Text,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null)
                throw new InvalidOperationException("The XML document has no root element.");

            _validationText.Text =
                $"Valid XML - root <{document.Root.Name.LocalName}>, " +
                $"{document.Descendants().Count():N0} elements.";

            if (showDialog)
            {
                MessageBox.Show(
                    this,
                    _validationText.Text,
                    "XML Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return true;
        }
        catch (Exception ex)
        {
            _validationText.Text = "Invalid XML: " + ex.Message;

            if (showDialog)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Invalid XML",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
    }
}
