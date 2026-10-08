using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>
/// Fully editable, syntax-colored code surface. This window never executes scripts.
/// SYSVOL changes are made only by the caller's protected save callback.
/// </summary>
public sealed partial class GpoScriptEditorWindow : Window
{
    private readonly TextEditor _editor;
    private readonly Func<string, Task>? _saveAction;
    private readonly GpoScriptInfo _script;
    private readonly GpoScriptDocument _document;
    private readonly ObservableCollection<ScriptDiagnostic> _diagnostics = new();
    private readonly TextBlock _positionText;
    private readonly TextBlock _statusText;
    private readonly TextBlock _dirtyText;
    private readonly TextBox _findBox;
    private readonly TextBox _replaceBox;
    private readonly TextBox _lineBox;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;
    private readonly DataGrid _diagnosticGrid;
    private readonly Expander _diagnosticsPanel;
    private bool _saving;
    private bool _saveSucceeded;
    private readonly string _originalText;

    public string ScriptText => _editor.Text;

    public GpoScriptEditorWindow(
        GpoScriptInfo script,
        GpoScriptDocument document,
        int lineNumber = 0,
        Func<string, Task>? saveAction = null)
    {
        _script = script;
        _document = document;
        _saveAction = saveAction;
        _originalText = document.Text;

        Title = $"GPO Script Editor - {script.FileName}";
        Width = 1160;
        Height = 800;
        MinWidth = 690;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(9) };
        Content = root;

        var footer = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        _cancelButton = new Button
        {
            Content = "Close",
            IsCancel = true,
            ToolTip = "Close the editor. Unsaved changes require confirmation."
        };
        _cancelButton.Click += (_, _) => Close();

        _saveButton = new Button
        {
            Content = EditingGuard.IsEnabled ? "Save to GPO" : "READ ONLY",
            Style = (Style)FindResource("UiPrimaryButton"),
            IsEnabled = EditingGuard.IsEnabled,
            IsDefault = true,
            ToolTip = "Validate and save to SYSVOL after a backup and explicit change preview."
        };
        _saveButton.Click += async (_, _) => await SaveAsync();
        footer.Children.Add(_cancelButton);
        footer.Children.Add(_saveButton);

        var statusPanel = new WrapPanel { Margin = new Thickness(2, 4, 2, 4) };
        DockPanel.SetDock(statusPanel, Dock.Bottom);

        _positionText = new TextBlock
        {
            MinWidth = 138,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UiStyle.MutedBrush
        };
        _dirtyText = new TextBlock
        {
            MinWidth = 145,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UiStyle.MutedBrush,
            Margin = new Thickness(10, 0, 0, 0)
        };
        _statusText = new TextBlock
        {
            Text = ScriptSyntaxService.CheckDescription(script.FileName),
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.MutedBrush,
            Margin = new Thickness(12, 0, 0, 0)
        };

        statusPanel.Children.Add(_positionText);
        statusPanel.Children.Add(_dirtyText);
        statusPanel.Children.Add(_statusText);

