using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GpoScriptEditorWindow : Window
{
    private readonly TextBox _editor;

    public string ScriptText => _editor.Text;

    public GpoScriptEditorWindow(
        GpoScriptInfo script,
        GpoScriptDocument document,
        int lineNumber = 0)
    {
        Title = $"Edit GPO Script - {script.FileName}";
        Width = 980;
        Height = 720;
        MinWidth = 680;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var footer = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var save = new Button
        {
            Content = "Save",
            IsDefault = true
        };
        save.Click += (_, _) => DialogResult = true;

        footer.Children.Add(new Button
        {
            Content = "Cancel",
            IsCancel = true
        });
        footer.Children.Add(save);

        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);

        header.Children.Add(new TextBlock
        {
            Text = script.FileName,
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        header.Children.Add(new TextBlock
        {
            Text = $"{script.GpoName} | {script.Assignment}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });

        header.Children.Add(new TextBlock
        {
            Text = script.FullPath,
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 2, 0, 8)
        });

        header.Children.Add(new TextBlock
        {
            Text = "Edit only: GPO Settings Explorer never executes this script.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _editor = new TextBox
        {
            Text = document.Text,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = UiStyle.MonospaceFontFamily,
            FontSize = UiStyle.MonospaceFontSize
        };

        root.Children.Add(footer);
        root.Children.Add(header);
        root.Children.Add(_editor);
        Content = root;

        Loaded += (_, _) =>
        {
            _editor.Focus();

            if (lineNumber <= 0)
                return;

            var lineIndex = Math.Min(
                Math.Max(lineNumber - 1, 0),
                Math.Max(_editor.LineCount - 1, 0));

            var characterIndex =
                _editor.GetCharacterIndexFromLineIndex(lineIndex);

            if (characterIndex >= 0)
            {
                _editor.Select(characterIndex, 0);
                _editor.ScrollToLine(lineIndex);
            }
        };
    }
}
