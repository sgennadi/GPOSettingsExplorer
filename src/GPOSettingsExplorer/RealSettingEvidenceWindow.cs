using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

/// <summary>
/// Displays captured source-file values without invoking or modifying MMC,
/// Windows Registry, SYSVOL, AD or source policy files.
/// </summary>
public sealed class RealSettingEvidenceWindow : Window
{
    public RealSettingEvidenceWindow(RealSettingRecord record)
    {
        Title = "Stored GPO Source Evidence (READ ONLY)";
        Width = 880;
        Height = 600;
        MinWidth = 560;
        MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);

        var outer = new DockPanel { Margin = new Thickness(12) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = record.SettingName,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            FontSize = UiStyle.HeadingFontSize
        });
        header.Children.Add(new TextBlock
        {
            Text = record.GpoName + " | " + record.Scope + " | " +
                record.Category + " | " + record.State,
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock
        {
            Text = "Read-only saved source data, NOT effective RSoP. " +
                "This window cannot edit AD, SYSVOL or Windows Registry.",
            Foreground = UiStyle.WarningBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });

        DockPanel.SetDock(header, Dock.Top);
        outer.Children.Add(header);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var copy = new Button { Content = "Copy evidence", MinWidth = 115 };
        var close = new Button { Content = "Close", MinWidth = 85 };
        actions.Children.Add(copy);
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom);
        outer.Children.Add(actions);

        var explanation =
            $"GPO: {record.GpoName}\n" +
            $"Scope: {record.Scope}\n" +
            $"Category: {record.Category}\n" +
            $"Name: {record.SettingName}\n" +
            $"Stored state: {record.State}\n" +
            $"Type: {record.ValueType}\n" +
            $"Registry key: {record.RegistryKey}\n" +
            $"Registry value: {record.RegistryValue}\n" +
            $"Source: {record.SourceFile}\n" +
            $"Source SHA-256: {record.SourceSha256}\n" +
            $"Evidence: {record.Evidence}\n\n" +
            $"Stored value:\n{record.Value}";

        var valueBox = new TextBox
        {
            Text = explanation,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = UiStyle.MonospaceFontFamily,
            FontSize = UiStyle.MonospaceFontSize,
            Padding = new Thickness(9)
        };
        outer.Children.Add(valueBox);
        copy.Click += (_, _) => Clipboard.SetText(explanation);
        close.Click += (_, _) => Close();
        Content = outer;
    }
}
