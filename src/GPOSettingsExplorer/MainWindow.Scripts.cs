using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly GpoScriptService _gpoScriptService = new();
    private readonly ObservableCollection<GpoScriptInfo> _gpoScripts = new();
    private readonly ObservableCollection<GpoScriptSearchResult> _gpoScriptSearchResults = new();
    private readonly HashSet<Guid> _selectedGpoScriptGpoIds = new();

    private ICollectionView? _gpoScriptsView;
    private bool _gpoScriptsInitialized;
    private bool _gpoScriptsLoaded;
    private CancellationTokenSource? _gpoScriptSearchCancellation;
    private TextBlock? _gpoScriptSearchScopeText;
    private ComboBox? _gpoScriptSearchModeCombo;

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

            GpoScriptSearchResultsGrid.ItemsSource =
                _gpoScriptSearchResults;

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
        object sender,
        MouseButtonEventArgs e)
    {
        if (GpoScriptSearchResultsGrid.SelectedItem is not GpoScriptSearchResult result)
        {
            return;
        }

        var script =
            SelectScriptCopyForEditing(
                result);

        if (script is null)
        {
            return;
        }

        await EditGpoScriptAsync(
            script,
            result.LineNumber);
    }

    private GpoScriptInfo? SelectScriptCopyForEditing(
        GpoScriptSearchResult result)
    {
        var copies =
            result.Scripts
                .GroupBy(
                    item =>
                        item.FullPath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.First())
                .OrderBy(
                    item =>
                        item.GpoName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(
                    item =>
                        item.Scope,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    item =>
                        item.EventName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (copies.Length == 0)
        {
            return null;
        }

        if (copies.Length == 1)
        {
            return copies[0];
        }

        var picker =
            new GpoScriptCopyPickerWindow(
                copies)
            {
                Owner =
                    this
            };

        return picker.ShowDialog() == true
            ? picker.SelectedScript
            : null;
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
                    lineNumber)
                {
                    Owner =
                        this
                };

            if (editor.ShowDialog() != true)
            {
                return;
            }

            // Never create a backup and start a write if the operator
            // has left the application in read-only mode.
            if (!EditingGuard.IsEnabled)
            {
                MessageBox.Show(
                    this,
                    "Script saving is blocked by Safe mode: READ ONLY. Enable WRITE ENABLED in the main toolbar, reopen the script and save again.",
                    "GPO Scripts - Read only",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SetBusy(
                true,
                "Backing up GPO before script edit...");

            var backupPath =
                await Task.Run(() =>
                    _gpmService.BackupGpo(
                        _domainContext.DomainName,
                        script.GpoId,
                        $"Automatic backup before editing script '{script.FileName}'"));

            var gpo =
                _gpos.FirstOrDefault(item =>
                    item.Id ==
                    script.GpoId)
                ?? throw new InvalidOperationException(
                    "The script's GPO is no longer available.");

            document.Text =
                ScriptTextSanitizer.StripOuterMarkdownFence(
                    editor.ScriptText,
                    out _);

            await StaTask.Run(() =>
                _gpoScriptService.SaveDocument(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    script,
                    document));

            _auditService.Write(
                "Edit GPO Script",
                "GPO Script",
                script.FileName,
                $"GPO: {gpo.DisplayName}; Assignment: {script.Assignment}; Path: {script.FullPath}; Backup: {backupPath}");

            await LoadGpoScriptsAsync(
                forceRefresh: false);

            RestoreGpoScriptSelection(
                selectionBeforeEdit);

            var remainingMatches =
                await RecalculateCurrentGpoScriptSearchAsync(
                    showValidationMessages: false);

            var markdownNote =
                removedMarkdownFence
                    ? " Markdown wrapper removed."
                    : string.Empty;

            StatusText.Text =
                remainingMatches is null
                    ? $"Saved {script.FileName}.{markdownNote} Backup: {backupPath}"
                    : $"Saved {script.FileName}.{markdownNote} Search refreshed: {remainingMatches.Value:N0} unique match(es) remain. Backup: {backupPath}";
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
        var query =
            GpoScriptContentSearchBox
                .Text
                .Trim();

        if (string.IsNullOrWhiteSpace(
                query))
        {
            if (showValidationMessages)
            {
                MessageBox.Show(
                    this,
                    "Enter text to find, for example: wmic, .vbs, powershell.exe, cscript or net use.",
                    "Search GPO Scripts",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return null;
        }

        var source =
            ResolveGpoScriptSearchSource(
                out var scopeDescription);

        var searchMode =
            (_gpoScriptSearchModeCombo?.SelectedItem as ComboBoxItem)?.Tag
            is GpoScriptSearchMode selectedMode
                ? selectedMode
                : GpoScriptSearchMode.ContentOnly;

        _gpoScriptSearchCancellation?.Cancel();

        _gpoScriptSearchCancellation =
            new CancellationTokenSource();

        SetBusy(
            true,
            $"Searching {scopeDescription} for '{query}'...");

        try
        {
            var results =
                await Task.Run(() =>
                    _gpoScriptService.SearchContent(
                        source,
                        query,
                        _gpoScriptSearchCancellation.Token,
                        searchMode));

            ReplaceCollection(
                _gpoScriptSearchResults,
                results);

            var physicalCopies =
                results
                    .SelectMany(item =>
                        item.Scripts)
                    .Select(item =>
                        item.FullPath)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .Count();

            GpoScriptSearchCountText.Text =
                $"{results.Count:N0} unique matches | {physicalCopies:N0} copies | {scopeDescription} | {searchMode}";

            StatusText.Text =
                results.Count == 0
                    ? $"No script matches remain for '{query}' in {scopeDescription}"
                    : $"Found {results.Count:N0} unique script matches for '{query}' in {scopeDescription}";

            return results.Count;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Script search canceled";

            return null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Search GPO Scripts",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return null;
        }
        finally
        {
            SetBusy(
                false);
        }
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
