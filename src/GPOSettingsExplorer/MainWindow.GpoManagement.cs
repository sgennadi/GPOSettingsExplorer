using System.Windows;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private async void NewGpo_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null)
        {
            return;
        }

        var dialog = new InputDialog(
            "New GPO",
            "GPO display name:")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value))
        {
            return;
        }

        SetBusy(true, "Creating GPO...");

        try
        {
            var id = await Task.Run(() =>
                _gpmService.CreateGpo(_domainContext.DomainName, dialog.Value));

            _auditService.Write(
                "Create",
                "GPO",
                dialog.Value,
                $"GUID: {id:B}");

            await RefreshAllAsync();

            var created = _gpos.FirstOrDefault(g => g.Id == id);
            if (created is not null)
            {
                GpoGrid.SelectedItem = created;
                GpoGrid.ScrollIntoView(created);
            }

            StatusText.Text = $"Created GPO: {dialog.Value}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Create GPO",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RenameGpo_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || GpoGrid.SelectedItem is not GpoInfo selected)
        {
            return;
        }

        var dialog = new InputDialog(
            "Rename GPO",
            "New display name:",
            selected.DisplayName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Value) ||
            dialog.Value.Equals(selected.DisplayName, StringComparison.CurrentCulture))
        {
            return;
        }

        SetBusy(true, "Renaming GPO...");

        try
        {
            var oldName = selected.DisplayName;
            await Task.Run(() =>
                _gpmService.RenameGpo(
                    _domainContext.DomainName,
                    selected.Id,
                    dialog.Value));

            _auditService.Write(
                "Rename",
                "GPO",
                oldName,
                $"GUID: {selected.Id:B}",
                before: oldName,
                after: dialog.Value);

            await RefreshAllAsync();
            StatusText.Text = $"Renamed GPO: {oldName} -> {dialog.Value}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Rename GPO",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGpo_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || GpoGrid.SelectedItem is not GpoInfo selected)
        {
            return;
        }

        if (_linkTargets.Count == 0)
        {
            await LoadLinksAsync();
        }

        var links = _links.Where(link => link.GpoId == selected.Id).ToArray();
        var linkMessage = links.Length == 0
            ? string.Empty
            : $"\n\nThis GPO has {links.Length} link(s). They will be removed first.";

        if (MessageBox.Show(
                this,
                $"Delete GPO '{selected.DisplayName}'?{linkMessage}\n\nAn automatic backup will be created before deletion.",
                "Delete GPO",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "Backing up GPO before deletion...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    selected.Id,
                    "Automatic backup before GPO deletion"));

            foreach (var link in links)
            {
                StatusText.Text = $"Removing link from {link.TargetName}...";
                await Task.Run(() =>
                    _gpoLinkService.RemoveLink(link.TargetDn, selected.Id));
            }

            StatusText.Text = "Deleting GPO...";
            await Task.Run(() =>
                _gpmService.DeleteGpo(
                    _domainContext.DomainName,
                    selected.Id));

            _auditService.Write(
                "Delete",
                "GPO",
                selected.DisplayName,
                $"GUID: {selected.Id:B}; Removed links: {links.Length}; Backup: {backup}");

            for (var i = _settings.Count - 1; i >= 0; i--)
            {
                if (_settings[i].GpoId == selected.Id)
                {
                    _settings.RemoveAt(i);
                }
            }

            await RefreshAllAsync();
            if (_linkTargets.Count > 0)
            {
                await LoadLinksAsync();
            }

            StatusText.Text = $"Deleted GPO. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete GPO",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ToggleComputerScope_Click(object sender, RoutedEventArgs e)
    {
        await ToggleScopeAsync(computer: true);
    }

    private async void ToggleUserScope_Click(object sender, RoutedEventArgs e)
    {
        await ToggleScopeAsync(computer: false);
    }

    private async Task ToggleScopeAsync(bool computer)
    {
        if (_domainContext is null || GpoGrid.SelectedItem is not GpoInfo selected)
        {
            return;
        }

        var current = computer ? selected.ComputerEnabled : selected.UserEnabled;
        var newValue = !current;
        var scopeName = computer ? "Computer" : "User";
        var verb = newValue ? "enable" : "disable";

        if (MessageBox.Show(
                this,
                $"{verb} {scopeName} Configuration for '{selected.DisplayName}'?",
                $"Toggle {scopeName} Configuration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, $"Backing up before {scopeName.ToLowerInvariant()} scope change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    selected.Id,
                    $"Automatic backup before changing {scopeName} Configuration state"));

            if (computer)
            {
                await Task.Run(() =>
                    _gpmService.SetComputerEnabled(
                        _domainContext.DomainName,
                        selected.Id,
                        newValue));
            }
            else
            {
                await Task.Run(() =>
                    _gpmService.SetUserEnabled(
                        _domainContext.DomainName,
                        selected.Id,
                        newValue));
            }

            _auditService.Write(
                "Scope state",
                "GPO",
                selected.DisplayName,
                $"{scopeName} Configuration; Backup: {backup}",
                before: current.ToString(),
                after: newValue.ToString());

            await RefreshAllAsync();
            StatusText.Text = $"{scopeName} Configuration {(newValue ? "enabled" : "disabled")}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, $"Toggle {scopeName} Configuration",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }
}
