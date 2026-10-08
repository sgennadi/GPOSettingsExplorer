using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public sealed partial class GpoScriptEditorWindow
{
    private string _validationMessage = "Not yet checked";

    private void UpdateStatus()
    {
        if (_editor.Document is null)
            return;

        var caret = _editor.TextArea.Caret;
        _positionText.Text =
            $"Ln {caret.Line}, Col {caret.Column} / {_editor.Document.LineCount} lines";
        var changed = !_editor.Text.Equals(_originalText, StringComparison.Ordinal);
        _dirtyText.Text = changed ? "● Unsaved changes" : "Saved / unchanged";
        _dirtyText.Foreground = changed ? UiStyle.WarningBrush : UiStyle.SuccessBrush;

        var encoding = _document.CodePage == 65001
            ? (_document.EmitBom ? "UTF-8 BOM" : "UTF-8")
            : $"Windows CP {_document.CodePage}";
        _statusText.Text =
            $"{encoding} | EOL: {EolDescription()} | {_validationMessage}";
    }

    private string EolDescription() =>
        _document.NewLine switch
        {
            "\r\n" => "CRLF",
            "\n" => "LF",
            "\r" => "CR",
            _ => "System default"
        };

    private void FindNext()
    {
        var term = _findBox.Text;
        if (string.IsNullOrEmpty(term))
        {
            _findBox.Focus();
            return;
        }

        var content = _editor.Text;
        var start = Math.Min(_editor.SelectionStart + Math.Max(1, _editor.SelectionLength), content.Length);
        var offset = content.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
        if (offset < 0)
            offset = content.IndexOf(term, 0, StringComparison.OrdinalIgnoreCase);

        if (offset < 0)
        {
            _validationMessage = "Search: not found";
            UpdateStatus();
            return;
        }

        _editor.Select(offset, term.Length);
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(offset).LineNumber);
        _editor.Focus();
    }

    private void ReplaceSelected()
    {
        if (string.IsNullOrEmpty(_findBox.Text))
        {
            _findBox.Focus();
            return;
        }

        if (_editor.SelectionLength > 0 &&
            _editor.SelectedText.Equals(_findBox.Text, StringComparison.OrdinalIgnoreCase))
        {
            _editor.Document.Replace(
                _editor.SelectionStart,
                _editor.SelectionLength,
                _replaceBox.Text);
        }
        FindNext();
    }

    private void ReplaceAll()
    {
        var term = _findBox.Text;
        if (string.IsNullOrEmpty(term))
        {
            _findBox.Focus();
            return;
        }

        var text = _editor.Text;
        var matches = new List<int>();
        var position = 0;
        while (position < text.Length)
        {
            var found = text.IndexOf(term, position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                break;
            matches.Add(found);
            position = found + term.Length;
        }

        if (matches.Count == 0)
        {
            _validationMessage = "Replace all: no matches";
            UpdateStatus();
            return;
        }

        var decision = MessageBox.Show(
            this,
            $"Replace {matches.Count:N0} occurrence(s) of '{term}' in this script?",
            "Replace all",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (decision != MessageBoxResult.Yes)
            return;

        _editor.Document.BeginUpdate();
        try
        {
            foreach (var offset in matches.AsEnumerable().Reverse())
                _editor.Document.Replace(offset, term.Length, _replaceBox.Text);
        }
        finally
        {
            _editor.Document.EndUpdate();
        }

        _validationMessage = $"Replaced {matches.Count:N0} occurrence(s)";
        UpdateStatus();
    }

    private void GoToTypedLine()
    {
        if (!int.TryParse(_lineBox.Text, out var number))
        {
            MessageBox.Show(this, "Enter a valid line number.", "Go to line",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        GoToLine(number, 1);
    }

    private void GoToLine(int line, int column)
    {
        var document = _editor.Document;
        if (document is null)
            return;
        var actualLine = document.GetLineByNumber(
            Math.Clamp(line, 1, document.LineCount));
        var offset = actualLine.Offset + Math.Clamp(column - 1, 0, actualLine.Length);
        _editor.Select(offset, 0);
        _editor.CaretOffset = offset;
        _editor.ScrollToLine(actualLine.LineNumber);
        _editor.Focus();
    }

    private void ChangeZoom(int delta)
    {
        _editor.FontSize = Math.Clamp(
            _editor.FontSize + delta,
            UiStyle.MonospaceFontSize * 0.65,
            UiStyle.MonospaceFontSize * 2.5);
    }

    private async Task<IReadOnlyList<ScriptDiagnostic>> ValidateAsync(bool showPanel)
    {
        var text = _editor.Text;
        _validationMessage = "Checking syntax without running the script...";
        UpdateStatus();

        var diagnostics = await Task.Run(
            () => ScriptSyntaxService.Analyze(text, _script.FileName));

        // A save may have started in the meantime or the text may have changed.
        if (!text.Equals(_editor.Text, StringComparison.Ordinal))
        {
            _validationMessage = "Script changed; please check again";
            UpdateStatus();
            return Array.Empty<ScriptDiagnostic>();
        }

        _diagnostics.Clear();
        foreach (var diagnostic in diagnostics)
            _diagnostics.Add(diagnostic);

        var errors = diagnostics.Count(d => d.Severity == ScriptDiagnosticSeverity.Error);
        var warnings = diagnostics.Count(d => d.Severity == ScriptDiagnosticSeverity.Warning);
        var infos = diagnostics.Count(d => d.Severity == ScriptDiagnosticSeverity.Information);
        _validationMessage = errors == 0 && warnings == 0 && infos == 0
            ? "Syntax check: no issues found"
            : $"Syntax check: {errors} error(s), {warnings} warning(s), {infos} note(s)";
        _diagnosticsPanel.Header =
            $"Syntax diagnostics: {errors} error(s) / {warnings} warning(s)";
        if (showPanel || diagnostics.Count > 0)
            _diagnosticsPanel.IsExpanded = true;

        UpdateStatus();
        return diagnostics;
    }

    private async Task SaveAsync()
    {
        if (_saving || !EditingGuard.IsEnabled)
            return;

        if (_editor.Text.Equals(_originalText, StringComparison.Ordinal))
        {
            _saveSucceeded = true;
            DialogResult = true;
            return;
        }

        _saving = true;
        _saveButton.IsEnabled = false;
        _saveButton.Content = "Checking / saving...";
        _cancelButton.IsEnabled = false;
        _editor.IsReadOnly = true;

        try
        {
            var diagnostics = await ValidateAsync(showPanel: false);
            if (diagnostics.Any(d => d.Severity == ScriptDiagnosticSeverity.Error))
            {
                var response = MessageBox.Show(
                    this,
                    "Syntax errors were found. Saving this script could break the linked Group Policy. Save anyway?",
                    "Script syntax errors",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (response != MessageBoxResult.Yes)
                    return;
            }

            if (_saveAction is not null)
                await _saveAction(_editor.Text);

            _saveSucceeded = true;
            _saving = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            // Keep the editor and its changed text open; never discard it on failure.
            ErrorDialog.Show(
                this, "Save GPO Script",
                "The script was not saved successfully. Your edited text is preserved in the editor. Review the error, fix the problem and retry.",
                ex);
        }
        finally
        {
            _saving = false;
            if (IsLoaded)
            {
                _editor.IsReadOnly = false;
                _cancelButton.IsEnabled = true;
                _saveButton.IsEnabled = EditingGuard.IsEnabled;
                _saveButton.Content = EditingGuard.IsEnabled ? "Save to GPO" : "READ ONLY";
            }
        }
    }

    private void ExportLocalCopy()
    {
        var extension = Path.GetExtension(_script.FileName);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".txt";

        var dialog = new SaveFileDialog
        {
            Title = "Export local script copy (does not change GPO)",
            FileName = _script.FileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Filter = $"Script (*{extension})|*{extension}|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        // Export only to a local location: never use this non-GPO command to
        // circumvent backups, approval, audit or SYSVOL protection.
        var target = Path.GetFullPath(dialog.FileName);
        if (target.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ||
            target.Equals(Path.GetFullPath(_script.FullPath), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                "Export is restricted to local copies. Use Save to GPO for SYSVOL changes.",
                "Export script", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding encoding = _document.CodePage switch
            {
                65001 => new UTF8Encoding(_document.EmitBom),
                1200 => new UnicodeEncoding(false, _document.EmitBom),
                1201 => new UnicodeEncoding(true, _document.EmitBom),
                _ => Encoding.GetEncoding(_document.CodePage)
            };

            var text = _editor.Text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", _document.NewLine, StringComparison.Ordinal);
            File.WriteAllText(target, text, encoding);

            _validationMessage = "Local copy exported; GPO unchanged";
            UpdateStatus();
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, "Export script", "Could not write the local script copy.", ex);
        }
    }
}
