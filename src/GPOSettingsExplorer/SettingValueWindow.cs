using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public sealed class SettingValueWindow : Window
{
    private readonly ComboBox? _booleanCombo;

    public bool? SelectedBooleanValue =>
        _booleanCombo?.SelectedItem is bool value
            ? value
            : null;

    public SettingValueWindow(
        GpoInfo gpo,
        PolicySettingInfo setting,
        bool canEditBoolean,
        bool exactNavigationAvailable,
        Func<IProgress<string>, Task<bool>> openExactGpoEditor,
        Func<IProgress<string>, Task<bool>>? discoverExactMmcRoute = null,
        Action? cancelExactDiscovery = null,
        Func<IProgress<string>, CancellationToken,
            Task<GpoKerberosStoredSourceEvidenceService.Result>>?
            verifyStoredKerberos = null,
        Func<Task>? openGpmcReport = null,
        Action? openManualGpoEditor = null)
    {
        Title =
            $"Setting Value - {setting.SettingName}";

        Width = 760;
        Height =
            canEditBoolean
                ? 360
                : 430;

        MinWidth = 540;
        MinHeight =
            canEditBoolean
                ? 310
                : 350;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        UiStyle.ApplyWindowDefaults(this);

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(12)
            };

        var footer =
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        DockPanel.SetDock(
            footer,
            Dock.Bottom);

        var verifiedSectionPath = GpoEditorNavigatorService.NavigationTarget(setting);
        var hasVerifiedSection = GpoEditorNavigatorService.HasVerifiedSection(setting);
        var manualSecuritySection =
            SecurityXmlEntryClassifier.InferLegacyCategory(setting);
        var securitySectionLabel = manualSecuritySection switch
        {
            SecurityXmlEntryClassifier.RegistrySection => "Registry (manual section)",
            SecurityXmlEntryClassifier.RestrictedGroupsSection => "Restricted Groups (manual section)",
            _ => null
        };

        var navigationStatus =
            new TextBlock
            {
                Text =
                    exactNavigationAvailable
                        ? $"Known MMC section: {verifiedSectionPath}. " +
                          "A unique native row must be observed when opening Properties."
                        : !hasVerifiedSection
                            ? "No verified MMC section is mapped for this GPMC source. " +
                              "The full GPO editor can be opened for manual inspection."
                        : securitySectionLabel is not null
                            ? "Read-only XML detail. Related MMC section (manual navigation only): " +
                              GpoEditorNavigatorService.NavigationTarget(setting)
                        : setting.Extension.Equals(
                            "RegistrySettings",
                            StringComparison.OrdinalIgnoreCase) &&
                          setting.Value.Contains(
                              "AdmSetting=false",
                              StringComparison.OrdinalIgnoreCase)
                            ? "This is an Extra Registry Setting from registry.pol (AdmSetting=false), not a Group Policy Preferences Registry item. There is no deterministic row for it in the standard GPO editor."
                            : "The standard GPO editor will be opened.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground = UiStyle.MutedBrush,
                Margin =
                    new Thickness(
                        4,
                        0,
                        4,
                        8)
            };

        var open =
            new Button
            {
                Content =
                    exactNavigationAvailable
                        ? "Open exact setting in GPO editor..."
                        : !hasVerifiedSection
                            ? "Open full GPO editor (manual)..."
                        : securitySectionLabel is not null
                            ? "Open " + securitySectionLabel + "..."
                        : setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase)
                            ? "Open Administrative Templates (manual)..."
                            : setting.Extension.Equals("SoftwareInstallationSettings", StringComparison.OrdinalIgnoreCase)
                                ? "Open Software installation (manual)..."
                                : setting.Extension.Equals("PublicKeySettings", StringComparison.OrdinalIgnoreCase)
                                    ? "Open Public Key Policies (manual)..."
                                    : setting.Extension.Equals("NrptSettings", StringComparison.OrdinalIgnoreCase)
                                        ? "Open Name Resolution Policy (manual)..."
                                        : "Open related GPO editor section..."
            };

        open.Click += async (_, _) =>
        {
            var originalContent = open.Content;

            open.IsEnabled =
                false;

            open.Content =
                "Opening...";

            try
            {
                navigationStatus.Text =
                    "Starting Group Policy Management Editor...";

                var progress =
                    new Progress<string>(
                        message =>
                            navigationStatus.Text =
                                message);

                var exact =
                    await openExactGpoEditor(
                        progress);

                // A detached MMC window is not a successful edit/save.
                // Keep the value and registry details available until the
                // operator explicitly clicks Close, regardless of result.
                if (exact)
                {
                    navigationStatus.Foreground = UiStyle.SuccessBrush;
                    navigationStatus.Text =
                        "A unique MMC policy row was selected and its Properties were requested. " +
                        "Check whether the native MMC dialog appeared; no GPO change was made.";
                    return;
                }

                if (!exactNavigationAvailable)
                {
                    navigationStatus.Foreground = UiStyle.WarningBrush;
                    var sectionPath = GpoEditorNavigatorService.NavigationTarget(setting);
                    navigationStatus.Text = !hasVerifiedSection
                        ? "The GPO editor was opened at its root; no verified section exists for this extension. Navigate manually. No policy row was selected."
                        : setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase)
                            ? "MMC opened for Administrative Templates. This registry.pol entry may not have an ADMX editor. Value retained below."
                            : $"MMC section navigation requested: {sectionPath}. Verify the correct section is visible; exact row editing is unavailable for this report entry.";
                    return;
                }

                navigationStatus.Foreground = UiStyle.WarningBrush;

                MessageBox.Show(
                    this,
                    setting.Extension.Equals(
                        "RegistrySettings",
                        StringComparison.OrdinalIgnoreCase)
                        ? "A raw registry.pol entry cannot be opened as a Group Policy Preferences item. The corresponding ADMX definition may be missing. The value remains visible in this window."
                        : "The expected MMC section was opened, but a unique exact native row could not be confirmed or activated. " +
                          "For Kerberos, the typed GPMC Account name must match the native policy label; " +
                          "for an SRP Path Rule, the exact Path value must match exactly one Additional Rules row. " +
                          "No substitute row was selected. Check permissions, snap-in availability and the navigation log in " +
                          "%LOCALAPPDATA%\\GPOSettingsExplorer\\Logs.",
                    "GPO Editor Navigation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                navigationStatus.Text =
                    "GPO editor navigation failed.";

                ErrorDialog.Show(
                    this,
                    "Open GPO Editor",
                    $"Unable to navigate to: {GpoEditorNavigatorService.NavigationTarget(setting)}",
                    ex);
            }
            finally
            {
                if (IsLoaded)
                {
                    open.Content =
                        originalContent;

                    open.IsEnabled =
                        true;
                }
            }
        };

        footer.Children.Add(open);

        // Manual GPO editor: no implicit native MMC inventory or row guess.
        if (openManualGpoEditor is not null)
        {
            var manual = new Button
            {
                Content = "Open GPO editor (manual)",
                ToolTip = "Launch selected GPO at its root without scanning native MMC. " +
                    "Manual changes in MMC may modify this domain GPO."
            };
            manual.Click += (_, _) =>
            {
                try
                {
                    openManualGpoEditor();
                    navigationStatus.Foreground = UiStyle.WarningBrush;
                    navigationStatus.Text =
                        "Native GPO editor opened at its root. Select a policy manually. " +
                        "Exact row, stored source value and effective client RSoP are NOT verified.";
                }
                catch (Exception ex)
                {
                    navigationStatus.Foreground = UiStyle.ErrorBrush;
                    navigationStatus.Text = "Manual GPO editor failed: " + ex.Message;
                    CrashLogService.Write("Manual GPO editor from Setting Value", ex);
                }
            };
            footer.Children.Add(manual);
        }

        // One GPMC policy may recheck existing, non-aborted MMC evidence.
        // Never start a full native MMC traversal from this small value dialog:
        // an unrelated snap-in can crash while looking for the selected item.
        if (discoverExactMmcRoute is not null &&
            !SecurityXmlEntryClassifier.IsTechnicalDetail(setting))
        {
            var discover = new Button
            {
                Content = "Check captured MMC route...",
                ToolTip = "Uses only a previously captured, non-aborted MMC snapshot " +
                    "for this same GPO/DC. To capture, close this value window and " +
                    "run Scan MMC (read-only) from MMC Full Settings Inventory. " +
                    "No hidden broad snap-in scan and no RSoP inference."
            };
            var cancelScan = new Button
            {
                Content = "Cancel MMC scan",
                IsEnabled = false,
                ToolTip = "Cancel the current read-only native inventory of this GPO."
            };
            cancelScan.Click += (_, _) =>
            {
                cancelExactDiscovery?.Invoke();
                navigationStatus.Text = "Cancellation requested for the current MMC scan...";
            };
            discover.Click += async (_, _) =>
            {
                discover.IsEnabled = false;
                open.IsEnabled = false;
                cancelScan.IsEnabled = cancelExactDiscovery is not null;
                var original = discover.Content;
                discover.Content = "Discovering...";
                try
                {
                    navigationStatus.Foreground = UiStyle.WarningBrush;
                    navigationStatus.Text = "Checking existing safe MMC evidence for this GPO. " +
                        "No background native inventory will be started.";
                    var progress = new Progress<string>(message =>
                    {
                        navigationStatus.Text = message;
                        navigationStatus.Foreground = UiStyle.WarningBrush;
                    });
                    var succeeded = await discoverExactMmcRoute(progress);
                    if (succeeded)
                    {
                        navigationStatus.Foreground = UiStyle.SuccessBrush;
                        navigationStatus.Text =
                            "The selected native MMC row was rechecked in a fresh editor. " +
                            "Inspect its Properties window. This does NOT verify the stored " +
                            "policy value or effective RSoP.";
                    }
                    else
                    {
                        navigationStatus.Foreground = UiStyle.WarningBrush;
                        navigationStatus.Text =
                            "No uniquely verified MMC row was opened. See the MMC Inventory " +
                            "coverage / skipped sections for the exact reason. " +
                            "No substitute row or GPO value was changed.";
                    }
                }
                catch (Exception ex)
                {
                    navigationStatus.Foreground = UiStyle.ErrorBrush;
                    navigationStatus.Text = "Exact-path discovery failed: " + ex.Message;
                    CrashLogService.Write("Exact MMC route discovery", ex);
                }
                finally
                {
                    if (IsLoaded)
                    {
                        discover.IsEnabled = true;
                        discover.Content = original;
                        open.IsEnabled = true;
                        cancelScan.IsEnabled = false;
                    }
                }
            };
            footer.Children.Add(discover);
            if (cancelExactDiscovery is not null)
                footer.Children.Add(cancelScan);
        }

        // On-demand, read-only typed Kerberos source verification. This is
        // intentionally distinct from exact MMC navigation: agreement of
        // two stored snapshots must never be labeled as applied client RSoP,
        // a verified native MMC value, or an authorized policy change.
        TextBlock? kerberosSourceStatus = null;
        if (verifyStoredKerberos is not null)
        {
            var verifier = verifyStoredKerberos;
            kerberosSourceStatus = new TextBlock
            {
                Text = "Stored Kerberos source: NOT CHECKED. " +
                    "Click Verify to read only this GPO's pinned-DC GptTmpl.inf " +
                    "and compare its typed value with the cached GPMC report.",
                Foreground = UiStyle.WarningBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 8)
            };
            var sourceStatus = kerberosSourceStatus;
            var verifyButton = new Button
            {
                Content = "Verify stored Kerberos source...",
                ToolTip = "On demand: read-only, single-GPO, pinned-DC SecEdit " +
                    "GptTmpl.inf SHA-256 comparison. Does not start MMC, " +
                    "write a GPO or establish effective RSoP."
            };
            var cancelVerify = new Button
            {
                Content = "Cancel source check",
                IsEnabled = false
            };
            CancellationTokenSource? activeRead = null;
            Closed += (_, _) => activeRead?.Cancel();
            cancelVerify.Click += (_, _) =>
            {
                activeRead?.Cancel();
                if (IsLoaded)
                    sourceStatus.Text = "Cancelling the security-template source read...";
            };
            verifyButton.Click += async (_, _) =>
            {
                if (activeRead is not null)
                    return;
                var operation = new CancellationTokenSource();
                activeRead = operation;
                var reading = true;
                verifyButton.IsEnabled = false;
                cancelVerify.IsEnabled = true;
                open.IsEnabled = false;
                sourceStatus.Foreground = UiStyle.WarningBrush;
                sourceStatus.Text = "Reading pinned Kerberos security-template source (read-only)...";
                try
                {
                    var progress = new Progress<string>(message =>
                    {
                        if (IsLoaded && reading)
                            sourceStatus.Text = message;
                    });
                    var result = await verifier(progress, operation.Token);
                    reading = false;
                    if (!IsLoaded)
                        return;
                    sourceStatus.Foreground =
                        result.Status == GpoKerberosStoredSourceEvidenceService.Match
                            ? UiStyle.SuccessBrush : UiStyle.WarningBrush;
                    var literals = result.IsCompared
                        ? " Cached GPMC literal: " + result.GpmcLiteral +
                          "; pinned GptTmpl.inf literal: " + result.InfLiteral + "."
                        : string.Empty;
                    sourceStatus.Text = "Stored Kerberos source: " + result.Status +
                        ". " + result.Explanation + literals +
                        " This is NOT native MMC value verification or client RSoP.";
                }
                catch (OperationCanceledException)
                {
                    reading = false;
                    if (IsLoaded)
                    {
                        sourceStatus.Foreground = UiStyle.WarningBrush;
                        sourceStatus.Text =
                            "Stored Kerberos source: CANCELED. No stored-value " +
                            "agreement was established.";
                    }
                }
                catch (Exception ex)
                {
                    reading = false;
                    CrashLogService.Write("Read-only Kerberos source check", ex);
                    if (IsLoaded)
                    {
                        sourceStatus.Foreground = UiStyle.ErrorBrush;
                        sourceStatus.Text = "Stored Kerberos source: READ_FAILED. " +
                            ex.GetType().Name + ": " + ex.Message +
                            ". No source value was verified.";
                    }
                }
                finally
                {
                    reading = false;
                    if (IsLoaded)
                    {
                        verifyButton.IsEnabled = true;
                        cancelVerify.IsEnabled = false;
                        open.IsEnabled = true;
                    }
                    activeRead = null;
                    operation.Dispose();
                }
            };
            footer.Children.Add(verifyButton);
            footer.Children.Add(cancelVerify);
        }

        // The official GPMC Settings view is generated from the source XML.
        // Open the report for this setting's GPO, not the possibly unrelated
        // GPO currently selected in the main grid.
        if (openGpmcReport is not null)
        {
            var sourceReport = new Button
            {
                Content = GpmcRegistryReportLocator.TryCreate(setting, out _)
                    ? "Locate exact GPP item in GPMC report..."
                    : "View GPMC Settings report...",
                ToolTip = "Show the source hierarchy for this exact GPO. Known Registry " +
                    "items are located by UID/order/action/hive/key/value; " +
                    "never guess by display name. No local registry edits."
            };
            sourceReport.Click += async (_, _) =>
            {
                sourceReport.IsEnabled = false;
                try { await openGpmcReport(); }
                catch (Exception ex)
                {
                    CrashLogService.Write("GPMC Settings report", ex);
                    ErrorDialog.Show(this, "GPMC Settings report",
                        "Unable to display this GPO's original report.", ex);
                }
                finally { if (IsLoaded) sourceReport.IsEnabled = true; }
            };
            footer.Children.Add(sourceReport);
        }

        // A raw registry.pol entry has no guaranteed MMC row. Make its real
        // key/value easy to inspect without opening the live local registry.
        if (GpoSettingSourceProvenance.IsVerifiedRegistryTarget(setting))
        {
            var copyKey = new Button
            {
                Content = "Copy registry key",
                ToolTip = "Copy the registry.pol key path; this does not alter the GPO."
            };
            copyKey.Click += (_, _) => Clipboard.SetText(setting.RegistryKey);
            footer.Children.Add(copyKey);
        }

        if (GpoSettingSourceProvenance.IsVerifiedRegistryTarget(setting) &&
            !string.IsNullOrWhiteSpace(setting.RegistryValue))
        {
            var copyValueName = new Button { Content = "Copy value name" };
            copyValueName.Click += (_, _) => Clipboard.SetText(setting.RegistryValue);
            footer.Children.Add(copyValueName);
        }

        if (GpoSettingSourceProvenance.IsGppRegistrySource(setting))
        {
            var copyGppKey = new Button
            {
                Content = "Copy GPP registry key",
                ToolTip = "Copy the stored preference Hive + Key path. " +
                    "Does not read or modify local registry."
            };
            copyGppKey.Click += (_, _) => Clipboard.SetText(
                setting.GppRegistryHive + "\\" + setting.GppRegistryKeyPath);
            footer.Children.Add(copyGppKey);
            if (!string.IsNullOrWhiteSpace(setting.GppRegistryValueName))
            {
                var copyGppValue = new Button
                {
                    Content = "Copy GPP value name",
                    ToolTip = "Value name only; not a verified live client value."
                };
                copyGppValue.Click += (_, _) =>
                    Clipboard.SetText(setting.GppRegistryValueName);
                footer.Children.Add(copyGppValue);
            }
        }

        if (canEditBoolean &&
            bool.TryParse(
                setting.Value,
                out var currentBoolean))
        {
            _booleanCombo =
                new ComboBox
                {
                    ItemsSource =
                        new object[]
                        {
                            true,
                            false
                        },
                    SelectedItem =
                        currentBoolean,
                    MinWidth = 120
                };

            var apply =
                new Button
                {
                    Content = "Apply",
                    IsDefault = true
                };

            apply.Click += (_, _) =>
            {
                DialogResult = true;
            };

            footer.Children.Add(
                apply);
        }

        footer.Children.Add(
            new Button
            {
                Content = "Close",
                IsCancel = true,
                ToolTip = "Only this button (or Escape) closes the value window."
            });

        var panel =
            new StackPanel();

        panel.Children.Add(
            navigationStatus);
        if (kerberosSourceStatus is not null)
            panel.Children.Add(kerberosSourceStatus);

        panel.Children.Add(
            new TextBlock
            {
                Text = setting.SettingName,
                FontSize = UiStyle.HeadingFontSize,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        4,
                        0,
                        4,
                        8)
            });

        panel.Children.Add(
            Meta(
                "GPO",
                gpo.DisplayName));

        panel.Children.Add(
            Meta(
                "Category",
                KerberosPolicyMetadataService.IsKnownKerberosSetting(setting)
                    ? KerberosPolicyMetadataService.Category
                    : manualSecuritySection ?? setting.Category));

        panel.Children.Add(Meta("Source type",
            GpoSettingSourceProvenance.SourceType(setting)));
        if (GpoSettingSourceProvenance.IsGppRegistrySource(setting))
        {
            panel.Children.Add(Meta("GPP Action", setting.GppRegistryAction));
            panel.Children.Add(Meta("Hive", setting.GppRegistryHive));
            panel.Children.Add(Meta("Key path", setting.GppRegistryKeyPath));
            panel.Children.Add(Meta("Value name",
                string.IsNullOrEmpty(setting.GppRegistryValueName)
                    ? "(key/default item)" : setting.GppRegistryValueName));
            panel.Children.Add(new TextBlock
            {
                Text = "Stored GPMC GPP Registry preference. The action describes " +
                    "policy intent, not an already modified client registry. " +
                    "Neither Registry.pol nor effective client RSoP has been checked.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiStyle.WarningBrush,
                Margin = new Thickness(4, 5, 4, 6)
            });
        }
        if (SoftwareRestrictionMmcRouteService.IsSoftwareRestriction(setting))
            panel.Children.Add(new TextBlock
            {
                Text = "Software Restriction Policies: an exact Additional Rules " +
                    "path confirms only native MMC row identity. To compare the " +
                    "saved rule path and security level, open Exact MMC Paths and " +
                    "choose Check SRP Registry.pol (read-only) for this GPO. " +
                    "That comparison does NOT verify other SRP flags, rule precedence " +
                    "or effective client application.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiStyle.WarningBrush,
                Margin = new Thickness(4, 6, 4, 6)
            });
        panel.Children.Add(Meta("MMC section",
            hasVerifiedSection ? verifiedSectionPath :
                "UNMAPPED - full editor supports manual navigation only"));

        panel.Children.Add(
            Meta(
                "Registry",
                BuildRegistrySummary(
                    setting)));

        if (setting.Extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) &&
            setting.Category.Equals(
                "Security Settings > Local Policies > Security Options",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(setting.RegistryKey))
        {
            panel.Children.Add(Meta("GPMC Security Options KeyName (not verified Registry.pol)",
                setting.RegistryKey));
        }

        if (SecurityXmlEntryClassifier.IsTechnicalDetail(setting))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "GPMC XML detail (read-only). This entry describes a member, " +
                    "security descriptor, or registry ACL rather than an independently " +
                    "editable setting. MMC opens only a related section. The original " +
                    "full value is preserved below for inspection.",
                Foreground = UiStyle.WarningBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 4)
            });
        }

        if (setting.Extension.Equals("SoftwareInstallationSettings", StringComparison.OrdinalIgnoreCase) &&
            setting.SettingName.Equals("Applicability", StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "GPMC Applicability is package metadata, not a Registry key. " +
                    "The parent package identity is not present in this flattened entry. " +
                    "Use View / open section > GPMC report tree to inspect the parent " +
                    "software deployment package and its XML properties. " +
                    "Its applicability flags cannot be opened as an independent policy dialog. " +
                    "This is not a Registry.pol or GPP Registry item and has no registry path. " +
                    "GPMC's Settings report can show Registry hive/key/value details for a " +
                    "DIFFERENT GPP Registry preference in the same GPO; those values must " +
                    "be read from that Registry item's Properties, not inferred here.",
                Foreground = UiStyle.WarningBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 4)
            });
        }

        if (setting.Extension.Equals("SoftwareInstallationSettings", StringComparison.OrdinalIgnoreCase) &&
            (setting.SettingName.Contains("Trustee ", StringComparison.OrdinalIgnoreCase) ||
             setting.SettingName.Contains("Applicability", StringComparison.OrdinalIgnoreCase)))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "This is package security descriptor/auditing metadata from the GPO report, not a standalone Administrative Templates policy. Open Software installation and inspect the relevant package's security properties. The XML value below remains read-only.",
                Foreground = UiStyle.WarningBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 4)
            });
        }

        if (setting.Extension.Equals("PublicKeySettings", StringComparison.OrdinalIgnoreCase) ||
            setting.Extension.Equals("NrptSettings", StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "MMC section: " + GpoEditorNavigatorService.NavigationTarget(setting) +
                    ". This XML summary does not identify a uniquely editable MMC field, so the program opens the related section without guessing a settings dialog.",
                Foreground = UiStyle.AccentBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 4)
            });
        }

        if (setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase) &&
            setting.Value.Contains("AdmSetting=false", StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Raw registry.pol setting (AdmSetting=false): MMC cannot navigate to an exact ADMX editor when the policy definition is absent. This is not a GPP Registry item. Use the values below or install the matching ADMX files to enable policy-level editing.",
                Foreground = UiStyle.WarningBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 4)
            });
        }

        if (_booleanCombo is not null)
        {
            var valueGrid =
                new Grid
                {
                    Margin =
                        new Thickness(
                            4,
                            12,
                            4,
                            8)
                };

            valueGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(130)
                });

            valueGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            var label =
                new TextBlock
                {
                    Text = "Value:",
                    FontWeight =
                        FontWeights.SemiBold,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            Grid.SetColumn(
                label,
                0);

            Grid.SetColumn(
                _booleanCombo,
                1);

            valueGrid.Children.Add(
                label);

            valueGrid.Children.Add(
                _booleanCombo);

            panel.Children.Add(
                valueGrid);

            panel.Children.Add(
                new TextBlock
                {
                    Text =
                        "This Boolean Security Option can be changed directly. A GPO backup is created before the value is written.",
                    TextWrapping =
                        TextWrapping.Wrap,
                    Margin =
                        new Thickness(
                            8,
                            4,
                            8,
                            8),
                    Foreground =
                        System.Windows.Media.Brushes.DimGray
                });
        }
        else
        {
            var valueBox =
                new TextBox
                {
                    Text =
                        string.IsNullOrWhiteSpace(setting.Value)
                            ? "<not reported>"
                            : setting.Value,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 80,
                    VerticalScrollBarVisibility =
                        ScrollBarVisibility.Auto,
                    Margin =
                        new Thickness(
                            4,
                            8,
                            4,
                            8)
                };

            panel.Children.Add(
                new GroupBox
                {
                    Header = "Value",
                    Content = valueBox,
                    Margin =
                        new Thickness(
                            4,
                            8,
                            4,
                            8)
                });
        }

        var scroll =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                Content = panel
            };

        root.Children.Add(
            footer);

        root.Children.Add(
            scroll);

        Content =
            root;

        Loaded += (_, _) =>
        {
            _booleanCombo?.Focus();
        };
    }

    private static string BuildRegistrySummary(PolicySettingInfo setting) =>
        GpoSettingSourceProvenance.RegistrySummary(setting);

    private static FrameworkElement Meta(
        string name,
        string value)
    {
        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        4,
                        2,
                        4,
                        2)
            };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(130)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var label =
            new TextBlock
            {
                Text = name + ":",
                FontWeight =
                    FontWeights.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            };

        var text =
            new TextBlock
            {
                Text =
                    string.IsNullOrWhiteSpace(value)
                        ? "<not reported>"
                        : value,
                TextWrapping =
                    TextWrapping.Wrap
            };

        Grid.SetColumn(
            label,
            0);

        Grid.SetColumn(
            text,
            1);

        grid.Children.Add(
            label);

        grid.Children.Add(
            text);

        return grid;
    }
}
