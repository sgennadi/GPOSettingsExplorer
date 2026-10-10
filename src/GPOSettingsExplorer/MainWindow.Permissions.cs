using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly GpoCapabilityService _gpoCapabilityService =
        new();

    private readonly Dictionary<Guid, GpoCapability> _gpoCapabilities =
        new();

    private CancellationTokenSource? _capabilityCancellation;

    private async void GpoGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (GpoGrid.SelectedItem
            is not GpoInfo gpo)
        {
            ApplyGpoCapabilityToMainButtons(
                null);

            FavoriteGpoButton.Content =
                "★ Favorite";

            return;
        }

        FavoriteGpoButton.Content =
            gpo.IsFavorite
                ? "★ Unfavorite"
                : "★ Favorite";

        await UpdateSelectedGpoCapabilityAsync(
            gpo);
    }

    private async Task UpdateSelectedGpoCapabilityAsync(
        GpoInfo gpo,
        bool force =
            false)
    {
        if (!force &&
            _gpoCapabilities.TryGetValue(
                gpo.Id,
                out var cached))
        {
            ApplyGpoCapabilityToMainButtons(
                cached);

            return;
        }

        var previous =
            _capabilityCancellation;

        var current =
            new CancellationTokenSource();

        _capabilityCancellation =
            current;

        try
        {
            previous?.Cancel();

            SelectedGpoAccessText.Text =
                "Checking AD and SYSVOL evidence...";

            var capability =
                await EvaluateGpoCapabilityAsync(
                    gpo,
                    current.Token);

            current.Token.ThrowIfCancellationRequested();

            if (GpoGrid.SelectedItem
                    is GpoInfo selected &&
                selected.Id ==
                gpo.Id)
            {
                _gpoCapabilities[
                    gpo.Id] =
                    capability;

                ApplyGpoCapabilityToMainButtons(
                    capability);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CrashLogService.Write(
                $"Evaluate GPO capability: {gpo.DisplayName}",
                ex);

            if (GpoGrid.SelectedItem
                    is GpoInfo selected &&
                selected.Id ==
                gpo.Id)
            {
                SelectedGpoAccessText.Text =
                    "Access: unable to evaluate";

                ApplyGpoCapabilityToMainButtons(
                    null);
            }
        }
        finally
        {
            if (ReferenceEquals(
                    _capabilityCancellation,
                    current))
            {
                _capabilityCancellation =
                    null;
            }

            current.Dispose();
            previous?.Dispose();
        }
    }

    private async Task<GpoCapability> EvaluateGpoCapabilityAsync(
        GpoInfo gpo,
        CancellationToken cancellationToken =
            default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var permissions =
            await Task.Run(
                () =>
                    _gpmService.LoadPermissions(
                        gpo.DomainName,
                        gpo.Id),
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var writeTest =
            await Task.Run(
                () =>
                    new DiagnosticsService()
                        .TestGpoWriteAccess(
                            gpo),
                cancellationToken);

        return _gpoCapabilityService.Evaluate(
            permissions,
            writeTest);
    }

    private void ApplyGpoCapabilityToMainButtons(
        GpoCapability? capability)
    {
        var safeWrite =
            EditingGuard.IsEnabled;

        var canEdit =
            safeWrite &&
            capability?.CanEditSettings ==
            true;

        var canFull =
            safeWrite &&
            capability?.CanEditSecurity ==
            true;

        RenameGpoButton.IsEnabled =
            canFull;

        DeleteGpoButton.IsEnabled =
            canFull;

        ToggleComputerButton.IsEnabled =
            canEdit;

        ToggleUserButton.IsEnabled =
            canEdit;

        AssignWmiButton.IsEnabled =
            canEdit;

        RemoveWmiButton.IsEnabled =
            canEdit;

        RenameGpoButton.ToolTip =
            PermissionToolTip(
                capability,
                requireFullControl:
                    true);

        DeleteGpoButton.ToolTip =
            PermissionToolTip(
                capability,
                requireFullControl:
                    true);

        ToggleComputerButton.ToolTip =
            PermissionToolTip(
                capability,
                requireFullControl:
                    false);

        ToggleUserButton.ToolTip =
            ToggleComputerButton.ToolTip;

        AssignWmiButton.ToolTip =
            ToggleComputerButton.ToolTip;

        RemoveWmiButton.ToolTip =
            ToggleComputerButton.ToolTip;

        SelectedGpoAccessText.Text =
            capability is null
                ? GpoGrid.SelectedItem is null
                    ? "Select a GPO to review AD and SYSVOL access evidence."
                    : "Access: unable to evaluate"
                : capability.Summary +
                  (string.IsNullOrWhiteSpace(
                       capability.Details)
                      ? string.Empty
                      : $" — {capability.Details}");
    }

    private void ApplySecurityCapability(
        GpoCapability? capability)
    {
        var enabled =
            EditingGuard.IsEnabled &&
            capability?.CanEditSecurity ==
            true;

        AddSecurityFilterButton.IsEnabled =
            enabled;

        AddDelegationButton.IsEnabled =
            enabled;

        ChangePermissionButton.IsEnabled =
            enabled;

        RemovePermissionButton.IsEnabled =
            enabled;

        var toolTip =
            PermissionToolTip(
                capability,
                requireFullControl:
                    true);

        AddSecurityFilterButton.ToolTip =
            toolTip;

        AddDelegationButton.ToolTip =
            toolTip;

        ChangePermissionButton.ToolTip =
            toolTip;

        RemovePermissionButton.ToolTip =
            toolTip;
    }

    private async Task UpdateSecurityCapabilityAsync(
        GpoInfo gpo)
    {
        try
        {
            if (!_gpoCapabilities.TryGetValue(
                    gpo.Id,
                    out var capability))
            {
                capability =
                    await EvaluateGpoCapabilityAsync(
                        gpo);

                _gpoCapabilities[
                    gpo.Id] =
                    capability;
            }

            if (SecurityGpoCombo.SelectedItem
                    is GpoInfo selected &&
                selected.Id ==
                gpo.Id)
            {
                ApplySecurityCapability(
                    capability);
            }
        }
        catch (Exception ex)
        {
            CrashLogService.Write(
                $"Evaluate GPO security capability: {gpo.DisplayName}",
                ex);

            ApplySecurityCapability(
                null);
        }
    }

    private static string PermissionToolTip(
        GpoCapability? capability,
        bool requireFullControl)
    {
        if (!EditingGuard.IsEnabled)
        {
            return "Safe mode is READ ONLY. Enable WRITE ENABLED first.";
        }

        if (capability is null)
        {
            return "AD/SYSVOL access evidence is incomplete; use native Windows ACL tools to verify.";
        }

        if (requireFullControl &&
            !capability.CanEditSecurity)
        {
            return "GPMC permission evidence does not establish AD full-control/security access for this GPO.";
        }

        if (!requireFullControl &&
            !capability.CanEditSettings)
        {
            return "Both AD edit permission evidence and writable GPT.INI are required by this guarded UI.";
        }

        return capability.Details;
    }

    private void RefreshPermissionAwareUi()
    {
        if (GpoGrid.SelectedItem
            is GpoInfo gpo)
        {
            _ =
                UpdateSelectedGpoCapabilityAsync(
                    gpo);
        }
        else
        {
            ApplyGpoCapabilityToMainButtons(
                null);
        }

        if (SecurityGpoCombo.SelectedItem
            is GpoInfo securityGpo)
        {
            _ =
                UpdateSecurityCapabilityAsync(
                    securityGpo);
        }
        else
        {
            ApplySecurityCapability(
                null);
        }
    }
}
