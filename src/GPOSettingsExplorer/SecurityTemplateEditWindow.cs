using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>No freeform raw-INF editing. User can select only vetted values.</summary>
public sealed class SecurityTemplateEditWindow : Window
{
    private readonly ComboBox _choices = new();
    public int SelectedValue { get; private set; }

    public SecurityTemplateEditWindow(SecurityEditSpecification spec)
    {
        Title = "Security Settings - limited editor";
        Width = 730;
        Height = 420;
        MinWidth = 570;
        MinHeight = 365;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);
        SelectedValue = spec.CurrentValue;

        var outer = new DockPanel { Margin = new Thickness(15) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(new TextBlock
        {
            Text = "[" + spec.Section + "] " + spec.Key,
            FontWeight = FontWeights.SemiBold,
            FontSize = UiStyle.HeadingFontSize,
            TextWrapping = TextWrapping.Wrap
        });
        head.Children.Add(new TextBlock
        {
            Text = "Current source value: " + spec.CurrentValue +
                   ". This is stored GPO data, not effective RSoP.",
            TextWrapping = TextWrapping.Wrap
        });
        head.Children.Add(new TextBlock
        {
            Text = spec.Warning,
            Foreground = UiStyle.WarningBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        });
        head.Children.Add(new TextBlock
        {
            Text = "A GPMC backup, SHA-256 concurrency check and before/after confirmation " +
                   "are required. Nothing is changed until you approve the preview.",
            Foreground = UiStyle.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(head, Dock.Top);
        outer.Children.Add(head);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 100 };
        var accept = new Button { Content = "Review change...", MinWidth = 150 };
        bottom.Children.Add(cancel);
        bottom.Children.Add(accept);
        DockPanel.SetDock(bottom, Dock.Bottom);
        outer.Children.Add(bottom);
        cancel.Click += (_, _) => { DialogResult = false; };
        accept.Click += (_, _) =>
        {
            if (_choices.SelectedItem is not SecurityEditChoice selected)
                return;
            SelectedValue = selected.Value;
            DialogResult = true;
        };

        var form = new StackPanel { Margin = new Thickness(0, 15, 0, 0) };
        form.Children.Add(new TextBlock
        {
            Text = "New value", FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7)
        });
        _choices.ItemsSource = spec.Choices;
        _choices.DisplayMemberPath = nameof(SecurityEditChoice.Label);
        _choices.SelectedItem = spec.Choices.First(c => c.Value == spec.CurrentValue);
        _choices.MinWidth = 230;
        _choices.HorizontalAlignment = HorizontalAlignment.Left;
        form.Children.Add(_choices);
        outer.Children.Add(form);
        Content = outer;
    }
}
