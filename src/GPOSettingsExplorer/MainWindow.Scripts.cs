using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
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

    private ICollectionView? _gpoScriptsView;
    private bool _gpoScriptsInitialized;
    private bool _gpoScriptsLoaded;
    private CancellationTokenSource? _gpoScriptSearchCancellation;

    private async void GpoScriptsTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (!_gpoScriptsInitialized)
        {
            _gpoScriptsInitialized = true;

            _gpoScriptsView =
                CollectionViewSource.GetDefaultView(_gpoScripts);

            _gpoScriptsView.Filter =
                FilterGpoScript;

            GpoScriptsGrid.ItemsSource =
                _gpoScriptsView;

            GpoScriptSearchResultsGrid.ItemsSource =
                _gpoScriptSearchResults;

            GpoScriptSearchScopeCombo.ItemsSource =
                new[]
                {
                    "All files",
                    "Selected file"
                };

            GpoScriptSearchScopeCombo.SelectedIndex = 0;
        }

        if (!_gpoScriptsLoaded &&
            _gpos.Count > 0)
        {
            await LoadGpoScriptsAsync();
        }
    }

    private async void RefreshGpoScripts_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGpoScriptsAsync();
    }

    private async Task LoadGpoScriptsAsync()
    {
        if (_gpos.Count == 0)
            return;

        SetBusy(true, "Scanning GPO scripts...");

        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusText.Text = message;
            });

            var scripts = await Task.Run(() =>
                _gpoScriptService.Load(
                    _gpos,
                    progress));

            ReplaceCollection(
                _gpoScripts,
                scripts);

            _gpoScriptsView?.Refresh();
            _gpoScriptsLoaded = true;

            GpoScriptsCountText.Text =
                $"{_gpoScripts.Count:N0} script files";

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
            SetBusy(false);
        }
    }

    private void GpoScriptsFilterBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gpoScriptsView?.Refresh();
    }

    private bool FilterGpoScript(object item)
    {
        if (item is not GpoScriptInfo script)
            return false;

        var search =
            GpoScriptsFilterBox?.Text?.Trim();

        if (string.IsNullOrWhiteSpace(search))
            return true;

        return script.GpoName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               script.Scope.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               script.EventName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               script.FileName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               script.Parameters.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               script.FullPath.Contains(search, StringComparison.CurrentCultureIgnoreCase);
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
            return;

        await EditGpoScriptAsync(
            result.Script,
            result.LineNumber);
    }

    private async Task EditGpoScriptAsync(
        GpoScriptInfo? script,
        int lineNumber)
    {
        if (_domainContext is null ||
            script is null)
            return;

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

        try
        {
            var document =
                await Task.Run(() =>
                    _gpoScriptService.ReadDocument(
                        script.FullPath));

            var editor =
                new GpoScriptEditorWindow(
                    script,
                    document,
                    lineNumber)
                {
                    Owner = this
                };

            if (editor.ShowDialog() != true)
                return;

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
                    item.Id == script.GpoId)
                ?? throw new InvalidOperationException(
                    "The script's GPO is no longer available.");

            document.Text =
                editor.ScriptText;

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

            await LoadGpoScriptsAsync();

            StatusText.Text =
                $"Saved {script.FileName}. Backup: {backupPath}";
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
            SetBusy(false);
        }
    }

    private async void SearchGpoScriptContent_Click(
        object sender,
        RoutedEventArgs e)
    {
        var query =
            GpoScriptContentSearchBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show(
                this,
                "Enter text to find, for example: wmic, .vbs, powershell.exe, cscript or net use.",
                "Search GPO Scripts",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        IEnumerable<GpoScriptInfo> source =
            _gpoScripts;

        if (GpoScriptSearchScopeCombo.SelectedIndex == 1)
        {
            if (GpoScriptsGrid.SelectedItem is not GpoScriptInfo selected)
            {
                MessageBox.Show(
                    this,
                    "Select a script file first, or choose All files.",
                    "Search GPO Scripts",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            source = new[] { selected };
        }

        _gpoScriptSearchCancellation?.Cancel();
        _gpoScriptSearchCancellation =
            new CancellationTokenSource();

        SetBusy(
            true,
            $"Searching GPO scripts for '{query}'...");

        try
        {
            var results =
                await Task.Run(() =>
                    _gpoScriptService.SearchContent(
                        source,
                        query,
                        _gpoScriptSearchCancellation.Token));

            ReplaceCollection(
                _gpoScriptSearchResults,
                results);

            GpoScriptSearchCountText.Text =
                $"{results.Count:N0} matches";

            StatusText.Text =
                $"Found {results.Count:N0} script matches for '{query}'";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Script search canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Search GPO Scripts",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CopyGpoScriptPath_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GpoScriptsGrid.SelectedItem is not GpoScriptInfo script)
            return;

        Clipboard.SetText(
            script.FullPath);

        StatusText.Text =
            "Script path copied";
    }
}
