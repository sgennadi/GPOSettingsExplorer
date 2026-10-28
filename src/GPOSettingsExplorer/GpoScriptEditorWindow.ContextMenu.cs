using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>
/// Explicit native AvalonEdit text-area context menu. Routed clipboard
/// commands target AvalonEdit's TextArea (not the floating ContextMenu)
/// so cut/copy/paste reuse the editor's normal selection, encoding and
/// clipboard behavior without replacing the entire script text.
/// </summary>
public sealed partial class GpoScriptEditorWindow
{
    private void ConfigureEditorContextMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = _editor.TextArea
        };

        static MenuItem Item(
            ContextMenu target, string label, string shortcut, Action operation)
        {
            var item = new MenuItem
            {
                Header = label,
                InputGestureText = shortcut
            };
            item.Click += (_, _) => operation();
            target.Items.Add(item);
            return item;
        }

        static void Separator(ContextMenu target) => target.Items.Add(new Separator());

        void ClipboardCommand(RoutedUICommand command)
        {
            if (command.CanExecute(null, _editor.TextArea))
                command.Execute(null, _editor.TextArea);
        }

        var undo = Item(menu, "Undo", "Ctrl+Z", () =>
        {
            if (_editor.CanUndo && !_editor.IsReadOnly && !_saving)
                _editor.Undo();
        });
        var redo = Item(menu, "Redo", "Ctrl+Y", () =>
        {
            if (_editor.CanRedo && !_editor.IsReadOnly && !_saving)
                _editor.Redo();
        });

        Separator(menu);
        var cut = Item(menu, "Cut", "Ctrl+X", () =>
        {
            if (!_saving && !_editor.IsReadOnly)
                ClipboardCommand(ApplicationCommands.Cut);
        });
        var copy = Item(menu, "Copy", "Ctrl+C", () =>
            ClipboardCommand(ApplicationCommands.Copy));
        var paste = Item(menu, "Paste", "Ctrl+V", () =>
        {
            if (!_saving && !_editor.IsReadOnly)
                ClipboardCommand(ApplicationCommands.Paste);
        });
        var copyLine = Item(menu, "Copy current line", "", () =>
        {
            var doc = _editor.Document;
            if (doc is null)
                return;
            var line = doc.GetLineByOffset(
                Math.Clamp(_editor.CaretOffset, 0, doc.TextLength));
            Clipboard.SetText(doc.GetText(line));
        });

        var copyAll = Item(menu, "Copy entire script", "", () =>
        {
            if (_editor.Document is { TextLength: > 0 })
                Clipboard.SetText(_editor.Text);
        });

        Separator(menu);
        var selectAll = Item(menu, "Select all", "Ctrl+A", () =>
            _editor.SelectAll());
        var selectLine = Item(menu, "Select current line", "", () =>
        {
            var doc = _editor.Document;
            if (doc is null)
                return;
            var line = doc.GetLineByOffset(
                Math.Clamp(_editor.CaretOffset, 0, doc.TextLength));
            _editor.Select(line.Offset, line.Length);
            _editor.Focus();
        });
        var deselect = Item(menu, "Deselect", "", () =>
        {
            _editor.Select(_editor.CaretOffset, 0);
            _editor.Focus();
        });

        Separator(menu);
        Item(menu, "Find...", "Ctrl+F", () =>
        {
            _findBox.Focus();
            _findBox.SelectAll();
        });
        Item(menu, "Find next", "F3", FindNext);
        Item(menu, "Replace...", "Ctrl+H", () =>
        {
            _replaceBox.Focus();
            _replaceBox.SelectAll();
        });
        Item(menu, "Go to line...", "Ctrl+G", () =>
        {
            _lineBox.Focus();
            _lineBox.SelectAll();
        });

        Separator(menu);
        var comment = Item(menu, "Comment selected lines", "", () =>
            CommentSelection(true));
        var uncomment = Item(menu, "Uncomment selected lines", "", () =>
            CommentSelection(false));
        var indent = Item(menu, "Indent selected lines", "", () =>
            ChangeIndent(true));
        var outdent = Item(menu, "Outdent selected lines", "", () =>
            ChangeIndent(false));
        Item(menu, "Check syntax", "F7", () => _ = ValidateAsync(true));

        Separator(menu);
        var save = Item(menu, "Save to GPO (with preview)...", "Ctrl+S",
            () => _ = SaveAsync());

        menu.Opened += (_, _) =>
        {
            var editable = !_saving && !_editor.IsReadOnly;
            var hasText = _editor.Document is { TextLength: > 0 };
            var selected = _editor.SelectionLength > 0;
            undo.IsEnabled = editable && _editor.CanUndo;
            redo.IsEnabled = editable && _editor.CanRedo;
            cut.IsEnabled = editable && selected &&
                ApplicationCommands.Cut.CanExecute(null, _editor.TextArea);
            copy.IsEnabled = selected &&
                ApplicationCommands.Copy.CanExecute(null, _editor.TextArea);
            paste.IsEnabled = editable &&
                ApplicationCommands.Paste.CanExecute(null, _editor.TextArea);
            copyLine.IsEnabled = hasText;
            copyAll.IsEnabled = hasText;
            selectAll.IsEnabled = hasText;
            selectLine.IsEnabled = hasText;
            deselect.IsEnabled = selected;
            var supportsComments = ScriptSyntaxService.IsPowerShell(_script.FileName) ||
                ScriptSyntaxService.IsBatch(_script.FileName);
            comment.IsEnabled = editable && supportsComments && hasText;
            uncomment.IsEnabled = editable && supportsComments && hasText;
            indent.IsEnabled = editable && hasText;
            outdent.IsEnabled = editable && hasText;
            save.IsEnabled = !_saving && EditingGuard.IsEnabled;
        };

        // The actual TextArea owns the document and handles right-clicks.
        // A menu attached to the enclosing Window or TextEditor chrome may
        // not open on AvalonEdit's inner text view.
        _editor.TextArea.ContextMenu = menu;
    }
}
