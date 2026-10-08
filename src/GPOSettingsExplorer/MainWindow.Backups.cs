using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly GpoBackupService _gpoBackupService = new();
    private readonly ObservableCollection<GpoBackupInfo> _backups = new();
    private ICollectionView? _backupView;
    private bool _backupsInitialized;

    private void BackupsTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_backupsInitialized)
            return;

        _backupsInitialized = true;
        BackupsPathBox.Text = StoragePaths.GpoBackups;

        _backupView = CollectionViewSource.GetDefaultView(_backups);
        _backupView.Filter = FilterBackup;
        BackupsGrid.ItemsSource = _backupView;
    }

    private async void RefreshBackups_Click(object sender, RoutedEventArgs e)
    {
        await LoadBackupsAsync();
    }

    private async Task LoadBackupsAsync()
    {
        var path = BackupsPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
            path = StoragePaths.GpoBackups;

        SetBusy(true, "Loading GPO backups...");

        try
        {
            var backups = await Task.Run(() => _gpoBackupService.LoadBackups(path));
            ReplaceCollection(_backups, backups);
            _backupView?.Refresh();
            BackupCountText.Text = $"{_backups.Count:N0} backups";
            StatusText.Text = "GPO backups loaded";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load GPO Backups",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "GPO backup load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void CompareBackupWithCurrent_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            BackupsGrid.SelectedItem
                is not GpoBackupInfo backup)
        {
            return;
        }

        var current =
            _gpos.FirstOrDefault(
                gpo =>
                    gpo.Id ==
                    backup.GpoId);

        if (current is null)
        {
            MessageBox.Show(
                this,
                "The original GPO is not currently present in the connected domain.",
                "Compare Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        SetBusy(
            true,
            "Building semantic backup comparison...");

        try
        {
            var backupXmlTask =
                Task.Run(
                    () =>
                        _gpoBackupService.GenerateXmlReport(
                            backup));

            var currentXmlTask =
                Task.Run(
                    () =>
                        _gpmService.GenerateXmlReport(
                            _domainContext.DomainName,
                            current.Id));

            await Task.WhenAll(
                backupXmlTask,
                currentXmlTask);

            var rows =
                await Task.Run(
                    () =>
                        new SemanticXmlDiffService()
                            .CompareText(
                                backupXmlTask.Result,
                                currentXmlTask.Result));

            var window =
                new SemanticDiffWindow(
                    $"Backup vs Current - {backup.DisplayName}",
                    rows,
                    "Backup",
                    "Current")
                {
                    Owner =
                        this
                };

            window.ShowDialog();

            StatusText.Text =
                $"Compared backup with current GPO: {backup.DisplayName}";
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(
                this,
                "Compare Backup",
                "The backup could not be compared with the current GPO.",
                ex);
        }
        finally
        {
            SetBusy(
                false);
        }
    }

    private async void RestoreBackup_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (BackupsGrid.SelectedItem
            is not GpoBackupInfo backup)
        {
            return;
        }

        await RestoreGpoBackupAsync(
            backup,
            "Backups tab");
    }

    private async Task RestoreGpoBackupAsync(
        GpoBackupInfo backup,
        string source)
    {
        if (_domainContext is null)
            return;

        if (!backup.DomainName.Equals(
                _domainContext.DomainName,
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                $"This backup belongs to '{backup.DomainName}' and cannot be restored to '{_domainContext.DomainName}'.",
                "Restore GPO Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var currentGpo =
            _gpos.FirstOrDefault(
                gpo =>
                    gpo.Id ==
                    backup.GpoId);

        var currentStateText =
            currentGpo is null
                ? "The original GPO is currently missing and will be recreated with its original GUID."
                : "The current GPO exists. Its current state will be backed up before the restore.";

        if (MessageBox.Show(
                this,
                $"Restore '{backup.DisplayName}' from {backup.Timestamp:yyyy-MM-dd HH:mm:ss}?\n\n" +
                $"{currentStateText}\n\n" +
                "GPO links on OUs/domains/sites are not recreated by a normal GPMC restore. " +
                "The GPO settings, ACLs and WMI-filter association are restored.",
                "Restore GPO Backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Preparing GPO restore...");

        try
        {
            var safetyBackup =
                string.Empty;

            if (currentGpo is not null)
            {
                safetyBackup =
                    await Task.Run(
                        () =>
                            _gpmService.BackupGpo(
                                _domainContext.DomainName,
                                currentGpo.Id,
                                $"Safety backup before restoring backup {backup.BackupId:B}"));
            }

            StatusText.Text =
                "Restoring GPO backup...";

            await Task.Run(
                () =>
                    _gpoBackupService.Restore(
                        _domainContext.DomainName,
                        backup));

            _auditService.Write(
                "Restore",
                "GPO Backup",
                backup.DisplayName,
                $"Backup ID: {backup.BackupId:B}; Source: {backup.BackupDirectory}; " +
                $"Safety backup: {safetyBackup}; Requested from: {source}",
                before:
                    currentGpo is null
                        ? "<GPO missing>"
                        : "Current GPO state",
                after:
                    $"Restored {backup.Timestamp:O}");

            var hadIndex =
                _settings.Count >
                0;

            await RefreshAllAsync();

            var restored =
                _gpos.FirstOrDefault(
                    gpo =>
                        gpo.Id ==
                        backup.GpoId);

            if (hadIndex &&
                restored is not null)
            {
                await RefreshSingleGpoSettingsAsync(
                    restored);
            }

            if (_backupsInitialized)
            {
                await LoadBackupsAsync();
            }

            StatusText.Text =
                "GPO restore completed";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "GPO restore canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Restore GPO Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "GPO restore failed";
        }
        finally
        {
            SetBusy(
                false);
        }
    }

    private async void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsGrid.SelectedItem is not GpoBackupInfo backup)
            return;

        if (MessageBox.Show(this,
                $"Permanently delete backup '{backup.DisplayName}' from {backup.Timestamp:yyyy-MM-dd HH:mm:ss}?\n\n" +
                $"Backup ID: {backup.BackupId:B}",
                "Delete GPO Backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true, "Deleting GPO backup...");

        try
        {
            await Task.Run(() => _gpoBackupService.DeleteBackup(backup));

            _auditService.Write(
                "Delete",
                "GPO Backup",
                backup.DisplayName,
                $"Backup ID: {backup.BackupId:B}; Directory: {backup.BackupDirectory}");

            await LoadBackupsAsync();
            StatusText.Text = "GPO backup deleted";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete GPO Backup",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BackupReport_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsGrid.SelectedItem is not GpoBackupInfo backup)
            return;

        SetBusy(true, "Generating backup report...");

        try
        {
            var report = await Task.Run(() => _gpoBackupService.GenerateReport(backup));

            Process.Start(new ProcessStartInfo
            {
                FileName = report,
                UseShellExecute = true
            });

            StatusText.Text = $"Backup report: {report}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Backup Report",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = BackupsGrid.SelectedItem is GpoBackupInfo backup
            ? backup.BackupDirectory
            : BackupsPathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(path))
            path = StoragePaths.GpoBackups;

        if (!Directory.Exists(path))
        {
            MessageBox.Show(this, $"Directory does not exist:\n{path}", "Open Backup Folder",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    private void BackupSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _backupView?.Refresh();
    }

    private bool FilterBackup(object item)
    {
        if (item is not GpoBackupInfo backup)
            return false;

        var search = BackupSearchBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return backup.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               backup.DomainName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               backup.Comment.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               backup.BackupIdText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               backup.GpoIdText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               backup.BackupDirectory.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }
}
