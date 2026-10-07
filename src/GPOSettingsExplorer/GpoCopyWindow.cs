using System.Windows;
using System.Windows.Controls;

namespace GPOSettingsExplorer;

public sealed class GpoCopyWindow : Window
{
    private readonly TextBox _nameBox;
    private readonly CheckBox _copyAcl;
    private readonly CheckBox _copyWmi;
    private readonly CheckBox _copyLinks;

    public string NewDisplayName => _nameBox.Text.Trim();
    public bool CopyAcl => _copyAcl.IsChecked == true;
    public bool CopyWmiFilter => _copyWmi.IsChecked == true;
    public bool CopyLinks => _copyLinks.IsChecked == true;

    public GpoCopyWindow(string sourceName)
    {
        Title = "Copy GPO";
        Width = 720;
        Height = 360;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(14) };

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var copy = new Button { Content = "Copy GPO", IsDefault = true };
        copy.Click += Copy_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(copy);

        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "Source GPO:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 4, 4, 2)
        });

        panel.Children.Add(new TextBlock
        {
            Text = sourceName,
            Margin = new Thickness(4, 0, 4, 12),
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = "New GPO display name:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 4, 4, 2)
        });

        _nameBox = new TextBox
        {
            Text = sourceName + " - Copy",
            Margin = new Thickness(4)
        };
        _nameBox.SelectAll();
        panel.Children.Add(_nameBox);

        _copyAcl = new CheckBox
        {
            Content = "Copy GPO permissions / ACLs",
            IsChecked = true,
            Margin = new Thickness(4, 12, 4, 4)
        };

        _copyWmi = new CheckBox
        {
            Content = "Copy WMI filter association when the filter exists in this domain",
            IsChecked = true,
            Margin = new Thickness(4)
        };

        _copyLinks = new CheckBox
        {
            Content = "Copy GPO links to the same domain / OU / site targets",
            IsChecked = false,
            Margin = new Thickness(4)
        };

        panel.Children.Add(_copyAcl);
        panel.Children.Add(_copyWmi);
        panel.Children.Add(_copyLinks);

        panel.Children.Add(new TextBlock
        {
            Text = "The new GPO always receives a new GUID. Link copying is optional and is disabled by default because it immediately changes the scope where the new GPO can apply.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(4, 12, 4, 4)
        });

        root.Children.Add(buttons);
        root.Children.Add(panel);
        Content = root;

        Loaded += (_, _) => _nameBox.Focus();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewDisplayName))
        {
            MessageBox.Show(
                this,
                "Enter a display name for the copied GPO.",
                "Copy GPO",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _nameBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
