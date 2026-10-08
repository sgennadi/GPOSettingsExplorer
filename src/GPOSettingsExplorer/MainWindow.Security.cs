using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly ObservableCollection<GpoPermissionInfo> _permissions = new();
    private ICollectionView? _permissionView;

    private void InitializeSecurityUi()
    {
        SecurityGpoCombo.ItemsSource = _gpos;
        PermissionGrid.ItemsSource = _permissions;

        _permissionView = CollectionViewSource.GetDefaultView(_permissions);
        _permissionView.Filter = FilterPermission;
        PermissionGrid.ItemsSource = _permissionView;
    }

    private async void SecurityGpoCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SecurityGpoCombo.SelectedItem
            is GpoInfo selected)
        {
            await UpdateSecurityCapabilityAsync(
                selected);

            if (SecurityTab.IsSelected)
            {
                await LoadPermissionsAsync();
            }
        }
        else
        {
            ApplySecurityCapability(
                null);
        }
    }

    private async void MainTabs_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ScheduleWorkspaceControlRestore();

        if (!ReferenceEquals(
                e.OriginalSource,
                MainTabs) ||
            _domainContext is null ||
            _gpos.Count == 0)
        {
            return;
        }

        if (AllSettingsTab.IsSelected)
        {
            await EnsureSettingsIndexAsync(
                forceRebuild: false);

            return;
        }

        if (AdmxCatalogTab.IsSelected)
        {
            await EnsureAdmxCatalogAsync();
            ApplyAdmxFilter();
            return;
        }

        if (GpoScriptsTab.IsSelected)
        {
            await EnsureGpoScriptsLoadedAsync();
            return;
        }

        if (!SecurityTab.IsSelected)
        {
            return;
        }

        if (SecurityGpoCombo.SelectedItem is null)
        {
            SecurityGpoCombo.SelectedIndex = 0;
            return;
        }

        await LoadPermissionsAsync();
    }

    private async void RefreshSecurity_Click(object sender, RoutedEventArgs e)
    {
        await LoadPermissionsAsync();
    }

    private async Task LoadPermissionsAsync()
    {
        if (_domainContext is null ||
            SecurityGpoCombo.SelectedItem is not GpoInfo gpo)
        {
            _permissions.Clear();
            PermissionCountText.Text = string.Empty;
            return;
        }

        SetBusy(true, $"Loading permissions for {gpo.DisplayName}...");

        try
        {
            var permissions = await Task.Run(() =>
                _gpmService.LoadPermissions(_domainContext.DomainName, gpo.Id));

            ReplaceCollection(_permissions, permissions);
            _permissionView?.Refresh();

            var securityFilters = _permissions.Count(p =>
                p.Level == GpoPermissionLevel.Apply);
            var delegation = _permissions.Count - securityFilters;

            PermissionCountText.Text =
                $"{securityFilters:N0} filtering | {delegation:N0} delegation";
            StatusText.Text = "GPO security loaded";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load GPO Security",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "GPO security load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void AddSecurityFilter_Click(object sender, RoutedEventArgs e)
    {
        await AddPermissionAsync(
            "Add Security Filter",
            new[] { GpoPermissionLevel.Apply },
            GpoPermissionLevel.Apply);
    }

    private async void AddDelegation_Click(object sender, RoutedEventArgs e)
    {
        await AddPermissionAsync(
            "Add Delegation",
            new[]
            {
                GpoPermissionLevel.Read,
                GpoPermissionLevel.Edit,
                GpoPermissionLevel.FullControl
            },
            GpoPermissionLevel.Read);
    }

    private async Task AddPermissionAsync(
        string title,
        IEnumerable<GpoPermissionLevel> allowedLevels,
        GpoPermissionLevel defaultLevel)
    {
        if (_domainContext is null ||
            SecurityGpoCombo.SelectedItem is not GpoInfo gpo)
        {
            MessageBox.Show(
                this,
                "Select a GPO first.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var editor = new GpoPermissionEditorWindow(
            title,
            string.Empty,
            allowedLevels,
            defaultLevel,
            trusteeReadOnly: false)
        {
            Owner = this
        };

        if (editor.ShowDialog() != true)
        {
            return;
        }

        SetBusy(true, "Backing up GPO before security change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before adding {editor.SelectedLevel} permission for {editor.Trustee}"));

            await Task.Run(() =>
                _gpmService.AddPermission(
                    _domainContext.DomainName,
                    gpo.Id,
                    editor.Trustee,
                    editor.SelectedLevel));

            _auditService.Write(
                "Add permission",
                "GPO",
                gpo.DisplayName,
                $"Trustee: {editor.Trustee}; Permission: {editor.SelectedLevel}; Backup: {backup}");

            _gpoCapabilities.Remove(
                gpo.Id);

            await LoadPermissionsAsync();
            await UpdateSecurityCapabilityAsync(
                gpo);
            StatusText.Text = "GPO permission added";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ChangePermission_Click(object sender, RoutedEventArgs e)
    {
        await ChangeSelectedPermissionAsync();
    }

    private async void PermissionGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await ChangeSelectedPermissionAsync();
    }

    private async Task ChangeSelectedPermissionAsync()
    {
        if (_domainContext is null ||
            SecurityGpoCombo.SelectedItem is not GpoInfo gpo ||
            PermissionGrid.SelectedItem is not GpoPermissionInfo selected)
        {
            return;
        }

        if (selected.Inherited)
        {
            MessageBox.Show(
                this,
                "This permission is inherited and cannot be changed on this GPO.",
                "Change Permission",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (selected.Denied)
        {
            MessageBox.Show(
                this,
                "Denied permissions are shown for diagnostics but are not converted to Allow by the simplified editor. Remove the explicit deny first if that is really intended.",
                "Change Permission",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var initialLevel = selected.Level == GpoPermissionLevel.Custom
            ? GpoPermissionLevel.Read
            : selected.Level;

        var editor = new GpoPermissionEditorWindow(
            "Change GPO Permission",
            selected.TrusteeDisplay,
            new[]
            {
                GpoPermissionLevel.Apply,
                GpoPermissionLevel.Read,
                GpoPermissionLevel.Edit,
                GpoPermissionLevel.FullControl
            },
            initialLevel,
            trusteeReadOnly: true)
        {
            Owner = this
        };

        if (editor.ShowDialog() != true)
        {
            return;
        }

        if (selected.Level == editor.SelectedLevel)
        {
            return;
        }

        SetBusy(true, "Backing up GPO before permission change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before changing permission for {selected.TrusteeDisplay}"));

            await Task.Run(() =>
                _gpmService.ReplacePermission(
                    _domainContext.DomainName,
                    gpo.Id,
                    selected,
                    editor.SelectedLevel));

            _auditService.Write(
                "Change permission",
                "GPO",
                gpo.DisplayName,
                $"Trustee: {selected.TrusteeDisplay}; Backup: {backup}",
                before: selected.PermissionDisplay,
                after: editor.SelectedLevel.ToString());

            _gpoCapabilities.Remove(
                gpo.Id);

            await LoadPermissionsAsync();
            await UpdateSecurityCapabilityAsync(
                gpo);
            StatusText.Text = "GPO permission changed";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Change Permission",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RemovePermission_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            SecurityGpoCombo.SelectedItem is not GpoInfo gpo ||
            PermissionGrid.SelectedItem is not GpoPermissionInfo selected)
        {
            return;
        }

        if (selected.Inherited)
        {
            MessageBox.Show(
                this,
                "Inherited permissions cannot be removed on this GPO. Change them at the parent level.",
                "Remove Permission",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var warning = selected.Level == GpoPermissionLevel.Apply
            ? "\n\nThis changes Security Filtering and can change which users/computers apply the GPO."
            : "\n\nThis changes GPO delegation.";

        if (MessageBox.Show(
                this,
                $"Remove '{selected.PermissionDisplay}' for '{selected.TrusteeDisplay}'?{warning}\n\nA GPO backup will be created first.",
                "Remove Permission",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "Backing up GPO before permission removal...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before removing permission for {selected.TrusteeDisplay}"));

            await Task.Run(() =>
                _gpmService.RemovePermission(
                    _domainContext.DomainName,
                    gpo.Id,
                    selected));

            _auditService.Write(
                "Remove permission",
                "GPO",
                gpo.DisplayName,
                $"Trustee: {selected.TrusteeDisplay}; Backup: {backup}",
                before: selected.PermissionDisplay,
                after: "<Removed>");

            _gpoCapabilities.Remove(
                gpo.Id);

            await LoadPermissionsAsync();
            await UpdateSecurityCapabilityAsync(
                gpo);
            StatusText.Text = "GPO permission removed";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Remove Permission",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PermissionSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _permissionView?.Refresh();
    }

    private bool FilterPermission(object item)
    {
        if (item is not GpoPermissionInfo permission)
        {
            return false;
        }

        var search = PermissionSearchBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return permission.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               permission.TrusteeDisplay.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               permission.PermissionDisplay.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               permission.TrusteeSid.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               permission.TrusteeDsPath.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
