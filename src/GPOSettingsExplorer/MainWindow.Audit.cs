using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Data;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly AuditLogReaderService _auditLogReaderService = new();
    private readonly ObservableCollection<AuditEntryInfo> _auditEntries = new();

    private ICollectionView? _auditView;
    private bool _auditInitialized;

    private void AuditTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_auditInitialized)
            return;

        _auditInitialized = true;
        AuditPathBox.Text = StoragePaths.Audit;

        _auditView = CollectionViewSource.GetDefaultView(_auditEntries);
        _auditView.Filter = FilterAudit;
        AuditGrid.ItemsSource = _auditView;
    }

    private async void RefreshAudit_Click(object sender, RoutedEventArgs e)
    {
        await LoadAuditAsync();
    }

    private async Task LoadAuditAsync()
    {
        var path = AuditPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
            path = StoragePaths.Audit;

        SetBusy(true, "Loading audit log...");

        try
        {
            var entries = await Task.Run(() => _auditLogReaderService.Load(path));
            ReplaceCollection(_auditEntries, entries);
            _auditView?.Refresh();

            AuditCountText.Text = $"{_auditEntries.Count:N0} entries";
            StatusText.Text = "Audit log loaded";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Audit Log",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Audit log load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AuditSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _auditView?.Refresh();
        UpdateAuditShownCount();
    }

    private void ExportAuditCsv_Click(object sender, RoutedEventArgs e)
    {
        var rows = _auditView?
            .Cast<object>()
            .OfType<AuditEntryInfo>()
            .ToArray() ?? Array.Empty<AuditEntryInfo>();

        var save = new SaveFileDialog
        {
            Title = "Export audit log",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"GPOSettingsExplorer-audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            InitialDirectory = StoragePaths.Exports,
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (save.ShowDialog(this) != true)
            return;

        try
        {
            using var writer = new StreamWriter(
                save.FileName,
                false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            writer.WriteLine(
                "Timestamp,User,Computer,Action,ObjectType,ObjectName,Details,Before,After,SourceFile");

            foreach (var row in rows)
            {
                writer.WriteLine(string.Join(",",
                    Csv(row.Timestamp.ToString("O")),
                    Csv(row.User),
                    Csv(row.Computer),
                    Csv(row.Action),
                    Csv(row.ObjectType),
                    Csv(row.ObjectName),
                    Csv(row.Details),
                    Csv(row.Before),
                    Csv(row.After),
                    Csv(row.SourceFile)));
            }

            StatusText.Text = $"Exported {rows.Length:N0} audit entries";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Export Audit Log",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenAuditFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = AuditPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
            path = StoragePaths.Audit;

        if (!Directory.Exists(path))
        {
            MessageBox.Show(
                this,
                $"Directory does not exist:\n{path}",
                "Open Audit Folder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    private bool FilterAudit(object item)
    {
        if (item is not AuditEntryInfo entry)
            return false;

        var search = AuditSearchBox?.Text?.Trim();
        return string.IsNullOrWhiteSpace(search) ||
               entry.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateAuditShownCount()
    {
        if (_auditView is null)
            return;

        var shown = _auditView.Cast<object>().Count();
        AuditCountText.Text =
            $"{shown:N0} shown / {_auditEntries.Count:N0} total";
    }
}
