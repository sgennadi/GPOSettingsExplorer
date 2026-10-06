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
    private readonly GppRegionalOptionsService _gppRegionalOptionsService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppRegionalOptionsItemInfo>
        _gppRegionalOptions = new();

    private ICollectionView? _gppRegionalOptionsView;
    private CancellationTokenSource? _gppRegionalOptionsCancellation;
    private bool _gppRegionalOptionsInitialized;

    private void GppRegionalOptionsTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppRegionalOptionsInitialized)
            return;

        _gppRegionalOptionsInitialized = true;

        _gppRegionalOptionsView =
            CollectionViewSource.GetDefaultView(
                _gppRegionalOptions);

        _gppRegionalOptionsView.Filter =
            FilterGppRegionalOption;

        GppRegionalOptionsGrid.ItemsSource =
            _gppRegionalOptionsView;
    }

    private async void LoadGppRegionalOptions_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppRegionalOptionsAsync();
    }

    private async Task LoadGppRegionalOptionsAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppRegionalOptionsCancellation?.Cancel();
        _gppRegionalOptionsCancellation =
            new CancellationTokenSource();

        SetBusy(
            true,
            "Loading Regional Options preferences...");

        try
        {
            var progress =
                new Progress<string>(
                    message =>
                    {
                        StatusText.Text = message;
                        HeaderStatusText.Text = message;
                    });

            var rows =
                await Task.Run(
                    () =>
                        _gppRegionalOptionsService.Load(
                            _gpos,
                            progress,
                            _gppRegionalOptionsCancellation.Token));

            ReplaceCollection(
                _gppRegionalOptions,
                rows);

            _gppRegionalOptionsView?.Refresh();
            UpdateGppRegionalOptionsCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppRegionalOptions.Count:N0} Regional Options items";

            StatusText.Text =
                "Regional Options preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Regional Options loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Regional Options",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Regional Options load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppRegionalOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            _gpos.Count == 0)
            return;

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "New Regional Options Preference",
                "Select the target GPO. Regional Options are User Configuration preferences.",
                selectedScope: "User")
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        if (!target.SelectedScope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "Regional Options are supported in User Configuration. Select User scope.",
                "Regional Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var item =
            _gppRegionalOptionsService.CreateNew(
                target.SelectedGpo);

        var editor =
            new GppRegionalOptionsEditorWindow(
                item)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppRegionalOptionAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppRegionalOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppRegionalOptionAsync();
    }

    private async void GppRegionalOptionsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppRegionalOptionAsync();
    }

    private async Task EditSelectedGppRegionalOptionAsync()
    {
        if (GppRegionalOptionsGrid.SelectedItem
            is not GppRegionalOptionsItemInfo selected)
            return;

        var editable =
            CopyRegionalOptionItem(
                selected);

        var editor =
            new GppRegionalOptionsEditorWindow(
                editable)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppRegionalOptionAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppRegionalOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppRegionalOptionsGrid.SelectedItem
            is not GppRegionalOptionsItemInfo selected)
            return;

        var sourceGpo =
            _gpos.FirstOrDefault(
                gpo =>
                    gpo.Id == selected.GpoId);

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "Clone Regional Options Preference",
                $"Select the destination GPO for '{selected.DisplayName}'. Regional Options are User Configuration preferences.",
                sourceGpo,
                "User")
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        if (!target.SelectedScope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "Regional Options are supported in User Configuration. Select User scope.",
                "Clone Regional Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var clone =
            _gppRegionalOptionsService.CreateNew(
                target.SelectedGpo);

        _gppRegionalOptionsService.CopyEditableValues(
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
            new GppRegionalOptionsEditorWindow(
                clone)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppRegionalOptionAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppRegionalOptionAsync(
        GppRegionalOptionsItemInfo item,
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
                "Regional Options",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Regional Options change...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            $"Automatic backup before {action.ToLowerInvariant()} Regional Options preference"));

            await Task.Run(
                () =>
                    _gppRegionalOptionsService.Save(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        item));

            _auditService.Write(
                action,
                "GPP Regional Options",
                gpo.DisplayName,
                $"Locale: {item.LocaleName} ({item.LocaleId}); " +
                $"Time: {item.TimeFormat}; Short date: {item.ShortDateFormat}; " +
                $"Targeting: {item.HasFilters}; Backup: {backup}",
                after:
                    GppRegionalOptionSummary(
                        item));

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppRegionalOptionsAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"{action} Regional Options preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Regional Options",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Regional Options change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppRegionalOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppRegionalOptionsGrid.SelectedItem
                is not GppRegionalOptionsItemInfo selected)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Regional Options preference '{selected.DisplayName}'?\n\n" +
                $"Locale: {selected.LocaleName} ({selected.LocaleId})\n\n" +
                "A full GPO backup will be created first.",
                "Delete Regional Options Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before deleting Regional Options preference...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            "Automatic backup before deleting Regional Options preference"));

            await Task.Run(
                () =>
                    _gppRegionalOptionsService.Delete(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        selected));

            _auditService.Write(
                "Delete",
                "GPP Regional Options",
                gpo.DisplayName,
                $"Locale: {selected.LocaleName} ({selected.LocaleId}); Backup: {backup}",
                before:
                    GppRegionalOptionSummary(
                        selected),
                after:
                    "<Removed>");

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppRegionalOptionsAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"Regional Options preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Regional Options Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Regional Options delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppRegionalOptionRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppRegionalOptionsGrid.SelectedItem
            is not GppRegionalOptionsItemInfo selected)
            return;

        if (!_gppDocumentInitialized)
        {
            _gppDocumentInitialized = true;

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

            GppXmlScopeCombo.SelectedIndex = 0;

            var types =
                new[] { "All" }
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

            GppXmlTypeCombo.ItemsSource = types;
            GppXmlTypeCombo.SelectedIndex = 0;
        }

        MainTabs.SelectedItem = GppXmlTab;

        await LoadGppDocumentsAsync();

        GppXmlSearchBox.Text =
            selected.GpoName;

        GppXmlScopeCombo.SelectedItem =
            "User";

        GppXmlTypeCombo.SelectedItem =
            "Regional Options";

        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Regional Options for {selected.GpoName}";
    }

    private void GppRegionalOptionsSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppRegionalOptionsView?.Refresh();
        UpdateGppRegionalOptionsCount();
    }

    private bool FilterGppRegionalOption(
        object item)
    {
        if (item
            is not GppRegionalOptionsItemInfo regional)
            return false;

        var search =
            GppRegionalOptionsSearchBox?
                .Text?
                .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               regional.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppRegionalOptionsCount()
    {
        if (GppRegionalOptionsCountText is null)
            return;

        var shown =
            _gppRegionalOptionsView?
                .Cast<object>()
                .Count()
            ?? 0;

        GppRegionalOptionsCountText.Text =
            $"{shown:N0} shown / {_gppRegionalOptions.Count:N0} total";
    }

    private static GppRegionalOptionsItemInfo CopyRegionalOptionItem(
        GppRegionalOptionsItemInfo source) =>
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
            LocaleId = source.LocaleId,
            LocaleName = source.LocaleName,
            NumberDecimalSymbol = source.NumberDecimalSymbol,
            NumberDecimals = source.NumberDecimals,
            NumberGroupSymbol = source.NumberGroupSymbol,
            NumberGrouping = source.NumberGrouping,
            NumberNegativeSymbol = source.NumberNegativeSymbol,
            NumberNegativeFormat = source.NumberNegativeFormat,
            NumberLeadingZeros = source.NumberLeadingZeros,
            ListSeparator = source.ListSeparator,
            MeasurementSystem = source.MeasurementSystem,
            CurrencySymbol = source.CurrencySymbol,
            CurrencyPositiveFormat = source.CurrencyPositiveFormat,
            CurrencyNegativeFormat = source.CurrencyNegativeFormat,
            CurrencyDecimalSymbol = source.CurrencyDecimalSymbol,
            CurrencyDecimals = source.CurrencyDecimals,
            CurrencyGroupSymbol = source.CurrencyGroupSymbol,
            CurrencyGrouping = source.CurrencyGrouping,
            TimeFormat = source.TimeFormat,
            TimeSeparator = source.TimeSeparator,
            AmSymbol = source.AmSymbol,
            PmSymbol = source.PmSymbol,
            InterpretYearMax = source.InterpretYearMax,
            ShortDateFormat = source.ShortDateFormat,
            DateSeparator = source.DateSeparator,
            LongDateFormat = source.LongDateFormat,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext =
                source.RunInUserContext,
            FiltersXml =
                source.FiltersXml
        };

    private static string GppRegionalOptionSummary(
        GppRegionalOptionsItemInfo item) =>
        $"Locale={item.LocaleName} ({item.LocaleId}); " +
        $"Number={item.NumberDecimalSymbol}/{item.NumberGroupSymbol}; " +
        $"Currency={item.CurrencySymbol}; Time={item.TimeFormat}; " +
        $"ShortDate={item.ShortDateFormat}; LongDate={item.LongDateFormat}; " +
        $"ItemDisabled={item.Disabled}; Targeting={item.HasFilters}";
}
