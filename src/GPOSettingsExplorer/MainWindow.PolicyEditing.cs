using System.Text;
using System.Windows;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly AdmxCatalogService _admxCatalogService = new();
    private readonly AdmxCatalogCacheService _admxCatalogCacheService = new();
    private readonly RegistryPolicyService _registryPolicyService = new();
    private readonly SecurityTemplateService _securityTemplateService = new();
    private readonly GpoEditorNavigatorService _gpoEditorNavigatorService = new();
    private readonly AuditService _auditService = new();

    private IReadOnlyList<AdmxPolicyDefinition>? _admxPolicies;
    private readonly SemaphoreSlim _admxCatalogGate = new(1, 1);
    private bool _admxCatalogLoadedFromCache;

    private async void EditSetting_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedSettingAsync();
    }

    private async void EditSetting_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await EditSelectedSettingAsync();
    }

    private async Task EditSelectedSettingAsync()
    {
        if (SettingsGrid.SelectedItem is PolicySettingInfo setting)
            await EditConfiguredSettingAsync(setting);
    }

    // Called directly by Unified Settings; no dependence on whether the
    // advanced legacy DataGrid is visible or has an active search filter.
    private async Task EditConfiguredSettingAsync(PolicySettingInfo setting)
    {
        if (_domainContext is null)
            return;

        var gpo = _gpos.FirstOrDefault(item => item.Id == setting.GpoId);
        if (gpo is null)
        {
            return;
        }

        MarkGpoRecent(
            gpo.Id);

        SetBusy(true, "Loading Administrative Templates...");

        try
        {
            await EnsureAdmxCatalogAsync();

            var definition =
                _admxCatalogService.Find(
                    _admxPolicies!,
                    setting);

            if (definition is null)
            {
                SetBusy(false);

                var canEditBoolean =
                    _securityTemplateService.CanEditBoolean(
                        setting);

                var exactNavigationAvailable =
                    _gpoEditorNavigatorService.CanNavigateExactly(
                        setting);

                var valueWindow =
                    new SettingValueWindow(
                        gpo,
                        setting,
                        canEditBoolean,
                        exactNavigationAvailable,
                        progress => _gpoEditorNavigatorService.OpenAtSettingAsync(
                            gpo,
                            _domainContext.DomainDistinguishedName,
                            setting,
                            progress))
                    {
                        Owner = this
                    };

                if (valueWindow.ShowDialog() == true &&
                    canEditBoolean &&
                    valueWindow.SelectedBooleanValue is bool selectedBoolean &&
                    bool.TryParse(
                        setting.Value,
                        out var originalBoolean) &&
                    selectedBoolean != originalBoolean)
                {
                    SetBusy(
                        true,
                        "Backing up GPO before Security Settings change...");

                    var securityBackupPath =
                        await Task.Run(() =>
                            _gpmService.BackupGpo(
                                _domainContext.DomainName,
                                gpo.Id,
                                $"Automatic backup before editing '{setting.SettingName}'"));

                    StatusText.Text =
                        $"Writing {setting.SettingName}...";

                    await StaTask.Run(() =>
                        _securityTemplateService.ApplyBoolean(
                            gpo,
                            _domainContext.DomainDistinguishedName,
                            setting,
                            selectedBoolean));

                    _auditService.Write(
                        "Edit Security Setting",
                        "GPO",
                        gpo.DisplayName,
                        $"Setting: {setting.SettingName}; Scope: {setting.Scope}; Backup: {securityBackupPath}",
                        before: setting.Value,
                        after: selectedBoolean.ToString());

                    await RefreshSingleGpoSettingsAsync(
                        gpo);

                    StatusText.Text =
                        $"Saved {setting.SettingName} = {selectedBoolean}. Backup: {securityBackupPath}";
                }
                else
                {
                    StatusText.Text =
                        $"Showing value for {setting.SettingName}";
                }

                return;
            }

            definition = WithEffectiveScope(definition, setting.Scope);

            var unsupported = definition.Elements
                .Where(e => e.Type == AdmxElementType.Unknown)
                .Select(e => e.Id)
                .ToArray();

            if (unsupported.Length > 0)
            {
                var answer = MessageBox.Show(
                    this,
                    "This policy contains ADMX element types that the built-in editor does not safely support yet.\n\nOpen the selected GPO in the standard Group Policy editor?",
                    "Unsupported ADMX element",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (answer == MessageBoxResult.Yes)
                {
                    _gpmService.OpenEditor(gpo, _domainContext.DomainDistinguishedName);
                }

                return;
            }

            StatusText.Text = $"Reading {setting.SettingName}...";
            var session = await StaTask.Run(() =>
                _registryPolicyService.Read(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    definition,
                    setting.State));

            SetBusy(false);

            var editor = new PolicyEditorWindow(gpo, setting, definition, session)
            {
                Owner = this
            };

            if (editor.ShowDialog() != true)
            {
                return;
            }

            SetBusy(true, "Backing up GPO before change...");
            var backupPath = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before editing '{setting.SettingName}'"));

            StatusText.Text = "Writing policy setting...";
            await StaTask.Run(() =>
                _registryPolicyService.Apply(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    definition,
                    editor.Session));

            _auditService.Write(
                "Edit setting",
                "GPO",
                gpo.DisplayName,
                $"Setting: {setting.SettingName}; Scope: {setting.Scope}; Backup: {backupPath}",
                before: $"{setting.State}; {setting.Value}",
                after: editor.Session.State.ToString());

            await RefreshSingleGpoSettingsAsync(gpo);

            StatusText.Text = $"Saved. Backup: {backupPath}";
            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_settings.Count:N0} settings | {_wmiFilters.Count:N0} WMI filters";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Edit Policy Setting",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Policy change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task EnsureAdmxCatalogAsync(
        bool forceRefresh = false)
    {
        if (_domainContext is null)
        {
            return;
        }

        if (!forceRefresh &&
            _admxPolicies is not null)
        {
            return;
        }

        await _admxCatalogGate.WaitAsync();

        AdmxCatalogCacheSnapshot? cachedSnapshot = null;
        var scheduleBackgroundValidation = false;

        try
        {
            if (!forceRefresh &&
                _admxPolicies is not null)
            {
                return;
            }

            if (!forceRefresh)
            {
                cachedSnapshot =
                    await Task.Run(() =>
                        _admxCatalogCacheService.Load(
                            _domainContext.DomainName));

                if (cachedSnapshot is not null &&
                    cachedSnapshot.Policies.Count > 0)
                {
                    _admxPolicies =
                        cachedSnapshot.Policies;

                    _admxCatalogService.UseCachedStoreState(
                        cachedSnapshot.SourcePath,
                        cachedSnapshot.Language);

                    _admxCatalogLoadedFromCache =
                        true;

                    HeaderStatusText.Text =
                        $"ADMX: {_admxPolicies.Count:N0} policies (cached) from {cachedSnapshot.SourcePath}";

                    scheduleBackgroundValidation =
                        true;

                    return;
                }
            }

            await RefreshAdmxCatalogCacheAsync(
                forceRefresh: true);
        }
        finally
        {
            _admxCatalogGate.Release();

            if (scheduleBackgroundValidation &&
                cachedSnapshot is not null)
            {
                _ = ValidateCachedAdmxCatalogAsync(
                    cachedSnapshot);
            }
        }
    }

    private async Task ValidateCachedAdmxCatalogAsync(
        AdmxCatalogCacheSnapshot cachedSnapshot)
    {
        if (_domainContext is null)
        {
            return;
        }

        if (!await _admxCatalogGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            AdmxStoreState currentState;

            try
            {
                currentState =
                    await Task.Run(() =>
                        _admxCatalogService.GetStoreState(
                            _domainContext.DomainName));
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    $"Cached ADMX catalog shown - background validation failed: {ex.Message}";

                return;
            }

            if (currentState.Fingerprint.Equals(
                    cachedSnapshot.Fingerprint,
                    StringComparison.OrdinalIgnoreCase) &&
                currentState.SourcePath.Equals(
                    cachedSnapshot.SourcePath,
                    StringComparison.OrdinalIgnoreCase) &&
                currentState.Language.Equals(
                    cachedSnapshot.Language,
                    StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text =
                    "ADMX catalog cache is up to date";

                return;
            }

            StatusText.Text =
                "Central Store changed - refreshing ADMX cache...";

            await RefreshAdmxCatalogCacheAsync(
                forceRefresh: true);

            if (AdmxCatalogTab.IsSelected)
            {
                ApplyAdmxFilter();
            }

            StatusText.Text =
                "ADMX catalog cache refreshed";
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Cached ADMX catalog shown - background refresh failed: {ex.Message}";
        }
        finally
        {
            _admxCatalogGate.Release();
        }
    }

    private async Task RefreshAdmxCatalogCacheAsync(
        bool forceRefresh)
    {
        if (_domainContext is null)
        {
            return;
        }

        var storeState =
            await Task.Run(() =>
                _admxCatalogService.GetStoreState(
                    _domainContext.DomainName));

        var policies =
            await Task.Run(() =>
                _admxCatalogService.Load(
                    storeState));

        _admxPolicies =
            policies;

        _admxCatalogLoadedFromCache =
            false;

        await Task.Run(() =>
            _admxCatalogCacheService.Save(
                _domainContext.DomainName,
                storeState,
                policies));

        HeaderStatusText.Text =
            $"ADMX: {policies.Count:N0} policies from {storeState.SourcePath}";
    }

    private async Task RefreshSingleGpoSettingsAsync(GpoInfo gpo)
    {
        if (_domainContext is null)
        {
            return;
        }

        var refreshed = await Task.Run(() =>
            _gpmService.BuildSettingsIndex(
                _domainContext.DomainName,
                new[] { gpo }));

        for (var i = _settings.Count - 1; i >= 0; i--)
        {
            if (_settings[i].GpoId == gpo.Id)
            {
                _settings.RemoveAt(i);
            }
        }

        foreach (var item in refreshed)
        {
            _settings.Add(item);
        }

        _settingsView.Refresh();
        SettingsCountText.Text = $"{_settings.Count:N0} configured settings";

        await PersistCurrentSettingsCacheAsync();
        if (AllSettingsTab.IsSelected)
            await RefreshUnifiedCatalogAsync();
    }

    private async void BackupSelectedGpo_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || GpoGrid.SelectedItem is not GpoInfo gpo)
        {
            return;
        }

        SetBusy(true, $"Backing up {gpo.DisplayName}...");

        try
        {
            var path = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    "Manual backup from GPO Settings Explorer"));

            _auditService.Write(
                "Backup",
                "GPO",
                gpo.DisplayName,
                path);

            MessageBox.Show(
                this,
                $"Backup completed successfully.\n\n{path}",
                "GPO Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            StatusText.Text = "GPO backup complete";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "GPO Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ExportSettingsCsv_Click(object sender, RoutedEventArgs e)
    {
        var save = new SaveFileDialog
        {
            Title = "Export configured GPO settings",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"GPO-settings-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            InitialDirectory = StoragePaths.Exports,
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (save.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var rows = _settingsView
                .Cast<object>()
                .OfType<PolicySettingInfo>()
                .ToArray();

            using var writer = new StreamWriter(save.FileName, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine("GPO,Scope,State,Setting,Category,Value,RegistryKey,RegistryValue");

            foreach (var row in rows)
            {
                writer.WriteLine(string.Join(",",
                    Csv(row.GpoName),
                    Csv(row.Scope),
                    Csv(row.State),
                    Csv(row.SettingName),
                    Csv(row.Category),
                    Csv(row.Value),
                    Csv(row.RegistryKey),
                    Csv(row.RegistryValue)));
            }

            _auditService.Write(
                "Export",
                "Settings",
                "Configured settings",
                $"{rows.Length} rows to {save.FileName}");

            StatusText.Text = $"Exported {rows.Length:N0} settings";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Export Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowWmiUsage_Click(object sender, RoutedEventArgs e)
    {
        if (WmiGrid.SelectedItem is not WmiFilterInfo filter)
        {
            return;
        }

        GpoSearchBox.Text = filter.Name;
        MainTabs.SelectedIndex = 0;
        _gpoView.Refresh();
        StatusText.Text = $"Showing GPOs using WMI filter: {filter.Name}";
    }

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static AdmxPolicyDefinition WithEffectiveScope(
        AdmxPolicyDefinition definition,
        string scope)
    {
        if (!definition.Scope.Equals("Both", StringComparison.OrdinalIgnoreCase))
        {
            return definition;
        }

        return new AdmxPolicyDefinition
        {
            AdmxFile = definition.AdmxFile,
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Scope = scope,
            Category = definition.Category,
            ExplainText = definition.ExplainText,
            Key = definition.Key,
            ValueName = definition.ValueName,
            EnabledValue = definition.EnabledValue,
            DisabledValue = definition.DisabledValue,
            Elements = definition.Elements
        };
    }
}
