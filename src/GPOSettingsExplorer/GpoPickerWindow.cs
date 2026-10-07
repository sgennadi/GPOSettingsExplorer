using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GpoPickerWindow : Window
{
    private readonly ComboBox _gpoCombo;

    public GpoInfo? SelectedGpo => _gpoCombo.SelectedItem as GpoInfo;

    public GpoPickerWindow(
        IEnumerable<GpoInfo> gpos,
        string title,
        string prompt,
        GpoInfo? selected = null)
    {
        Title = title;
        Width = 700;
        Height = 210;
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
        var ok = new Button { Content = "Continue", IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (SelectedGpo is null)
            {
                MessageBox.Show(
                    this,
                    "Select a GPO.",
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(4, 4, 4, 6)
        });

        _gpoCombo = new ComboBox
        {
            ItemsSource = gpos.OrderBy(gpo => gpo.DisplayName).ToArray(),
            DisplayMemberPath = nameof(GpoInfo.DisplayName),
            IsTextSearchEnabled = true,
            Margin = new Thickness(4)
        };

        _gpoCombo.SelectedItem =
            selected is null
                ? _gpoCombo.Items.Cast<GpoInfo>().FirstOrDefault()
                : _gpoCombo.Items.Cast<GpoInfo>()
                    .FirstOrDefault(gpo => gpo.Id == selected.Id)
                  ?? _gpoCombo.Items.Cast<GpoInfo>().FirstOrDefault();

        panel.Children.Add(_gpoCombo);

        root.Children.Add(buttons);
        root.Children.Add(panel);
        Content = root;
    }
}
