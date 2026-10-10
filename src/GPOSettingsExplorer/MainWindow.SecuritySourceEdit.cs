using System.Windows;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private async void EditSecuritySource_Click(object sender, RoutedEventArgs e) =>
        await EditSecuritySourceAsync();

    private async Task EditSecuritySourceAsync()
    {
        if (_domainContext is null ||
            UnifiedSettingsGrid.SelectedItem is not UnifiedSettingInfo row ||
            row.StoredSource is not RealSettingRecord source)
            return;
        if (!SecurityTemplateEditRules.TryDescribe(source, out var spec) || spec is null)
        {
            MessageBox.Show(this,
                "Only existing, recognized Event Audit and two System Access values " +
                "can be edited here. ACLs, privileges, SIDs and unknown policy types are read-only.",
                "Unsupported security setting", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            EditingGuard.EnsureEnabled("Edit security policy");
            var gpo = _gpos.FirstOrDefault(g => g.Id == source.GpoId);
            if (gpo is null || !gpo.DomainName.Equals(_domainContext.DomainName,
                    StringComparison.OrdinalIgnoreCase) ||
                _realSourceSnapshot is null || _realSourceSnapshot.GpoId != gpo.Id ||
                !_realSourceSnapshot.Matches(_domainContext.DomainName,
                    _domainContext.ConnectedServer))
                throw new InvalidOperationException(
                    "The selected GPO source snapshot is missing or stale; read the GPO files again.");

            var editor = new SecurityTemplateEditWindow(spec) { Owner = this };
            if (editor.ShowDialog() != true || editor.SelectedValue == spec.CurrentValue)
                return;

            await EnsureSecurityWritePreflightAsync(gpo);
            SetBusy(true, "Backing up GPO before security policy change...");
            var backup = await Task.Run(() => _gpmService.BackupGpo(
                _domainContext.DomainName, gpo.Id,
                $"Before limited Security Settings edit: [{spec.Section}] {spec.Key}"));
            StatusText.Text = "Writing stored security setting with SHA-256 precondition...";
            await StaTask.Run(() => _securityTemplateService.ApplyStoredNumeric(
                gpo, _domainContext.DomainDistinguishedName, source,
                editor.SelectedValue, backup));

            _auditService.Write("Edit Security Settings", "GPO", gpo.DisplayName,
                $"[{spec.Section}] {spec.Key}; backup: {backup}; source SHA-256: {source.SourceSha256}",
                before: spec.CurrentValue.ToString(), after: editor.SelectedValue.ToString());

            _realSourceSnapshot = await Task.Run(() =>
                new RealSettingsSourceService().Scan(gpo));
            await RefreshUnifiedCatalogAsync();
            StatusText.Text = $"Saved [{spec.Section}] {spec.Key}; backup: {backup}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Security policy edit canceled; no change was approved.";
        }
        catch (Exception ex)
        {
            CrashLogService.Write("Edit stored security setting", ex);
            ErrorDialog.Show(this, "Edit Security Settings",
                "Security edit failed or requires recovery inspection. Check the GPO Health report.", ex);
            StatusText.Text = "Security policy edit failed or requires verification";
        }
        finally
        {
            SetBusy(false);
        }
    }
}
