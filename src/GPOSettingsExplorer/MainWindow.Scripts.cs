using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly GpoScriptService _gpoScriptService = new();
    private readonly ObservableCollection<GpoScriptInfo> _gpoScripts = new();
    // Raw content-hash matches stay cached after Find. Only their on-screen
    // presentation changes when switching line/file mode (no SYSVOL re-read).
    private IReadOnlyList<GpoScriptSearchResult> _gpoScriptRawSearchMatches =
        Array.Empty<GpoScriptSearchResult>();
    private GpoScriptSearchPresentation? _gpoScriptSearchPresentation;
    private string _lastGpoScriptSearchScope = string.Empty;
    private readonly HashSet<Guid> _selectedGpoScriptGpoIds = new();

    private ICollectionView? _gpoScriptsView;
    private bool _gpoScriptsInitialized;
    private bool _gpoScriptsLoaded;
    private CancellationTokenSource? _gpoScriptSearchCancellation;
    private TextBlock? _gpoScriptSearchScopeText;
    private ComboBox? _gpoScriptSearchModeCombo;
    private ComboBox? _gpoScriptResultLayoutCombo;

    private async Task EnsureGpoScriptsLoadedAsync()
    {
        if (!_gpoScriptsInitialized)
        {
            _gpoScriptsInitialized = true;

            _gpoScriptsView =
                CollectionViewSource.GetDefaultView(
                    _gpoScripts);

            _gpoScriptsView.Filter =
                FilterGpoScript;

            GpoScriptsGrid.ItemsSource =
                _gpoScriptsView;

            GpoScriptsGrid.SelectionMode =
                DataGridSelectionMode.Extended;

            GpoScriptsGrid.SelectionUnit =
                DataGridSelectionUnit.FullRow;

            GpoScriptsGrid.SelectionChanged +=
                GpoScriptsGrid_SelectionChanged;

            // The GPO+file grouped results view is assigned after each
            // search or layout change; do not bind raw SHA-grouped hits.
            GpoScriptSearchResultsGrid.ItemsSource = null;

            // Search scope is now driven directly by selected GPOs and files.
            GpoScriptSearchScopeCombo.Visibility =
                Visibility.Collapsed;

            InitializeGpoScriptSearchScopeControls();

            UpdateGpoScriptSelectionSummary();
        }

        if (!_gpoScriptsLoaded &&
            _gpos.Count > 0)
        {
            await LoadGpoScriptsAsync(
                forceRefresh: false);
        }
    }

    private void InitializeGpoScriptSearchScopeControls()
    {
        if (GpoScriptContentSearchBox.Parent is not WrapPanel panel ||
            _gpoScriptSearchScopeText is not null)
        {
            return;
        }

        _gpoScriptSearchModeCombo = new ComboBox
        {
            MinWidth = 190,
            ToolTip = "Choose whether to find a word inside the script, in its file name/path, or both."
        };
        _gpoScriptSearchModeCombo.Items.Add(new ComboBoxItem
        {
            Content = "Content only",
            Tag = GpoScriptSearchMode.ContentOnly
        });
        _gpoScriptSearchModeCombo.Items.Add(new ComboBoxItem
        {
            Content = "File names & paths",
            Tag = GpoScriptSearchMode.FileNamesAndPaths
        });
        _gpoScriptSearchModeCombo.Items.Add(new ComboBoxItem
        {
            Content = "Both",
            Tag = GpoScriptSearchMode.Both
        });
        _gpoScriptSearchModeCombo.SelectedIndex = 0;
        panel.Children.Insert(
            panel.Children.IndexOf(GpoScriptSearchScopeCombo) + 1,
            _gpoScriptSearchModeCombo);

        var layoutLabel = new TextBlock
        {
            Text = "Results:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = UiStyle.MutedBrush
        };
        _gpoScriptResultLayoutCombo = new ComboBox
        {
            MinWidth = 190,
            ToolTip = "One file per GPO shows each physical SYSVOL script once. " +
                "All matched lines groups results by GPO and file. " +
                "Switching layouts does NOT re-search SYSVOL."
        };
        _gpoScriptResultLayoutCombo.Items.Add(new ComboBoxItem
        {
            Content = "Unique file per GPO",
            Tag = GpoScriptSearchViewMode.UniqueFilesPerGpo
        });
        _gpoScriptResultLayoutCombo.Items.Add(new ComboBoxItem
        {
            Content = "All matching lines",
            Tag = GpoScriptSearchViewMode.EveryMatchingLine
        });
        _gpoScriptResultLayoutCombo.SelectedIndex = 0;
        _gpoScriptResultLayoutCombo.SelectionChanged += (_, _) =>
            RefreshGpoScriptSearchPresentation();
        var layoutPosition = panel.Children.IndexOf(_gpoScriptSearchModeCombo) + 1;
        panel.Children.Insert(layoutPosition, layoutLabel);
        panel.Children.Insert(layoutPosition + 1, _gpoScriptResultLayoutCombo);

        var selectGpos =
            new Button
            {
                Content = "Select GPOs..."
            };

        selectGpos.Click +=
            SelectGpoScriptGpos_Click;

        var clearScope =
            new Button
            {
                Content = "Clear scope"
            };

        clearScope.Click +=
            ClearGpoScriptSearchScope_Click;

        _gpoScriptSearchScopeText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        14,
                        0,
                        0,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center,
                Foreground = UiStyle.MutedBrush,
                TextWrapping =
                    TextWrapping.Wrap
            };

        panel.Children.Add(
            selectGpos);

        panel.Children.Add(
            clearScope);

        panel.Children.Add(
            _gpoScriptSearchScopeText);

        UpdateGpoScriptSearchScopeSummary();
    }

    private async void RefreshGpoScripts_Click(
        object sender,
        RoutedEventArgs e)
    {
        var selection =
            CaptureGpoScriptSelection();

        await LoadGpoScriptsAsync(
            forceRefresh: true);

        RestoreGpoScriptSelection(
            selection);

        await RecalculateCurrentGpoScriptSearchAsync(
            showValidationMessages: false);
    }

    private async Task LoadGpoScriptsAsync(
        bool forceRefresh = false)
    {
        if (_gpos.Count == 0)
        {
            return;
        }

        SetBusy(
            true,
            "Scanning GPO scripts...");

        try
        {
            var progress =
                new Progress<string>(message =>
                {
                    StatusText.Text =
                        message;
                });

            var scripts =
                await Task.Run(() =>
                    _gpoScriptService.Load(
                        _gpos,
                        progress,
                        CancellationToken.None,
                        forceRefresh));

            ReplaceCollection(
                _gpoScripts,
                scripts);

            _gpoScriptsView?.Refresh();

            _gpoScriptsLoaded =
                true;

            var availableGpoIds =
                _gpoScripts
                    .Select(item =>
                        item.GpoId)
                    .ToHashSet();

            _selectedGpoScriptGpoIds.RemoveWhere(id =>
                !availableGpoIds.Contains(
                    id));

            UpdateGpoScriptSelectionSummary();

            StatusText.Text =
                $"Loaded {_gpoScripts.Count:N0} GPO script files";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load GPO Scripts",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(
                false);
        }
    }

    private void GpoScriptsFilterBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        _gpoScriptsView?.Refresh();
    }

    private bool FilterGpoScript(
        object item)
    {
        if (item is not GpoScriptInfo script)
        {
            return false;
        }

        var search =
            GpoScriptsFilterBox
                ?.Text
                ?.Trim();

        if (string.IsNullOrWhiteSpace(
                search))
        {
            return true;
        }

        return script.GpoName.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase) ||
               script.Scope.Contains(
                   search,
                   StringComparison.OrdinalIgnoreCase) ||
               script.EventName.Contains(
                   search,
                   StringComparison.OrdinalIgnoreCase) ||
               script.FileName.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase) ||
               script.Parameters.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase) ||
               script.FullPath.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void GpoScriptsGrid_SelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        UpdateGpoScriptSelectionSummary();
    }

    private void UpdateGpoScriptSelectionSummary()
    {
        var selectedFiles =
            GetSelectedGpoScripts()
                .Select(item =>
                    new ScriptSelectionKey(
                        item.GpoId,
                        item.FullPath))
                .Distinct()
                .Count();

        GpoScriptsCountText.Text =
            selectedFiles == 0
                ? $"{_gpoScripts.Count:N0} script files"
                : $"{_gpoScripts.Count:N0} script files | {selectedFiles:N0} selected";

        UpdateGpoScriptSearchScopeSummary();
    }

    private void UpdateGpoScriptSearchScopeSummary()
    {
        if (_gpoScriptSearchScopeText is null)
        {
            return;
        }

        var selectedFiles =
            GetSelectedGpoScripts()
                .Select(item =>
                    new ScriptSelectionKey(
                        item.GpoId,
                        item.FullPath))
                .Distinct()
                .Count();

        var gpoText =
            _selectedGpoScriptGpoIds.Count == 0
                ? "All GPOs"
                : $"{_selectedGpoScriptGpoIds.Count:N0} GPO(s)";

        var fileText =
            selectedFiles == 0
                ? "All files"
                : $"{selectedFiles:N0} selected file(s)";

        _gpoScriptSearchScopeText.Text =
            $"Scope: {gpoText} / {fileText}";
    }

    private IReadOnlyList<GpoScriptInfo> GetSelectedGpoScripts()
    {
        return GpoScriptsGrid
            .SelectedItems
            .OfType<GpoScriptInfo>()
            .ToArray();
    }

    private IReadOnlyList<ScriptSelectionKey> CaptureGpoScriptSelection()
    {
        return GetSelectedGpoScripts()
            .Select(item =>
                new ScriptSelectionKey(
                    item.GpoId,
                    item.FullPath))
            .Distinct()
            .ToArray();
    }

    private void RestoreGpoScriptSelection(
        IReadOnlyList<ScriptSelectionKey> selection)
    {
        GpoScriptsGrid.SelectedItems.Clear();

        if (selection.Count == 0)
        {
            UpdateGpoScriptSelectionSummary();
            return;
        }

        foreach (var script in _gpoScripts)
        {
            if (!selection.Any(item =>
                    item.GpoId == script.GpoId &&
                    item.FullPath.Equals(
                        script.FullPath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (_gpoScriptsView is not null &&
                !_gpoScriptsView.Contains(
                    script))
            {
                continue;
            }

            GpoScriptsGrid.SelectedItems.Add(
                script);
        }

        UpdateGpoScriptSelectionSummary();
    }

    private void SelectGpoScriptGpos_Click(
        object sender,
        RoutedEventArgs e)
    {
        var choices =
            _gpoScripts
                .GroupBy(item =>
                    item.GpoId)
                .Select(group =>
                    new GpoScriptGpoChoice(
                        group.Key,
                        group.First().GpoName,
                        group.Select(item => item.FullPath)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count()))
                .OrderBy(item =>
                    item.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        var picker =
            new GpoScriptGpoPickerWindow(
                choices,
                _selectedGpoScriptGpoIds)
            {
                Owner = this
            };

        if (picker.ShowDialog() != true)
        {
            return;
        }

        _selectedGpoScriptGpoIds.Clear();

        foreach (var id in picker.SelectedGpoIds)
        {
            _selectedGpoScriptGpoIds.Add(
                id);
        }

        UpdateGpoScriptSearchScopeSummary();
    }

    private void ClearGpoScriptSearchScope_Click(
        object sender,
        RoutedEventArgs e)
    {
        _selectedGpoScriptGpoIds.Clear();

        GpoScriptsGrid.SelectedItems.Clear();

        UpdateGpoScriptSelectionSummary();
    }

    private async void EditGpoScript_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditGpoScriptAsync(
            GpoScriptsGrid.SelectedItem as GpoScriptInfo,
            lineNumber: 0);
    }

    private async void GpoScriptsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditGpoScriptAsync(
            GpoScriptsGrid.SelectedItem as GpoScriptInfo,
            lineNumber: 0);
    }

    private async void GpoScriptSearchResultsGrid_MouseDoubleClick(
        object sender, MouseButtonEventArgs e)
    {
        // Ignore double-clicks on GPO group headers, file groups, column
        // headers and the Match-lines button. Only a real row opens a script.
        if (e.OriginalSource is not DependencyObject origin ||
            !IsGpoScriptResultRowHit(origin) ||
            GpoScriptSearchResultsGrid.SelectedItem is not GpoScriptSearchDisplayRow result)
            return;

        await OpenGpoScriptSearchResultAsync(result, result.LineNumber);
    }

    private static bool IsGpoScriptResultRowHit(DependencyObject source)
    {
        for (DependencyObject? node = source; node is not null;)
        {
            if (node is DataGridRow)
                return true;
            if (node is GroupItem or Expander or DataGrid)
                return false;

            node = node is Visual
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    private void ScriptMatches_PreviewMouseDoubleClick(
        object sender, MouseButtonEventArgs e)
    {
        // Prevent the row handler from also opening the first match when the
        // operator double-clicks the per-file line-match counter.
        e.Handled = true;
    }

    private async void ScriptMatches_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: GpoScriptSearchDisplayRow result })
            return;

        var dialog = new GpoScriptSearchMatchesWindow(result)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true &&
            dialog.SelectedLineNumber is int line)
            await OpenGpoScriptSearchResultAsync(result, line);
    }

    private async Task OpenGpoScriptSearchResultAsync(
        GpoScriptSearchDisplayRow result, int lineNumber)
    {
        var script = result.Script;
        StatusText.Text = $"Opening '{script.FileName}' in GPO '{script.GpoName}'" +
            (lineNumber > 0 ? $" at line {lineNumber:N0}." : ".") +
            " Only this physical SYSVOL file is selected.";
        await EditGpoScriptAsync(script, lineNumber);
    }

    private async Task EditGpoScriptAsync(
        GpoScriptInfo? script,
        int lineNumber)
    {
        if (_domainContext is null ||
            script is null)
        {
            return;
        }

        if (!script.Exists)
        {
            MessageBox.Show(
                this,
                $"Script file does not exist:\n{script.FullPath}",
                "Edit GPO Script",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var selectionBeforeEdit =
            CaptureGpoScriptSelection();

        try
        {
            var document =
                await Task.Run(() =>
                    _gpoScriptService.ReadDocument(
                        script.FullPath));

            document.Text =
                ScriptTextSanitizer.StripOuterMarkdownFence(
                    document.Text,
                    out var removedMarkdownFence);

            var editor =
                new GpoScriptEditorWindow(
                    script,
                    document,
                    lineNumber,
                    editedText => SaveGpoScriptEditorChangesAsync(
                        script,
                        document,
                        editedText,
                        removedMarkdownFence,
                        selectionBeforeEdit))
                {
                    Owner = this
                };

            // The editor stays open while its Save callback is running. Failed
            // backups, SYSVOL copies and GPO commits do not discard the text.
            editor.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Edit GPO Script",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(
                false);
        }
    }

    private async Task SaveGpoScriptEditorChangesAsync(
        GpoScriptInfo script,
        GpoScriptDocument document,
        string editedText,
        bool removedMarkdownFence,
        IReadOnlyList<ScriptSelectionKey> selectionBeforeEdit)
    {
        EditingGuard.EnsureEnabled("Save GPO script");
        var context = _domainContext
            ?? throw new InvalidOperationException("The Active Directory connection is no longer available.");

        document.Text = ScriptTextSanitizer.StripOuterMarkdownFence(editedText, out _);
        if (!document.HasChanges)
        {
            StatusText.Text = "No changes to " + script.FileName;
            return;
        }

        var gpo = _gpos.FirstOrDefault(item => item.Id == script.GpoId)
            ?? throw new InvalidOperationException("The script's GPO is no longer available.");

        SetBusy(true, "Backing up GPO before script edit...");

        try
        {
            var backupPath = await Task.Run(() =>
                _gpmService.BackupGpo(
                    context.DomainName,
                    script.GpoId,
                    $"Automatic backup before editing script '{script.FileName}'"));

            // Native SaveDocumentWithEvidence requests approval BEFORE it
            // writes any SYSVOL bytes. Do not say "Writing" while the
            // operator is still considering the modal Preview.
            SetBusy(true, "Reviewing script change and awaiting approval...");
            var saveEvidence = await StaTask.Run(() =>
                _gpoScriptService.SaveDocumentWithEvidence(
                    gpo,
                    context.DomainDistinguishedName,
                    script,
                    document));

            // No committed change means no successful EDIT audit record.
            // Never store raw script source in persistent audit JSONL: startup
            // and logon scripts sometimes embed credentials or access tokens.
            if (saveEvidence.Changed)
            {
                try
                {
                    _auditService.Write(
                        "Edit GPO Script",
                        "GPO Script",
                        script.FileName,
                        $"GPO: {gpo.DisplayName}; Assignment: {script.Assignment}; " +
                        $"Path: {script.FullPath}; Backup: {backupPath}; " +
                        $"Verified change: {saveEvidence.ChangeSummary}",
                        before: saveEvidence.Before,
                        after: saveEvidence.After);
                }
                catch (Exception auditError)
                {
                    CrashLogService.Write("GPO script saved but audit logging failed", auditError);
                }
            }

            StatusText.Text = saveEvidence.Changed
                ? $"Saved and byte-verified {script.FileName}. Backup: {backupPath}"
                : $"No byte changes committed for {script.FileName}. Backup: {backupPath}";

            try
            {
                await LoadGpoScriptsAsync(forceRefresh: true);
                RestoreGpoScriptSelection(selectionBeforeEdit);
                var matches = await RecalculateCurrentGpoScriptSearchAsync(showValidationMessages: false);

                var note = removedMarkdownFence ? " Markdown wrapper removed." : string.Empty;
                var resultDescription = saveEvidence.Changed
                    ? $"Saved and verified {script.FileName}."
                    : $"No byte changes committed for {script.FileName}.";
                StatusText.Text = matches is null
                    ? $"{resultDescription}{note} Backup: {backupPath}"
                    : $"{resultDescription}{note} Search refreshed: {matches.Value:N0} matching GPO file(s) remain. Backup: {backupPath}";
            }
            catch (Exception refreshError)
            {
                CrashLogService.Write("GPO script saved but refresh failed", refreshError);
                StatusText.Text = saveEvidence.Changed
                    ? $"Saved {script.FileName}. Backup: {backupPath}. Refresh failed; use Refresh manually."
                    : $"No byte changes committed for {script.FileName}. Backup: {backupPath}. Refresh failed; use Refresh manually.";
            }
        }
        catch (ChangePreviewCanceledException)
        {
            // Backup may already exist, but preview cancellation occurs
            // before any staged/committed script write. Propagate the typed
            // cancellation for the editor's neutral, non-error status.
            StatusText.Text =
                $"Save canceled in Preview: {script.FileName} was NOT written to SYSVOL. " +
                "Unsaved edits remain in the script editor.";
            throw;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SearchGpoScriptContent_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RecalculateCurrentGpoScriptSearchAsync(
            showValidationMessages: true);
    }

    private async Task<int?> RecalculateCurrentGpoScriptSearchAsync(
        bool showValidationMessages)
    {
        var query = GpoScriptContentSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            if (showValidationMessages)
                MessageBox.Show(this,
                    "Enter text to find, for example: wmic, .vbs, powershell.exe, cscript or net use.",
                    "Search GPO Scripts", MessageBoxButton.OK,
                    MessageBoxImage.Information);
            return null;
        }

        var source = ResolveGpoScriptSearchSource(out var scopeDescription);
        var searchMode =
            (_gpoScriptSearchModeCombo?.SelectedItem as ComboBoxItem)?.Tag
                is GpoScriptSearchMode selectedMode
                ? selectedMode : GpoScriptSearchMode.ContentOnly;

        // Capture this search's CTS locally: newer Find requests may cancel
        // it and replace the field while Task.Run is still reading SYSVOL.
        _gpoScriptSearchCancellation?.Cancel();
        var search = new CancellationTokenSource();
        _gpoScriptSearchCancellation = search;
        SetBusy(true, $"Searching {scopeDescription} for '{query}'...");

        try
        {
            var results = await Task.Run(() =>
                _gpoScriptService.SearchContent(
                    source, query, search.Token, searchMode), search.Token);

            search.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_gpoScriptSearchCancellation, search))
                return null;

            // Keep SHA-deduplicated raw results in memory. The projector
            // will expand each hit back to its real GPO + physical file;
            // changing the results layout NEVER touches remote SYSVOL.
            _gpoScriptRawSearchMatches = results;
            _lastGpoScriptSearchScope = scopeDescription;
            RefreshGpoScriptSearchPresentation();

            var uniqueFiles = _gpoScriptSearchPresentation?.UniqueFileCount ?? 0;
            StatusText.Text = uniqueFiles == 0
                ? $"No script files matched '{query}' in {scopeDescription}."
                : $"Found {uniqueFiles:N0} unique GPO script file(s) for '{query}' " +
                  $"in {scopeDescription}. Select a file or choose a matching line.";
            return uniqueFiles;
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_gpoScriptSearchCancellation, search))
                StatusText.Text = "Script search canceled; previous results preserved.";
            return null;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_gpoScriptSearchCancellation, search))
                MessageBox.Show(this, ex.Message, "Search GPO Scripts",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        finally
        {
            if (ReferenceEquals(_gpoScriptSearchCancellation, search))
            {
                _gpoScriptSearchCancellation = null;
                SetBusy(false);
            }
            search.Dispose();
        }
    }

    private void RefreshGpoScriptSearchPresentation()
    {
        if (!_gpoScriptsInitialized)
            return;

        var viewMode =
            (_gpoScriptResultLayoutCombo?.SelectedItem as ComboBoxItem)?.Tag
                is GpoScriptSearchViewMode selectedViewMode
                ? selectedViewMode
                : GpoScriptSearchViewMode.UniqueFilesPerGpo;

        var presentation = GpoScriptSearchPresentationService.Project(
            _gpoScriptRawSearchMatches, viewMode);
        _gpoScriptSearchPresentation = presentation;

        // An item is always anchored to a physical path AND GPO ID. Separate
        // file headers are shown in line mode so dozens of matched lines
        // cannot visually merge with the next file's results.
        var view = new ListCollectionView(presentation.Rows.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(
            nameof(GpoScriptSearchDisplayRow.GpoHeader)));
        if (viewMode == GpoScriptSearchViewMode.EveryMatchingLine)
            view.GroupDescriptions.Add(new PropertyGroupDescription(
                nameof(GpoScriptSearchDisplayRow.FileHeader)));

        GpoScriptSearchResultsGrid.ItemsSource = view;

        var label = viewMode == GpoScriptSearchViewMode.UniqueFilesPerGpo
            ? "unique file per GPO" : "matching lines grouped by GPO / file";
        var scope = string.IsNullOrWhiteSpace(_lastGpoScriptSearchScope)
            ? "Search not run" : _lastGpoScriptSearchScope;
        GpoScriptSearchCountText.Text =
            $"{presentation.Rows.Count:N0} rows ({label}) | " +
            $"{presentation.UniqueFileCount:N0} physical GPO file(s) | " +
            $"{presentation.GpoCount:N0} GPO(s) | " +
            $"{presentation.ContentLineCount:N0} matching line(s)" +
            (presentation.MetadataMatchCount > 0
                ? $" + {presentation.MetadataMatchCount:N0} filename/path match(es)"
                : string.Empty) +
            $" | {scope}";
    }

    private IReadOnlyList<GpoScriptInfo> ResolveGpoScriptSearchSource(
        out string scopeDescription)
    {
        IEnumerable<GpoScriptInfo> source =
            _gpoScripts;

        if (_selectedGpoScriptGpoIds.Count > 0)
        {
            source =
                source.Where(item =>
                    _selectedGpoScriptGpoIds.Contains(
                        item.GpoId));
        }

        var selectedFileKeys =
            GetSelectedGpoScripts()
                .Select(item =>
                    new ScriptSelectionKey(
                        item.GpoId,
                        item.FullPath))
                .ToArray();

        if (selectedFileKeys.Length > 0)
        {
            source =
                source.Where(item =>
                    selectedFileKeys.Any(selected =>
                        selected.GpoId == item.GpoId &&
                        selected.FullPath.Equals(
                            item.FullPath,
                            StringComparison.OrdinalIgnoreCase)));
        }

        var result =
            source
                .ToArray();

        var gpoCount =
            result
                .Select(item =>
                    item.GpoId)
                .Distinct()
                .Count();

        var fileCount =
            result
                .Select(item =>
                    new ScriptSelectionKey(
                        item.GpoId,
                        item.FullPath))
                .Distinct()
                .Count();

        if (_selectedGpoScriptGpoIds.Count == 0 &&
            selectedFileKeys.Length == 0)
        {
            scopeDescription =
                $"{fileCount:N0} files in all GPOs";
        }
        else
        {
            scopeDescription =
                $"{gpoCount:N0} GPO(s) / {fileCount:N0} file(s)";
        }

        return result;
    }

    private void CopyGpoScriptPath_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GpoScriptsGrid.SelectedItem is not GpoScriptInfo script)
        {
            return;
        }

        Clipboard.SetText(
            script.FullPath);

        StatusText.Text =
            "Script path copied";
    }

    private readonly record struct ScriptSelectionKey(
        Guid GpoId,
        string FullPath);
}
