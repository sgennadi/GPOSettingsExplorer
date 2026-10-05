using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly GppLocalUsersGroupsService _gppLocalUsersGroupsService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppLocalUserInfo> _gppLocalUsers = new();
    private readonly ObservableCollection<GppLocalGroupInfo> _gppLocalGroups = new();

    private ICollectionView? _gppLocalUserView;
    private ICollectionView? _gppLocalGroupView;
    private CancellationTokenSource? _gppLocalUsersGroupsCancellation;
    private bool _gppLocalUsersGroupsInitialized;

    private void GppLocalUsersGroupsTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppLocalUsersGroupsInitialized)
            return;

        _gppLocalUsersGroupsInitialized = true;

        _gppLocalUserView =
            CollectionViewSource.GetDefaultView(
                _gppLocalUsers);
        _gppLocalUserView.Filter =
            FilterGppLocalUser;
        GppLocalUsersGrid.ItemsSource =
            _gppLocalUserView;

        _gppLocalGroupView =
            CollectionViewSource.GetDefaultView(
                _gppLocalGroups);
        _gppLocalGroupView.Filter =
            FilterGppLocalGroup;
        GppLocalGroupsGrid.ItemsSource =
            _gppLocalGroupView;

        GppLocalUsersScopeCombo.ItemsSource =
            new[]
            {
                "All",
                "Computer",
                "User"
            };
        GppLocalUsersScopeCombo.SelectedIndex =
            0;

        GppLocalGroupsScopeCombo.ItemsSource =
            new[]
            {
                "All",
                "Computer",
                "User"
            };
        GppLocalGroupsScopeCombo.SelectedIndex =
            0;
    }

    private async void LoadGppLocalUsersGroups_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppLocalUsersGroupsAsync();
    }

    private async Task LoadGppLocalUsersGroupsAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppLocalUsersGroupsCancellation?.Cancel();
        _gppLocalUsersGroupsCancellation =
            new CancellationTokenSource();

        SetBusy(
            true,
            "Loading Local Users and Groups preferences...");

        try
        {
            var progress =
                new Progress<string>(
                    message =>
                    {
                        StatusText.Text =
                            message;
                        HeaderStatusText.Text =
                            message;
                    });

            var token =
                _gppLocalUsersGroupsCancellation.Token;

            var users =
                await Task.Run(
                    () =>
                        _gppLocalUsersGroupsService.LoadUsers(
                            _gpos,
                            progress,
                            token));

            token.ThrowIfCancellationRequested();

            var groups =
                await Task.Run(
                    () =>
                        _gppLocalUsersGroupsService.LoadGroups(
                            _gpos,
                            progress,
                            token));

            ReplaceCollection(
                _gppLocalUsers,
                users);

            ReplaceCollection(
                _gppLocalGroups,
                groups);

            _gppLocalUserView?.Refresh();
            _gppLocalGroupView?.Refresh();

            UpdateGppLocalUserCount();
            UpdateGppLocalGroupCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppLocalUsers.Count:N0} Local Users | {_gppLocalGroups.Count:N0} Local Groups";

            StatusText.Text =
                "Local Users and Groups preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Local Users and Groups loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Local Users and Groups",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Local Users and Groups load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppLocalUser_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            _gpos.Count == 0)
            return;

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "New Local User",
                "Select the target GPO and scope for this Local User preference:",
                selectedScope: "Computer")
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var item =
            _gppLocalUsersGroupsService.CreateNewUser(
                target.SelectedGpo,
                target.SelectedScope);

        var editor =
            new GppLocalUserEditorWindow(
                item)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppLocalUserAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppLocalUser_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppLocalUserAsync();
    }

    private async void GppLocalUsersGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppLocalUserAsync();
    }

    private async Task EditSelectedGppLocalUserAsync()
    {
        if (GppLocalUsersGrid.SelectedItem
            is not GppLocalUserInfo selected)
            return;

        var editable =
            CopyLocalUser(
                selected);

        var editor =
            new GppLocalUserEditorWindow(
                editable)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppLocalUserAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppLocalUser_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppLocalUsersGrid.SelectedItem
            is not GppLocalUserInfo selected)
            return;

        var sourceGpo =
            _gpos.FirstOrDefault(
                gpo =>
                    gpo.Id == selected.GpoId);

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "Clone Local User",
                "Select the destination GPO and scope for the cloned Local User preference:",
                sourceGpo,
                selected.Scope)
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        if (selected.HasStoredCredential)
        {
            MessageBox.Show(
                this,
                "The source Local User preference contains legacy GPP cpassword data. " +
                "The cloned item will not copy that credential. GPO Settings Explorer never decrypts, displays, or creates cpassword values.",
                "Clone Local User",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var clone =
            _gppLocalUsersGroupsService.CreateNewUser(
                target.SelectedGpo,
                target.SelectedScope);

        _gppLocalUsersGroupsService.CopyUserValues(
            selected,
            clone,
            preserveCredential: false);

        clone.Uid =
            Guid.NewGuid()
                .ToString("B")
                .ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(
                clone.DisplayName))
        {
            clone.DisplayName +=
                " - Copy";
        }

        var editor =
            new GppLocalUserEditorWindow(
                clone)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppLocalUserAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppLocalUserAsync(
        GppLocalUserInfo item,
        string action)
    {
        if (_domainContext is null)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == item.GpoId);

        if (gpo is null)
        {
            MessageBox.Show(
                this,
                "The target GPO no longer exists. Refresh the GPO list.",
                "Local User",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Local User change...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            $"Automatic backup before {action.ToLowerInvariant()} Local User preference"));

            await Task.Run(
                () =>
                    _gppLocalUsersGroupsService.SaveUser(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        item));

            _auditService.Write(
                action,
                "GPP Local User",
                gpo.DisplayName,
                $"Scope: {item.Scope}; User: {item.UserName}; " +
                $"Action: {item.ActionDisplay}; Rename: {item.NewName}; " +
                $"Stored credential preserved: {item.HasStoredCredential && !item.ClearStoredCredential}; " +
                $"Targeting: {item.HasFilters}; Backup: {backup}",
                after:
                    GppLocalUserSummary(
                        item));

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppLocalUsersGroupsAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"{action} Local User preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Local User",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Local User change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppLocalUser_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppLocalUsersGrid.SelectedItem
                is not GppLocalUserInfo selected)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Local User preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ActionDisplay}: {selected.UserName}\n\n" +
                "This removes the preference item from the GPO. A full GPO backup will be created first.",
                "Delete Local User Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before deleting Local User preference...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            "Automatic backup before deleting Local User preference"));

            await Task.Run(
                () =>
                    _gppLocalUsersGroupsService.DeleteUser(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        selected));

            _auditService.Write(
                "Delete",
                "GPP Local User",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; User: {selected.UserName}; Backup: {backup}",
                before:
                    GppLocalUserSummary(
                        selected),
                after:
                    "<Removed>");

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppLocalUsersGroupsAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"Local User preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Local User Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Local User preference delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppLocalGroup_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            _gpos.Count == 0)
            return;

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "New Local Group",
                "Select the target GPO and scope for this Local Group preference:",
                selectedScope: "Computer")
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var item =
            _gppLocalUsersGroupsService.CreateNewGroup(
                target.SelectedGpo,
                target.SelectedScope);

        var editor =
            new GppLocalGroupEditorWindow(
                item)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppLocalGroupAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppLocalGroup_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppLocalGroupAsync();
    }

    private async void GppLocalGroupsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppLocalGroupAsync();
    }

    private async Task EditSelectedGppLocalGroupAsync()
    {
        if (GppLocalGroupsGrid.SelectedItem
            is not GppLocalGroupInfo selected)
            return;

        var editable =
            CopyLocalGroup(
                selected);

        var editor =
            new GppLocalGroupEditorWindow(
                editable)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppLocalGroupAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppLocalGroup_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppLocalGroupsGrid.SelectedItem
            is not GppLocalGroupInfo selected)
            return;

        var sourceGpo =
            _gpos.FirstOrDefault(
                gpo =>
                    gpo.Id == selected.GpoId);

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "Clone Local Group",
                "Select the destination GPO and scope for the cloned Local Group preference:",
                sourceGpo,
                selected.Scope)
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var clone =
            _gppLocalUsersGroupsService.CreateNewGroup(
                target.SelectedGpo,
                target.SelectedScope);

        _gppLocalUsersGroupsService.CopyGroupValues(
            selected,
            clone);

        clone.Uid =
            Guid.NewGuid()
                .ToString("B")
                .ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(
                clone.DisplayName))
        {
            clone.DisplayName +=
                " - Copy";
        }

        var editor =
            new GppLocalGroupEditorWindow(
                clone)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppLocalGroupAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppLocalGroupAsync(
        GppLocalGroupInfo item,
        string action)
    {
        if (_domainContext is null)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == item.GpoId);

        if (gpo is null)
        {
            MessageBox.Show(
                this,
                "The target GPO no longer exists. Refresh the GPO list.",
                "Local Group",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Local Group change...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            $"Automatic backup before {action.ToLowerInvariant()} Local Group preference"));

            await Task.Run(
                () =>
                    _gppLocalUsersGroupsService.SaveGroup(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        item));

            _auditService.Write(
                action,
                "GPP Local Group",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Group: {item.GroupName}; SID: {item.GroupSid}; " +
                $"Action: {item.ActionDisplay}; Members: {item.Members.Count}; " +
                $"Delete all users: {item.DeleteAllUsers}; Delete all groups: {item.DeleteAllGroups}; " +
                $"Targeting: {item.HasFilters}; Backup: {backup}",
                after:
                    GppLocalGroupSummary(
                        item));

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppLocalUsersGroupsAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"{action} Local Group preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Local Group",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Local Group change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppLocalGroup_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppLocalGroupsGrid.SelectedItem
                is not GppLocalGroupInfo selected)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Local Group preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ActionDisplay}: {selected.GroupName}\n" +
                $"Members in preference: {selected.Members.Count}\n\n" +
                "This removes the preference item from the GPO. A full GPO backup will be created first.",
                "Delete Local Group Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before deleting Local Group preference...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            "Automatic backup before deleting Local Group preference"));

            await Task.Run(
                () =>
                    _gppLocalUsersGroupsService.DeleteGroup(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        selected));

            _auditService.Write(
                "Delete",
                "GPP Local Group",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Group: {selected.GroupName}; Backup: {backup}",
                before:
                    GppLocalGroupSummary(
                        selected),
                after:
                    "<Removed>");

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppLocalUsersGroupsAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"Local Group preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Local Group Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Local Group preference delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppLocalUserRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppLocalUsersGrid.SelectedItem
            is not GppLocalUserInfo selected)
            return;

        await ShowGppLocalUsersGroupsRawXmlAsync(
            selected.GpoName,
            selected.Scope);
    }

    private async void ShowGppLocalGroupRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppLocalGroupsGrid.SelectedItem
            is not GppLocalGroupInfo selected)
            return;

        await ShowGppLocalUsersGroupsRawXmlAsync(
            selected.GpoName,
            selected.Scope);
    }

    private async Task ShowGppLocalUsersGroupsRawXmlAsync(
        string gpoName,
        string scope)
    {
        if (!_gppDocumentInitialized)
        {
            _gppDocumentInitialized =
                true;

            _gppDocumentView =
                CollectionViewSource.GetDefaultView(
                    _gppDocuments);

            _gppDocumentView.Filter =
                FilterGppDocument;

            GppXmlGrid.ItemsSource =
                _gppDocumentView;

            GppXmlScopeCombo.ItemsSource =
                new[]
                {
                    "All",
                    "Computer",
                    "User"
                };

            GppXmlScopeCombo.SelectedIndex =
                0;

            var types =
                new[]
                {
                    "All"
                }
                .Concat(
                    _gppDocumentService
                        .GetKnownTypes()
                        .Select(
                            type =>
                                type.Name))
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(
                    name =>
                        name.Equals(
                            "All",
                            StringComparison.OrdinalIgnoreCase)
                            ? string.Empty
                            : name)
                .ToArray();

            GppXmlTypeCombo.ItemsSource =
                types;

            GppXmlTypeCombo.SelectedIndex =
                0;
        }

        MainTabs.SelectedItem =
            GppXmlTab;

        await LoadGppDocumentsAsync();

        GppXmlSearchBox.Text =
            gpoName;

        GppXmlScopeCombo.SelectedItem =
            scope;

        GppXmlTypeCombo.SelectedItem =
            "Local Users and Groups";

        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Local Users and Groups for {gpoName}";
    }

    private void GppLocalUsersSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppLocalUserView?.Refresh();
        UpdateGppLocalUserCount();
    }

    private void GppLocalUsersScopeCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppLocalUserView?.Refresh();
        UpdateGppLocalUserCount();
    }

    private bool FilterGppLocalUser(
        object item)
    {
        if (item
            is not GppLocalUserInfo user)
            return false;

        var scope =
            Convert.ToString(
                GppLocalUsersScopeCombo?.SelectedItem);

        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !user.Scope.Equals(
                scope,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var search =
            GppLocalUsersSearchBox?
                .Text?
                .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               user.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppLocalUserCount()
    {
        if (GppLocalUsersCountText is null)
            return;

        var shown =
            _gppLocalUserView?
                .Cast<object>()
                .Count()
            ?? 0;

        GppLocalUsersCountText.Text =
            $"{shown:N0} shown / {_gppLocalUsers.Count:N0} total";
    }

    private void GppLocalGroupsSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppLocalGroupView?.Refresh();
        UpdateGppLocalGroupCount();
    }

    private void GppLocalGroupsScopeCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppLocalGroupView?.Refresh();
        UpdateGppLocalGroupCount();
    }

    private bool FilterGppLocalGroup(
        object item)
    {
        if (item
            is not GppLocalGroupInfo group)
            return false;

        var scope =
            Convert.ToString(
                GppLocalGroupsScopeCombo?.SelectedItem);

        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !group.Scope.Equals(
                scope,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var search =
            GppLocalGroupsSearchBox?
                .Text?
                .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               group.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppLocalGroupCount()
    {
        if (GppLocalGroupsCountText is null)
            return;

        var shown =
            _gppLocalGroupView?
                .Cast<object>()
                .Count()
            ?? 0;

        GppLocalGroupsCountText.Text =
            $"{shown:N0} shown / {_gppLocalGroups.Count:N0} total";
    }

    private static GppLocalUserInfo CopyLocalUser(
        GppLocalUserInfo source) =>
        new()
        {
            GpoId = source.GpoId,
            GpoName = source.GpoName,
            DomainName = source.DomainName,
            Scope = source.Scope,
            XmlPath = source.XmlPath,
            Uid = source.Uid,
            Ordinal = source.Ordinal,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Action = source.Action,
            UserName = source.UserName,
            NewName = source.NewName,
            FullName = source.FullName,
            AccountDescription = source.AccountDescription,
            ChangePasswordAtLogon =
                source.ChangePasswordAtLogon,
            UserCannotChangePassword =
                source.UserCannotChangePassword,
            PasswordNeverExpires =
                source.PasswordNeverExpires,
            AccountDisabled =
                source.AccountDisabled,
            Expires = source.Expires,
            OpaqueCredential =
                source.OpaqueCredential,
            ClearStoredCredential =
                false,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext =
                source.RunInUserContext,
            FiltersXml = source.FiltersXml
        };

    private static GppLocalGroupInfo CopyLocalGroup(
        GppLocalGroupInfo source) =>
        new()
        {
            GpoId = source.GpoId,
            GpoName = source.GpoName,
            DomainName = source.DomainName,
            Scope = source.Scope,
            XmlPath = source.XmlPath,
            Uid = source.Uid,
            Ordinal = source.Ordinal,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Action = source.Action,
            GroupName = source.GroupName,
            GroupSid = source.GroupSid,
            NewName = source.NewName,
            GroupDescription =
                source.GroupDescription,
            CurrentUserAction =
                source.CurrentUserAction,
            DeleteAllUsers =
                source.DeleteAllUsers,
            DeleteAllGroups =
                source.DeleteAllGroups,
            RemoveAccounts =
                source.RemoveAccounts,
            PropertiesDisabled =
                source.PropertiesDisabled,
            Members =
                source.Members
                    .Select(
                        member =>
                            member.Clone())
                    .ToList(),
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext =
                source.RunInUserContext,
            FiltersXml = source.FiltersXml
        };

    private static string GppLocalUserSummary(
        GppLocalUserInfo item) =>
        $"Scope={item.Scope}; Action={item.ActionDisplay}; " +
        $"User={item.UserName}; NewName={item.NewName}; FullName={item.FullName}; " +
        $"AccountDisabled={item.AccountDisabled}; PasswordNeverExpires={item.PasswordNeverExpires}; " +
        $"LegacyCredentialPresent={item.HasStoredCredential}; Disabled={item.Disabled}; " +
        $"Targeting={item.HasFilters}";

    private static string GppLocalGroupSummary(
        GppLocalGroupInfo item) =>
        $"Scope={item.Scope}; Action={item.ActionDisplay}; " +
        $"Group={item.GroupName}; SID={item.GroupSid}; NewName={item.NewName}; " +
        $"Members={item.Members.Count}; DeleteAllUsers={item.DeleteAllUsers}; " +
        $"DeleteAllGroups={item.DeleteAllGroups}; CurrentUserAction={item.CurrentUserAction}; " +
        $"Disabled={item.Disabled}; Targeting={item.HasFilters}";
}
