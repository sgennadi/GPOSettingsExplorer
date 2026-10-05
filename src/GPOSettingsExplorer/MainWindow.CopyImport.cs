using System.Windows;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private async void CopyGpo_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || GpoGrid.SelectedItem is not GpoInfo source)
            return;

        var dialog = new GpoCopyWindow(source.DisplayName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        if (_gpos.Any(gpo =>
                gpo.DisplayName.Equals(
                    dialog.NewDisplayName,
                    StringComparison.CurrentCultureIgnoreCase)))
        {
            if (MessageBox.Show(
                    this,
                    $"A GPO named '{dialog.NewDisplayName}' already exists. Create another GPO with the same display name?",
                    "Copy GPO",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        IReadOnlyList<GpoLinkInfo> sourceLinks = Array.Empty<GpoLinkInfo>();

        if (dialog.CopyLinks)
        {
            await LoadLinksAsync();
            sourceLinks = _links
                .Where(link => link.GpoId == source.Id)
                .OrderBy(link => link.TargetDn, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        SetBusy(true, $"Copying {source.DisplayName}...");

        Guid copiedId = Guid.Empty;
        var warnings = new List<string>();

        try
        {
            copiedId = await Task.Run(() =>
                _gpmService.CopyGpo(
                    _domainContext.DomainName,
                    source.Id,
                    dialog.NewDisplayName,
                    dialog.CopyAcl));

            if (dialog.CopyWmiFilter &&
                !string.IsNullOrWhiteSpace(source.WmiFilterPath))
            {
                var sourceFilter = _wmiFilters.FirstOrDefault(filter =>
                    filter.Path.Equals(
                        source.WmiFilterPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    source.WmiFilterPath.Contains(
                        filter.Id,
                        StringComparison.OrdinalIgnoreCase));

                if (sourceFilter is null)
                {
                    warnings.Add(
                        $"WMI filter '{source.WmiFilterName}' could not be resolved and was not assigned to the copy.");
                }
                else
                {
                    try
                    {
                        await Task.Run(() =>
                            _gpmService.SetWmiFilter(
                                _domainContext.DomainName,
                                copiedId,
                                sourceFilter));
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"WMI filter was not copied: {ex.Message}");
                    }
                }
            }

            if (dialog.CopyLinks && sourceLinks.Count > 0)
            {
                foreach (var link in sourceLinks)
                {
                    try
                    {
                        await Task.Run(() =>
                            _gpoLinkService.UpsertLink(
                                link.TargetDn,
                                copiedId,
                                link.Enabled,
                                link.Enforced,
                                link.Order + 1));
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(
                            $"Link to '{link.TargetName}' was not copied: {ex.Message}");
                    }
                }
            }

            _auditService.Write(
                "Copy",
                "GPO",
                source.DisplayName,
                $"New name: {dialog.NewDisplayName}; New GUID: {copiedId:B}; " +
                $"Copy ACL: {dialog.CopyAcl}; Copy WMI: {dialog.CopyWmiFilter}; " +
                $"Copy links: {dialog.CopyLinks}; Warnings: {warnings.Count}",
                before: source.IdText,
                after: copiedId.ToString("B"));

            var hadIndex = _settings.Count > 0;

            await RefreshAllAsync();

            var copied = _gpos.FirstOrDefault(gpo => gpo.Id == copiedId);

            if (hadIndex && copied is not null)
                await RefreshSingleGpoSettingsAsync(copied);

            if (dialog.CopyLinks)
                await LoadLinksAsync();

            if (copied is not null)
            {
                GpoGrid.SelectedItem = copied;
                GpoGrid.ScrollIntoView(copied);
            }

            StatusText.Text = warnings.Count == 0
                ? $"GPO copied: {dialog.NewDisplayName}"
                : $"GPO copied with {warnings.Count} warning(s)";

            if (warnings.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "The GPO was copied successfully, but some optional items could not be copied:\n\n" +
                    string.Join("\n", warnings.Select(item => "• " + item)),
                    "Copy GPO",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Copy GPO",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text = copiedId == Guid.Empty
                ? "GPO copy failed"
                : "GPO copy completed only partially";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            BackupsGrid.SelectedItem is not GpoBackupInfo backup)
            return;

        var dialog = new BackupImportWindow(backup, _gpos)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        var target = dialog.SelectedTarget;
        var createdNewTarget = false;
        Guid targetId = Guid.Empty;
        string targetName;
        string safetyBackup = string.Empty;

        if (dialog.CreateNewTarget)
        {
            targetName = dialog.NewGpoName;

            if (_gpos.Any(gpo =>
                    gpo.DisplayName.Equals(
                        targetName,
                        StringComparison.CurrentCultureIgnoreCase)))
            {
                if (MessageBox.Show(
                        this,
                        $"A GPO named '{targetName}' already exists. Create another GPO with the same display name?",
                        "Import GPO Backup",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }
        }
        else
        {
            if (target is null)
                return;

            targetId = target.Id;
            targetName = target.DisplayName;

            if (MessageBox.Show(
                    this,
                    $"Import backup '{backup.DisplayName}' into existing GPO '{targetName}'?\n\n" +
                    "All current policy settings in the destination GPO will be replaced. " +
                    "The destination ACL, links, GUID and WMI-filter association remain unchanged. " +
                    "A safety backup will be created first.",
                    "Import GPO Backup",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        SetBusy(true, "Preparing GPO import...");

        try
        {
            if (dialog.CreateNewTarget)
            {
                StatusText.Text = $"Creating destination GPO '{targetName}'...";

                targetId = await Task.Run(() =>
                    _gpmService.CreateGpo(
                        _domainContext.DomainName,
                        targetName));

                createdNewTarget = true;
            }
            else
            {
                StatusText.Text = $"Backing up destination GPO '{targetName}'...";

                safetyBackup = await Task.Run(() =>
                    _gpmService.BackupGpo(
                        _domainContext.DomainName,
                        targetId,
                        $"Safety backup before importing backup {backup.BackupId:B}"));
            }

            StatusText.Text = $"Importing settings into '{targetName}'...";

            await Task.Run(() =>
                _gpmService.ImportBackupSettings(
                    _domainContext.DomainName,
                    targetId,
                    backup,
                    string.IsNullOrWhiteSpace(dialog.MigrationTablePath)
                        ? null
                        : dialog.MigrationTablePath));

            _auditService.Write(
                "Import",
                "GPO Backup",
                backup.DisplayName,
                $"Target: {targetName}; Target GUID: {targetId:B}; " +
                $"Created new target: {createdNewTarget}; " +
                $"Migration table: {dialog.MigrationTablePath}; " +
                $"Safety backup: {safetyBackup}",
                before: createdNewTarget ? "<New empty GPO>" : "Existing target policy settings",
                after: $"Imported backup {backup.BackupId:B}");

            var hadIndex = _settings.Count > 0;

            await RefreshAllAsync();

            var imported = _gpos.FirstOrDefault(gpo => gpo.Id == targetId);
            if (hadIndex && imported is not null)
                await RefreshSingleGpoSettingsAsync(imported);

            if (imported is not null)
            {
                GpoGrid.SelectedItem = imported;
                GpoGrid.ScrollIntoView(imported);
            }

            await LoadBackupsAsync();

            StatusText.Text = $"Backup imported into: {targetName}";
        }
        catch (Exception ex)
        {
            var cleanupMessage = string.Empty;

            if (createdNewTarget && targetId != Guid.Empty)
            {
                try
                {
                    await Task.Run(() =>
                        _gpmService.DeleteGpo(
                            _domainContext.DomainName,
                            targetId));

                    cleanupMessage =
                        "\n\nThe newly-created destination GPO was removed automatically because the import failed.";
                }
                catch (Exception cleanupEx)
                {
                    cleanupMessage =
                        $"\n\nAutomatic cleanup of the new destination GPO also failed: {cleanupEx.Message}";
                }
            }

            MessageBox.Show(
                this,
                ex.Message + cleanupMessage,
                "Import GPO Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text = "GPO backup import failed";
        }
        finally
        {
            SetBusy(false);
        }
    }
}
