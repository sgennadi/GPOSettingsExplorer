using System.Windows;
using System.Windows.Input;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private async void LoadAdmxCatalog_Click(object sender, RoutedEventArgs e)
    {
        await LoadAdmxCatalogIntoGridAsync();
    }

    private async void ConfigureAdmxPolicy_Click(object sender, RoutedEventArgs e)
    {
        await ConfigureSelectedAdmxPolicyAsync();
    }

    private async void AdmxGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await ConfigureSelectedAdmxPolicyAsync();
    }

    private void AdmxSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyAdmxFilter();
    }

    private async Task LoadAdmxCatalogIntoGridAsync()
    {
        if (_domainContext is null)
        {
            return;
        }

        SetBusy(true, "Loading ADMX catalog...");

        try
        {
            await EnsureAdmxCatalogAsync();
            ApplyAdmxFilter();

            AdmxCountText.Text =
                $"{_admxPolicies?.Count ?? 0:N0} policies | {_admxCatalogService.LastLanguage} | {_admxCatalogService.LastSourcePath}";

            StatusText.Text = "ADMX catalog loaded";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load ADMX Catalog",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyAdmxFilter()
    {
        if (_admxPolicies is null || AdmxGrid is null)
        {
            return;
        }

        var search = AdmxSearchBox?.Text?.Trim();
        IEnumerable<AdmxPolicyDefinition> source = _admxPolicies;

        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(policy =>
                policy.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                policy.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                policy.Scope.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                policy.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                policy.AdmxFile.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                policy.Key.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                policy.ValueName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                policy.Elements.Any(element =>
                    element.Label.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    element.ValueName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    element.Key.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        var rows = source.ToArray();
        AdmxGrid.ItemsSource = rows;
        AdmxCountText.Text = $"{rows.Length:N0} shown / {_admxPolicies.Count:N0} total";
    }

    private async Task ConfigureSelectedAdmxPolicyAsync()
    {
        if (_domainContext is null)
        {
            return;
        }

        if (AdmxGrid.SelectedItem is not AdmxPolicyDefinition selectedDefinition)
        {
            if (_admxPolicies is null)
            {
                await LoadAdmxCatalogIntoGridAsync();
            }

            return;
        }

        var picker = new PolicyTargetPickerWindow(_gpos, selectedDefinition)
        {
            Owner = this
        };

        if (picker.ShowDialog() != true || picker.SelectedGpo is null)
        {
            return;
        }

        var gpo = picker.SelectedGpo;
        var scope = picker.SelectedScope;
        var definition = WithEffectiveScope(selectedDefinition, scope);

        var unsupported = definition.Elements
            .Where(element => element.Type == AdmxElementType.Unknown)
            .ToArray();

        if (unsupported.Length > 0)
        {
            MessageBox.Show(
                this,
                "This ADMX policy contains an unsupported element type. Direct editing is disabled for this policy to avoid an unsafe write.",
                "Unsupported policy",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var indexed = _settings.FirstOrDefault(item =>
            item.GpoId == gpo.Id &&
            item.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase) &&
            item.SettingName.Equals(definition.DisplayName, StringComparison.CurrentCultureIgnoreCase));

        var setting = indexed ?? new PolicySettingInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            Scope = scope,
            Extension = "Administrative Templates",
            Category = definition.Category,
            SettingName = definition.DisplayName,
            State = "NotConfigured",
            Value = string.Empty,
            RegistryKey = definition.Key,
            RegistryValue = definition.ValueName
        };

        SetBusy(true, $"Reading {definition.DisplayName}...");

        try
        {
            var session = await Task.Run(() =>
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
                    $"Automatic backup before configuring '{definition.DisplayName}'"));

            StatusText.Text = "Writing policy setting...";

            await Task.Run(() =>
                _registryPolicyService.Apply(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    definition,
                    editor.Session));

            _auditService.Write(
                "Configure ADMX policy",
                "GPO",
                gpo.DisplayName,
                $"Setting: {definition.DisplayName}; Scope: {scope}; Backup: {backupPath}",
                before: $"{setting.State}; {setting.Value}",
                after: editor.Session.State.ToString());

            await RefreshSingleGpoSettingsAsync(gpo);

            StatusText.Text = $"Policy saved. Backup: {backupPath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Configure ADMX Policy",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Policy configuration failed";
        }
        finally
        {
            SetBusy(false);
        }
    }
}
