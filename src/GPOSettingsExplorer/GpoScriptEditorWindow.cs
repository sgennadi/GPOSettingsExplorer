using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public sealed class GpoScriptEditorWindow : Window
{
    private readonly TextBox _editor;
    private readonly Func<string, Task>? _saveAction;
    private bool _saving;

    public string ScriptText => _editor.Text;

    public GpoScriptEditorWindow(
        GpoScriptInfo script,
        GpoScriptDocument document,
        int lineNumber = 0,
        Func<string, Task>? saveAction = null)
    {
        _saveAction = saveAction;
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
            Content = EditingGuard.IsEnabled ? "Save" : "Save (read-only)",
            IsDefault = true,
            IsEnabled = EditingGuard.IsEnabled,
            ToolTip = EditingGuard.IsEnabled ? "Save the updated GPO script." : "Read-only mode blocks writes. Close this editor, enable WRITE ENABLED in the main window, then reopen."
        };
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true
        };

        save.Click += async (_, _) =>
        {
            if (_saveAction is null)
            {
                DialogResult = true;
                return;
            }

            _saving = true;
            save.IsEnabled = false;
            save.Content = "Saving...";
            _editor.IsReadOnly = true;
            cancel.IsEnabled = false;
            try
            {
                await _saveAction(_editor.Text);
                _saving = false;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                _saving = false;
                _editor.IsReadOnly = false;
                cancel.IsEnabled = true;

                // The editor and its unsaved text stay open on any failure.
                ErrorDialog.Show(
                    this,
                    "Save GPO Script",
                    "The script was not saved successfully. Your edited text is still in this window; fix the reported error and try Save again.",
                    ex);

                if (IsLoaded)
                {
                    save.Content = "Save";
                    save.IsEnabled = EditingGuard.IsEnabled;
                }
            }
        };

        footer.Children.Add(cancel);
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
            Foreground = UiStyle.MutedBrush,
            Margin = new Thickness(0, 2, 0, 8)
        });

        header.Children.Add(new TextBlock
        {
            Text = EditingGuard.IsEnabled
                ? "Edit only: GPO Settings Explorer never executes this script."
                : "READ ONLY: Saving is disabled. Close the editor and switch the main toolbar to WRITE ENABLED before modifying Group Policy.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = EditingGuard.IsEnabled ? UiStyle.MutedBrush : UiStyle.WarningBrush,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _editor = new TextBox
        {
            Text = document.Text,
            IsReadOnly = !EditingGuard.IsEnabled,
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

        Closing += (_, args) =>
        {
            // Keep the modal editor open until the in-flight SYSVOL save
            // finishes so a second click cannot race the GPO commit.
            if (_saving)
                args.Cancel = true;
        };

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