        _diagnosticGrid = new DataGrid
        {
            RowStyle = (Style)FindResource("UiScriptDiagnosticRowStyle"),
            ItemsSource = _diagnostics,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            MaxHeight = 170,
            MinHeight = 105
        };
        _diagnosticGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Severity", Binding = new System.Windows.Data.Binding(nameof(ScriptDiagnostic.Severity)),
            Width = 100
        });
        _diagnosticGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Location", Binding = new System.Windows.Data.Binding(nameof(ScriptDiagnostic.Location)),
            Width = 105
        });
        _diagnosticGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Diagnostic", Binding = new System.Windows.Data.Binding(nameof(ScriptDiagnostic.Message)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _diagnosticGrid.MouseDoubleClick += (_, _) =>
        {
            if (_diagnosticGrid.SelectedItem is ScriptDiagnostic d && d.Line > 0)
                GoToLine(d.Line, d.Column);
        };

        _diagnosticsPanel = new Expander
        {
            Header = "Syntax diagnostics (F7) - click a result to navigate",
            IsExpanded = false,
            Content = _diagnosticGrid,
            Margin = new Thickness(2, 6, 2, 2)
        };
        DockPanel.SetDock(_diagnosticsPanel, Dock.Bottom);

        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(new TextBlock
        {
            Text = script.FileName,
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ScriptColor(script.Type)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{script.GpoName} | {script.Assignment} | {script.StatusText}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = script.ScopeDisabled ? UiStyle.WarningBrush : UiStyle.MutedBrush
        });
        header.Children.Add(new TextBlock
        {
            Text = script.FullPath,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.MutedBrush,
            Margin = new Thickness(0, 2, 0, 6)
        });
        header.Children.Add(new TextBlock
        {
            Text = EditingGuard.IsEnabled
                ? "Changes are previewed, backed up and verified before saving. Scripts are never executed."
                : "SAFE MODE: READ ONLY. Enable WRITE ENABLED in the main window to save changes.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Foreground = EditingGuard.IsEnabled ? UiStyle.SuccessBrush : UiStyle.WarningBrush
        });

        var tools = new WrapPanel { Margin = new Thickness(0, 5, 0, 2) };
        DockPanel.SetDock(tools, Dock.Top);

        _findBox = new TextBox
        {
            Width = 154,
            ToolTip = "Search current script (Ctrl+F / F3)"
        };
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                FindNext();
            }
        };
        _replaceBox = new TextBox { Width = 145, ToolTip = "Replacement text (Ctrl+H)" };

        tools.Children.Add(new TextBlock { Text = "Find", VerticalAlignment = VerticalAlignment.Center });
        tools.Children.Add(_findBox);
        tools.Children.Add(ToolButton("Next (F3)", (_, _) => FindNext()));
        tools.Children.Add(new TextBlock { Text = "Replace", VerticalAlignment = VerticalAlignment.Center });
        tools.Children.Add(_replaceBox);
        tools.Children.Add(ToolButton("Replace", (_, _) => ReplaceSelected()));
        tools.Children.Add(ToolButton("Replace all", (_, _) => ReplaceAll()));
        tools.Children.Add(ToolButton("Undo", (_, _) => { if (_editor.CanUndo) _editor.Undo(); }));
        tools.Children.Add(ToolButton("Redo", (_, _) => { if (_editor.CanRedo) _editor.Redo(); }));

        var options = new WrapPanel { Margin = new Thickness(0, 2, 0, 5) };
        DockPanel.SetDock(options, Dock.Top);
        _lineBox = new TextBox { Width = 54, Text = "1", ToolTip = "Line number (Ctrl+G)" };
        _lineBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                GoToTypedLine();
            }
        };

        options.Children.Add(new TextBlock { Text = "Line", VerticalAlignment = VerticalAlignment.Center });
        options.Children.Add(_lineBox);
        options.Children.Add(ToolButton("Go", (_, _) => GoToTypedLine()));
        options.Children.Add(ToolButton("Check syntax (F7)", async (_, _) => await ValidateAsync(true)));
        options.Children.Add(ToolButton("Zoom +", (_, _) => ChangeZoom(1)));
        options.Children.Add(ToolButton("Zoom -", (_, _) => ChangeZoom(-1)));

        var wrap = new CheckBox { Content = "Word wrap", IsChecked = false };
        wrap.Checked += (_, _) => _editor.WordWrap = true;
        wrap.Unchecked += (_, _) => _editor.WordWrap = false;
        var whitespace = new CheckBox { Content = "Whitespace", IsChecked = false };
        whitespace.Checked += (_, _) =>
        {
            _editor.Options.ShowSpaces = true;
            _editor.Options.ShowTabs = true;
        };
        whitespace.Unchecked += (_, _) =>
        {
            _editor.Options.ShowSpaces = false;
            _editor.Options.ShowTabs = false;
        };
        var lines = new CheckBox { Content = "Line numbers", IsChecked = true };
        lines.Checked += (_, _) => _editor.ShowLineNumbers = true;
        lines.Unchecked += (_, _) => _editor.ShowLineNumbers = false;
        var tabs = new CheckBox { Content = "Spaces for Tab", IsChecked = false };
        tabs.Checked += (_, _) => _editor.Options.ConvertTabsToSpaces = true;
        tabs.Unchecked += (_, _) => _editor.Options.ConvertTabsToSpaces = false;

        options.Children.Add(wrap);
        options.Children.Add(whitespace);
        options.Children.Add(lines);
        options.Children.Add(tabs);
        options.Children.Add(ToolButton("Comment", (_, _) => CommentSelection(true)));
        options.Children.Add(ToolButton("Uncomment", (_, _) => CommentSelection(false)));
        options.Children.Add(ToolButton("Indent", (_, _) => ChangeIndent(true)));
        options.Children.Add(ToolButton("Outdent", (_, _) => ChangeIndent(false)));
        options.Children.Add(ToolButton("Copy all", (_, _) => Clipboard.SetText(_editor.Text)));
        options.Children.Add(ToolButton("Export local copy...", (_, _) => ExportLocalCopy()));

        _editor = new TextEditor
        {
            Text = document.Text,
            ShowLineNumbers = true,
            WordWrap = false,
            FontFamily = UiStyle.MonospaceFontFamily,
            FontSize = UiStyle.MonospaceFontSize,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = SystemColors.WindowBrush,
            Foreground = SystemColors.WindowTextBrush
        };
        _editor.Options.IndentationSize = 4;
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;

        try
        {
            _editor.SyntaxHighlighting =
                ScriptSyntaxHighlightingService.ForFile(script.FileName);
        }
        catch (Exception ex)
        {
            // A failed syntax definition must never stop someone viewing
            // or saving their GPO scripts.
            CrashLogService.Write("Load syntax highlighting", ex);
        }

        _editor.TextChanged += (_, _) =>
        {
            _diagnostics.Clear();
            _diagnosticsPanel.Header = "Syntax diagnostics (F7) - needs recheck after editing";
            UpdateStatus();
        };
        _editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatus();

        root.Children.Add(footer);
        root.Children.Add(statusPanel);
        root.Children.Add(_diagnosticsPanel);
        root.Children.Add(header);
        root.Children.Add(tools);
        root.Children.Add(options);
        root.Children.Add(_editor);

        Loaded += (_, _) =>
        {
            _editor.Focus();
            if (lineNumber > 0)
                GoToLine(lineNumber, 1);
            UpdateStatus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F3) { FindNext(); e.Handled = true; }
            else if (e.Key == Key.F7) { _ = ValidateAsync(true); e.Handled = true; }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.S) { _ = SaveAsync(); e.Handled = true; }
                else if (e.Key == Key.F) { _findBox.Focus(); _findBox.SelectAll(); e.Handled = true; }
                else if (e.Key == Key.H) { _replaceBox.Focus(); _replaceBox.SelectAll(); e.Handled = true; }
                else if (e.Key == Key.G) { _lineBox.Focus(); _lineBox.SelectAll(); e.Handled = true; }
            }
        };
        Closing += (_, e) =>
        {
            if (_saving)
            {
                e.Cancel = true;
                return;
            }
            if (_saveSucceeded || _editor.Text.Equals(_originalText, StringComparison.Ordinal))
                return;

            if (MessageBox.Show(this,
                    "Discard unsaved changes to this script?",
                    "Unsaved GPO script changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
    }

    private static Brush ScriptColor(string type) => type.ToUpperInvariant() switch
    {
        "BAT" => UiStyle.FileBatchBrush,
        "CMD" => UiStyle.FileCmdBrush,
        "PS1" or "PSM1" or "PSD1" => UiStyle.FilePowerShellBrush,
        _ => UiStyle.FileOtherBrush
    };

    private static Button ToolButton(string title, RoutedEventHandler clicked)
    {
        var button = new Button { Content = title };
        button.Click += clicked;
        return button;
    }
}
