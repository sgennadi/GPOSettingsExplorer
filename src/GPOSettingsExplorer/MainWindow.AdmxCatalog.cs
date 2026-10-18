using System.Windows;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private async void LoadAdmxCatalog_Click(object sender, RoutedEventArgs e)
    {
        await LoadAdmxCatalogIntoGridAsync(
            forceRefresh: true);
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

    private async Task LoadAdmxCatalogIntoGridAsync(
        bool forceRefresh = false)
    {
        if (_domainContext is null)
        {
            return;
        }

        SetBusy(true, "Loading ADMX catalog...");

        try
        {
            await EnsureAdmxCatalogAsync(
                forceRefresh);
            ApplyAdmxFilter();

            AdmxCountText.Text =
                $"{_admxPolicies?.Count ?? 0:N0} policies | {_admxCatalogService.LastLanguage} | {_admxCatalogService.LastSourcePath}";

            StatusText.Text =
                forceRefresh
                    ? "ADMX catalog refreshed and cached"
                    : "ADMX catalog loaded";
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

        await ConfigureAdmxDefinitionAsync(selectedDefinition);
    }

    // Reused by Unified Settings and by the advanced ADMX source grid.
    private async Task ConfigureAdmxDefinitionAsync(
        AdmxPolicyDefinition selectedDefinition,
        Guid? preferredGpoId = null,
        string? preferredScope = null,
        AdmxElementDefinition? preferredElement = null)
    {
        if (_domainContext is null)
            return;

        // A row already bound to a GPO must open that GPO directly.
        // The target picker is only appropriate for unbound ADMX templates.
        var gpo = preferredGpoId is Guid id
            ? _gpos.FirstOrDefault(item => item.Id == id)
            : null;
        var scope = preferredScope ?? selectedDefinition.Scope;
        if (preferredGpoId.HasValue && gpo is null)
        {
            MessageBox.Show(this,
                "The GPO associated with this setting is no longer available. " +
                "Refresh the GPO list before editing. No alternate GPO was selected.",
                "GPO not found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (gpo is null)
        {
            var picker = new PolicyTargetPickerWindow(_gpos, selectedDefinition)
            {
                Owner = this
            };
            if (picker.ShowDialog() != true || picker.SelectedGpo is null)
                return;
            gpo = picker.SelectedGpo;
            scope = picker.SelectedScope;
        }

        var definition = WithEffectiveScope(selectedDefinition, scope);

        if (preferredElement is not null &&
            !definition.Elements.Any(element =>
                ReferenceEquals(element, preferredElement)))
        {
            MessageBox.Show(this,
                "The selected field is no longer part of this ADMX policy. " +
                "Refresh the policy map before editing.",
                "Policy map changed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!UnifiedSettingsCatalogService.IsDirectAdmxSupported(definition))
        {
            MessageBox.Show(this,
                "This ADMX policy has an unsupported element type, missing/duplicate " +
                "element IDs, or multiple fields writing the same registry value. " +
                "Direct editing is blocked to avoid a wrong-field write.",
                "Unsupported or ambiguous policy",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Do not reuse a GPMC row solely because its localized label matches.
        // An index entry is trusted as this template's value only if the
        // universal resolver confirms its unique schema identity.
        var matches = _settings.Where(item =>
            item.GpoId == gpo.Id &&
            item.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase) &&
            ReferenceEquals(
                UnifiedSettingsCatalogService.ResolveDefinition(
                    item, _admxPolicies ?? Array.Empty<AdmxPolicyDefinition>()),
                selectedDefinition)).ToArray();

        var setting = matches.Length == 1 ? matches[0] : new PolicySettingInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            Scope = scope,
            Extension = "Administrative Templates",
            Category = definition.Category,
            SettingName = definition.DisplayName,
            // Absence from the GPMC index is not evidence of Not Configured.
            State = "Not reported",
            Value = string.Empty,
            RegistryKey = definition.Key,
            RegistryValue = definition.ValueName
        };

        SetBusy(true, $"Reading {definition.DisplayName}...");

        try
        {
            var session = await StaTask.Run(() =>
                _registryPolicyService.Read(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    definition,
                    setting.State));

            SetBusy(false);

            var editor = new PolicyEditorWindow(
                gpo, setting, definition, session, preferredElement)
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

            await StaTask.Run(() =>
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
