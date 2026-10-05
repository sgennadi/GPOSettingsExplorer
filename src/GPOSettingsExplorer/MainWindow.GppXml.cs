using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly GppDocumentService _gppDocumentService = new();
    private readonly ObservableCollection<GppDocumentInfo> _gppDocuments = new();

    private ICollectionView? _gppDocumentView;
    private CancellationTokenSource? _gppDocumentCancellation;
    private bool _gppDocumentInitialized;

    private void GppXmlTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_gppDocumentInitialized)
            return;

        _gppDocumentInitialized = true;

        _gppDocumentView = CollectionViewSource.GetDefaultView(_gppDocuments);
        _gppDocumentView.Filter = FilterGppDocument;
        GppXmlGrid.ItemsSource = _gppDocumentView;

        GppXmlScopeCombo.ItemsSource = new[] { "All", "Computer", "User" };
        GppXmlScopeCombo.SelectedIndex = 0;

        var types = new[] { "All" }
            .Concat(_gppDocumentService.GetKnownTypes().Select(type => type.Name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name.Equals("All", StringComparison.OrdinalIgnoreCase) ? string.Empty : name)
            .ToArray();

        GppXmlTypeCombo.ItemsSource = types;
        GppXmlTypeCombo.SelectedIndex = 0;
    }

    private async void LoadGppXml_Click(object sender, RoutedEventArgs e)
    {
        await LoadGppDocumentsAsync();
    }

    private async Task LoadGppDocumentsAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppDocumentCancellation?.Cancel();
        _gppDocumentCancellation = new CancellationTokenSource();

        SetBusy(true, "Loading Group Policy Preferences XML documents...");

        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusText.Text = message;
                HeaderStatusText.Text = message;
            });

            var rows = await Task.Run(() =>
                _gppDocumentService.Load(
                    _gpos,
                    progress,
                    _gppDocumentCancellation.Token));

            ReplaceCollection(_gppDocuments, rows);
            _gppDocumentView?.Refresh();

            UpdateGppDocumentCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppDocuments.Count:N0} GPP XML documents";

            StatusText.Text = "GPP XML documents loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "GPP XML loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load GPP XML",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "GPP XML load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void EditGppXml_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedGppDocumentAsync();
    }

    private async void GppXmlGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await EditSelectedGppDocumentAsync();
    }

    private async Task EditSelectedGppDocumentAsync()
    {
        if (_domainContext is null ||
            GppXmlGrid.SelectedItem is not GppDocumentInfo selected)
            return;

        var gpo = _gpos.FirstOrDefault(candidate => candidate.Id == selected.GpoId);
        if (gpo is null)
            return;

        string xml;

        try
        {
            xml = await Task.Run(() => _gppDocumentService.ReadXml(selected));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Open GPP XML",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var editor = new GppXmlEditorWindow(selected, xml)
        {
            Owner = this
        };

        if (editor.ShowDialog() != true)
            return;

        await SaveGppDocumentAsync(
            gpo,
            selected,
            editor.Xml,
            "Edit");
    }

    private async void ImportGppXml_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        var target = new GppDocumentTargetWindow(
            _gpos,
            _gppDocumentService.GetKnownTypes(),
            "Import / Replace GPP XML")
        {
            Owner = this
        };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null ||
            target.SelectedType is null)
            return;

        var open = new OpenFileDialog
        {
            Title = $"Select {target.SelectedType.Name} XML",
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (open.ShowDialog(this) != true)
            return;

        string xml;

        try
        {
            xml = File.ReadAllText(open.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Read XML File",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var document = _gppDocumentService.BuildTarget(
            target.SelectedGpo,
            target.SelectedScope,
            target.SelectedType);

        var existing = File.Exists(document.XmlPath);

        var editor = new GppXmlEditorWindow(document, xml)
        {
            Owner = this
        };

        if (editor.ShowDialog() != true)
            return;

        if (existing &&
            MessageBox.Show(
                this,
                $"{target.SelectedType.Name} already exists in '{target.SelectedGpo.DisplayName}' ({target.SelectedScope}).\n\n" +
                "Replace the existing Preferences XML document? A full GPO backup will be created first.",
                "Replace GPP XML",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await SaveGppDocumentAsync(
            target.SelectedGpo,
            document,
            editor.Xml,
            existing ? "Replace from file" : "Import from file");
    }

    private async Task SaveGppDocumentAsync(
        GpoInfo gpo,
        GppDocumentInfo document,
        string xml,
        string action)
    {
        if (_domainContext is null)
            return;

        SetBusy(true, "Backing up GPO before Preferences XML change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before {action.ToLowerInvariant()} {document.PreferenceType} Preferences XML"));

            StatusText.Text = $"Saving {document.PreferenceType} Preferences XML...";

            await Task.Run(() =>
                _gppDocumentService.SaveXml(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    document,
                    xml));

            _auditService.Write(
                action,
                "GPP XML",
                gpo.DisplayName,
                $"Scope: {document.Scope}; Type: {document.PreferenceType}; " +
                $"Path: {document.RelativePath}; Backup: {backup}",
                after: $"CSE={document.CseGuidText}; Tool={document.ToolGuidText}");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppDocumentsAsync();

            if (document.PreferenceType.Equals(
                    "Registry",
                    StringComparison.OrdinalIgnoreCase) &&
                _gppRegistryInitialized)
            {
                await LoadGppRegistryAsync();
            }

            StatusText.Text =
                $"{document.PreferenceType} Preferences XML saved. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save GPP XML",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "GPP XML change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppXml_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppXmlGrid.SelectedItem is not GppDocumentInfo selected)
            return;

        var gpo = _gpos.FirstOrDefault(candidate => candidate.Id == selected.GpoId);
        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete the complete {selected.PreferenceType} Preferences document from '{gpo.DisplayName}' ({selected.Scope})?\n\n" +
                $"This removes all {selected.ItemCount:N0} item(s) stored in:\n{selected.RelativePath}\n\n" +
                "A full GPO backup will be created first.",
                "Delete GPP XML",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true, "Backing up GPO before deleting Preferences XML...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before deleting {selected.PreferenceType} Preferences XML"));

            await Task.Run(() =>
                _gppDocumentService.Delete(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    selected));

            _auditService.Write(
                "Delete",
                "GPP XML",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Type: {selected.PreferenceType}; " +
                $"Path: {selected.RelativePath}; Items: {selected.ItemCount}; Backup: {backup}",
                before: selected.XmlPath,
                after: "<Removed>");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppDocumentsAsync();

            if (selected.PreferenceType.Equals(
                    "Registry",
                    StringComparison.OrdinalIgnoreCase) &&
                _gppRegistryInitialized)
            {
                await LoadGppRegistryAsync();
            }

            StatusText.Text =
                $"{selected.PreferenceType} Preferences document deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete GPP XML",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "GPP XML delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ExportGppXml_Click(object sender, RoutedEventArgs e)
    {
        if (GppXmlGrid.SelectedItem is not GppDocumentInfo selected)
            return;

        if (!File.Exists(selected.XmlPath))
            return;

        var save = new SaveFileDialog
        {
            Title = $"Export {selected.PreferenceType} XML",
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            FileName = $"{SanitizeExportName(selected.GpoName)}-{SanitizeExportName(selected.PreferenceType)}-{selected.Scope}.xml",
            InitialDirectory = StoragePaths.Exports,
            AddExtension = true,
            DefaultExt = ".xml"
        };

        if (save.ShowDialog(this) != true)
            return;

        try
        {
            File.Copy(selected.XmlPath, save.FileName, overwrite: true);
            StatusText.Text = $"Exported GPP XML: {save.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Export GPP XML",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenGppXmlFile_Click(object sender, RoutedEventArgs e)
    {
        if (GppXmlGrid.SelectedItem is not GppDocumentInfo selected)
            return;

        if (!File.Exists(selected.XmlPath))
        {
            MessageBox.Show(
                this,
                $"File does not exist:\n{selected.XmlPath}",
                "GPP XML",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{selected.XmlPath}\"",
            UseShellExecute = true
        });
    }

    private void GppXmlSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppDocumentView?.Refresh();
        UpdateGppDocumentCount();
    }

    private void GppXmlFilter_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppDocumentView?.Refresh();
        UpdateGppDocumentCount();
    }

    private bool FilterGppDocument(object item)
    {
        if (item is not GppDocumentInfo document)
            return false;

        var scope = Convert.ToString(GppXmlScopeCombo?.SelectedItem) ?? "All";
        if (!scope.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !document.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase))
            return false;

        var type = Convert.ToString(GppXmlTypeCombo?.SelectedItem) ?? "All";
        if (!type.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !document.PreferenceType.Equals(type, StringComparison.CurrentCultureIgnoreCase))
            return false;

        var search = GppXmlSearchBox?.Text?.Trim();

        return string.IsNullOrWhiteSpace(search) ||
               document.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppDocumentCount()
    {
        if (_gppDocumentView is null)
            return;

        var shown = _gppDocumentView.Cast<object>().Count();
        GppXmlCountText.Text =
            $"{shown:N0} shown / {_gppDocuments.Count:N0} total";
    }

    private static string SanitizeExportName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value;
    }
}
