using System.Windows;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private async void SelectiveRestoreSetting_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            BackupsGrid.SelectedItem is not GpoBackupInfo backup)
            return;

        var gpo = _gpos.FirstOrDefault(g => g.Id == backup.GpoId);
        if (gpo is null)
        {
            MessageBox.Show(this,
                "Selective security setting recovery requires the original GPO " +
                "to exist. Use full GPMC restore for a deleted GPO.",
                "Selective restore", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            EditingGuard.EnsureEnabled("Selective restore");
            if (!backup.DomainName.Equals(_domainContext.DomainName,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The selected backup belongs to another domain.");

            SetBusy(true, "Examining live source and selected GPMC backup...");
            var current = await Task.Run(() => new RealSettingsSourceService().Scan(gpo));
            var plan = await Task.Run(() =>
                GpoSelectiveRecoveryService.Inspect(backup, gpo, current));
            SetBusy(false);

            if (plan.Candidates.Count == 0)
            {
                MessageBox.Show(this,
                    "No changed values are eligible for this limited restore. " +
                    "Only existing Event Audit and approved System Access values " +
                    "can be recovered here. Unsupported or missing keys are not changed.",
                    "Selective restore", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var picker = new GpoSelectiveRecoveryWindow(plan) { Owner = this };
            if (picker.ShowDialog() != true ||
                picker.SelectedCandidate is not { } candidate)
                return;

            if (MessageBox.Show(this,
                    $"Restore ONLY [{candidate.Section}] {candidate.Key}:\n" +
                    $"Current: {candidate.CurrentValue}\n" +
                    $"Backup: {candidate.BackupValue}\n\n" +
                    "No other settings will be restored. A fresh GPMC safety backup " +
                    "will be created first; a second approval preview follows.",
                    "Selective restore - one setting", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SetBusy(true, "Creating safety backup for selective restore...");
            await Task.Run(() => GpoSelectiveRecoveryService.EnsureBackupUnchanged(plan));
            var safetyBackup = await Task.Run(() =>
                _gpmService.BackupGpo(_domainContext.DomainName, gpo.Id,
                    $"Safety backup before selective restore of [{candidate.Section}] {candidate.Key} " +
                    $"from {backup.BackupId:B}"));

            await Task.Run(() => GpoSelectiveRecoveryService.EnsureBackupUnchanged(plan));
            await StaTask.Run(() => _securityTemplateService.ApplyStoredNumeric(
                gpo, _domainContext.DomainDistinguishedName,
                candidate.CurrentSource, candidate.BackupValue));

            _auditService.Write("Selective Security Setting Restore", "GPO",
                gpo.DisplayName,
                $"Setting [{candidate.Section}] {candidate.Key}; source backup " +
                $"{backup.BackupId:B}; safety backup: {safetyBackup}; " +
                $"source SHA-256: {candidate.CurrentSource.SourceSha256}",
                before: candidate.CurrentValue.ToString(),
                after: candidate.BackupValue.ToString());

            _realSourceSnapshot = await Task.Run(() =>
                new RealSettingsSourceService().Scan(gpo));
            await RefreshUnifiedCatalogAsync();
            StatusText.Text = $"One security setting restored; safety backup: {safetyBackup}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Selective restore canceled.";
        }
        catch (Exception ex)
        {
            CrashLogService.Write("Selective security setting restore", ex);
            ErrorDialog.Show(this, "Selective restore",
                "Limited recovery failed or requires verification. " +
                "Check the safety backup and AD/SYSVOL health.", ex);
            StatusText.Text = "Selective restore failed or requires verification";
        }
        finally
        {
            SetBusy(false);
        }
    }
}
