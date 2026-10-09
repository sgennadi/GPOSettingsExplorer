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
        var changed = !_editor.Text.Equals(_originalText, StringComparison.Ordinal) ||
            _document.FormatChanged;
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

    private void CommentSelection(bool add)
    {
        var extension = Path.GetExtension(_script.FileName);
        if (!ScriptSyntaxService.IsPowerShell(_script.FileName) &&
            !ScriptSyntaxService.IsBatch(_script.FileName))
        {
            _validationMessage = "Comment tool is currently available for BAT, CMD and PowerShell only";
            UpdateStatus();
            return;
        }

        var prefix = ScriptSyntaxService.IsPowerShell(_script.FileName) ? "# " : "rem ";
        TransformSelectedLines(line =>
        {
            if (add)
                return prefix + line;

            var trimmed = line.TrimStart();
            var leading = line.Length - trimmed.Length;
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line.Remove(leading, prefix.Length);
            if (ScriptSyntaxService.IsPowerShell(_script.FileName) &&
                trimmed.StartsWith("#", StringComparison.Ordinal))
                return line.Remove(leading, 1);
            if (ScriptSyntaxService.IsBatch(_script.FileName) &&
                trimmed.StartsWith("::", StringComparison.Ordinal))
                return line.Remove(leading, 2);
            return line;
        });
    }

    private void ChangeIndent(bool add)
    {
        TransformSelectedLines(line =>
        {
            if (add)
                return "    " + line;
            if (line.StartsWith("\t", StringComparison.Ordinal))
                return line[1..];
            var spaces = line.TakeWhile(ch => ch == ' ').Count();
            return spaces == 0 ? line : line[Math.Min(spaces, 4)..];
        });
    }

    private void TransformSelectedLines(Func<string, string> transform)
    {
        var doc = _editor.Document;
        if (doc is null)
            return;

        var start = _editor.SelectionStart;
        var end = Math.Max(start, start + _editor.SelectionLength - 1);
        var first = doc.GetLineByOffset(Math.Clamp(start, 0, doc.TextLength)).LineNumber;
        var last = doc.GetLineByOffset(Math.Clamp(end, 0, doc.TextLength)).LineNumber;
        doc.BeginUpdate();
        try
        {
            for (var lineNo = last; lineNo >= first; lineNo--)
            {
                var line = doc.GetLineByNumber(lineNo);
                var oldText = doc.GetText(line);
                var newText = transform(oldText);
                if (!oldText.Equals(newText, StringComparison.Ordinal))
                    doc.Replace(line.Offset, line.Length, newText);
            }
        }
        finally
        {
            doc.EndUpdate();
        }

        _editor.Focus();
        UpdateStatus();
    }

    private void EncodingChanged()
    {
        if (_encodingCombo.SelectedItem is not ScriptEncodingChoice choice)
            return;

        _document.CodePage = choice.CodePage;
        var hasBom = ScriptEncodingService.SupportsBom(choice.CodePage);
        _bomCheck.IsEnabled = hasBom;
        if (!hasBom)
        {
            _document.EmitBom = false;
            _bomCheck.IsChecked = false;
        }

        if (!ScriptEncodingService.CanRoundTrip(
                _editor.Text, choice.CodePage, out var message))
            _validationMessage =
                "Encoding loss risk: " + message + " (Save blocked)";
        else
            _validationMessage = "Encoding selected; no conversion until Save";

        UpdateStatus();
    }

    private void BomChanged()
    {
        if (!ScriptEncodingService.SupportsBom(_document.CodePage))
        {
            _document.EmitBom = false;
            return;
        }

        _document.EmitBom = _bomCheck.IsChecked == true;
        UpdateStatus();
    }

    private void ConvertLineEndings()
    {
        if (_eolCombo.SelectedItem is not ComboBoxItem { Tag: string newline })
            return;
        if (newline == _document.NewLine &&
            !ScriptEncodingService.HasMixedNewlines(_editor.Text))
            return;

        var converted = ScriptEncodingService.NormalizeNewlines(_editor.Text, newline);
        if (converted != _editor.Text)
        {
            _editor.Document.BeginUpdate();
            try { _editor.Document.Replace(0, _editor.Document.TextLength, converted); }
            finally { _editor.Document.EndUpdate(); }
        }
        _document.NewLine = newline;
        _validationMessage = "Converted line endings to " +
            ScriptEncodingService.DisplayLineEnding(newline);
        UpdateStatus();
    }

    private void ReloadInSelectedEncoding()
    {
        if (_encodingCombo.SelectedItem is not ScriptEncodingChoice choice)
            return;
        if ((_editor.Text != _originalText || _document.FormatChanged) &&
            MessageBox.Show(this,
                "Reload the file from SYSVOL using this code page? All unsaved editor changes will be discarded.",
                "Reload script", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var loaded = new GpoScriptService().ReadDocument(
                _script.FullPath, choice.CodePage);
            _editor.Text = loaded.Text;
            _originalText = loaded.Text;
            _document.Text = loaded.Text;
            _document.OriginalText = loaded.Text;
            _document.CodePage = choice.CodePage;
            _document.OriginalCodePage = choice.CodePage;
            _document.EmitBom = loaded.EmitBom;
            _document.OriginalEmitBom = loaded.EmitBom;
            _document.NewLine = loaded.NewLine;
            _document.OriginalNewLine = loaded.NewLine;
            _bomCheck.IsChecked = loaded.EmitBom;
            _eolCombo.SelectedItem = _eolCombo.Items
                .OfType<ComboBoxItem>().FirstOrDefault(
                    item => (string)item.Tag == loaded.NewLine);
            _validationMessage = "Reloaded source bytes using code page " + choice.CodePage;
            UpdateStatus();
        }
        catch (Exception error)
        {
            ErrorDialog.Show(this, "Reload script",
                "Cannot decode the original bytes using this code page. Editor text was preserved.",
                error);
        }
    }

    private async Task<IReadOnlyList<ScriptDiagnostic>> ValidateAsync(bool showPanel)
    {
        var text = _editor.Text;
        _validationMessage = "Checking syntax without running the script...";
        UpdateStatus();

        var codePage = _document.CodePage;
        var bom = _document.EmitBom;
        var newline = _document.NewLine;
        var diagnostics = await Task.Run(() =>
            ScriptSyntaxService.Analyze(text, _script.FileName)
                .Concat(ScriptTextSafetyService.Analyze(
                    text, _script.FileName, codePage, bom, newline))
                .ToArray());

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
        if (showPanel || diagnostics.Length > 0)
            _diagnosticsPanel.IsExpanded = true;

        UpdateStatus();
        return diagnostics;
    }

    private async Task SaveAsync()
    {
        if (_saving || !EditingGuard.IsEnabled)
            return;

        if (_editor.Text.Equals(_originalText, StringComparison.Ordinal) &&
            !_document.FormatChanged)
        {
            _saveSucceeded = true;
            DialogResult = true;
            return;
        }

        if (!ScriptEncodingService.CanRoundTrip(
                _editor.Text, _document.CodePage, out var conversionError))
        {
            MessageBox.Show(this,
                "Conversion would replace or lose characters. Choose another code page.\n\n" +
                    conversionError,
                "Unsafe script encoding", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var text = ScriptEncodingService.NormalizeNewlines(
                _editor.Text, _document.NewLine);
            File.WriteAllBytes(target, ScriptEncodingService.Encode(
                text, _document.CodePage, _document.EmitBom));

            _validationMessage = "Local copy exported; GPO unchanged";
            UpdateStatus();
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, "Export script", "Could not write the local script copy.", ex);
        }
    }
}
