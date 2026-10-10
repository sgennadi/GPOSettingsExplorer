using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

/// <summary>
/// Single compact, scroll-safe workspace for optional advanced read-only
/// analysis. Every live/network operation is operator initiated.
/// No automatic GPO modifications, Intune writes or public data uploads.
/// </summary>
public sealed partial class GpoAdvancedAnalysisWindow : Window
{
    private readonly GpoInfo? _target;
    private RealSettingsScanResult? _active;
    private string? _offlineRoot;
    private GpoSecurityScan? _lastSecurity;
    private GpoIntuneMappingDocument? _mapping;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _report = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = true,
        AcceptsTab = true,
        MinHeight = 230,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TabControl _tabs = new();
    private readonly TextBox _controllers = new()
    {
        MinWidth = 255,
        Text = "dc01.example.org,dc02.example.org",
        ToolTip = "Enter 1-16 actual DC hostnames/FQDNs, never the domain DFS alias."
    };
    private readonly TextBox _computer = new()
    {
        MinWidth = 165, Text = ".",
        ToolTip = "Computer/FQDN, or '.' for local Windows event log / gpresult."
    };
    private readonly ComboBox _scope = new() { MinWidth = 110 };
    private readonly TextBox _user = new()
    {
        MinWidth = 165,
        ToolTip = "For User RSoP only: DOMAIN\\user with previously logged policy processing."
    };
    private readonly TextBox _tenant = new()
    {
        MinWidth = 210, ToolTip = "Microsoft Entra tenant GUID; used only when Graph is clicked."
    };
    private readonly TextBox _appId = new()
    {
        MinWidth = 210,
        ToolTip = "Existing public client App Registration GUID; no app is created automatically."
    };
    private readonly TextBox _aiModel = new()
    {
        MinWidth = 160, Text = "qwen2.5:3b",
        ToolTip = "Existing locally installed Ollama model; no model is downloaded."
    };

    public GpoAdvancedAnalysisWindow(
        GpoInfo? target, RealSettingsScanResult? initialSource)
    {
        _target = target;
        _active = initialSource is not null && target is not null &&
                  initialSource.GpoId == target.Id &&
                  initialSource.Domain.Equals(target.DomainName,
                      StringComparison.OrdinalIgnoreCase)
            ? initialSource : null;

        Title = "GPO Settings Explorer - Advanced Analysis (read only)";
        Width = 1120;
        Height = 735;
        MinWidth = 760;
        MinHeight = 490;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);

        _report.FontFamily = UiStyle.MonospaceFontFamily;
        _report.FontSize = UiStyle.MonospaceFontSize;
        _status.Foreground = UiStyle.MutedBrush;
        _scope.ItemsSource = new[] { "Computer", "User" };
        _scope.SelectedIndex = 0;
        if (target is not null)
            _controllers.Text = DomainConnectionState.GetServerFor(target.DomainName);

        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = (_target is null
                ? "No connected GPO selected. Offline backup inspection remains available."
                : "Selected GPO: " + _target.DisplayName + " (" + _target.Id.ToString("B") + ")") +
                "\nEvery action is READ ONLY. Reports may contain sensitive domain metadata. " +
                "Graph and local AI are disabled until explicitly invoked.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        SetupTabs();
        _tabs.MaxHeight = 260;
        _tabs.MinHeight = 115;
        _tabs.Margin = new Thickness(0, 0, 0, 7);
        Grid.SetRow(_tabs, 1);
        root.Children.Add(_tabs);

        Grid.SetRow(_report, 2);
        root.Children.Add(_report);

        var footer = new DockPanel { LastChildFill = true };
        var commands = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var copy = MakeButton("Copy report");
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_report.Text))
                Clipboard.SetText(_report.Text);
        };
        var save = MakeButton("Save report locally...");
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_report.Text))
                return;
            var file = new SaveFileDialog
            {
                Title = "Save local GPO analysis (confidential)",
                Filter = "Text reports (*.txt)|*.txt|JSON (*.json)|*.json",
                FileName = "GPO-Advanced-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") +
                    ".txt", AddExtension = true
            };
            if (file.ShowDialog(this) == true)
                File.WriteAllText(file.FileName, _report.Text);
        };
        var close = MakeButton("Close");
        close.Click += (_, _) => Close();
        commands.Children.Add(copy);
        commands.Children.Add(save);
        commands.Children.Add(close);
        DockPanel.SetDock(commands, Dock.Right);
        footer.Children.Add(commands);
        _status.Text = _active is null
            ? "Choose Capture current GPO or Open backup to load source evidence."
            : "Loaded source: " + _active.Coverage;
        footer.Children.Add(_status);
        footer.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        Content = root;
    }

    private void SetupTabs()
    {
        var backups = Tab("Sources & timeline", out var sourcePanel);
        AddAction(sourcePanel, "Capture current GPO", CaptureCurrentAsync);
        AddAction(sourcePanel, "Open GPMC backup...", OpenBackupAsync);
        AddAction(sourcePanel, "Compare with backup...", CompareBackupAsync);
        AddAction(sourcePanel, "Save DPAPI timeline", SaveTimelineAsync);
        AddAction(sourcePanel, "Compare saved timeline", CompareTimelineAsync);
        AddAction(sourcePanel, "Security source scan", SecurityScanAsync);
        _tabs.Items.Add(backups);

        var health = Tab("Client & DC health", out var healthPanel);
        healthPanel.Children.Add(Label("DCs:"));
        healthPanel.Children.Add(_controllers);
        AddAction(healthPanel, "Compare source SHA-256", CrossDcAsync);
        AddAction(healthPanel, "Full SYSVOL tree SHA-256...", FullSysvolTreeAsync);
        healthPanel.Children.Add(Label("Client:"));
        healthPanel.Children.Add(_computer);
        AddAction(healthPanel, "Client GP events", ClientEventsAsync);
        healthPanel.Children.Add(Label("RSoP scope:"));
        healthPanel.Children.Add(_scope);
        healthPanel.Children.Add(Label("User:"));
        healthPanel.Children.Add(_user);
        AddAction(healthPanel, "Explain logged GPO", ExplainRsopAsync);
        AddAction(healthPanel, "Explain why (full evidence)...", ExplainWhyAsync);
        _tabs.Items.Add(health);

        var compliance = Tab("Baseline / Intune", out var compliancePanel);
        AddAction(compliancePanel, "Compare baseline JSON...", CompareBaselineAsync);
        AddAction(compliancePanel, "Compare reference GPMC baseline...", ReferenceBaselineAsync);
        AddAction(compliancePanel, "Load reviewed CSP map...", LoadCspMappingAsync);
        AddAction(compliancePanel, "Assess Intune readiness", IntuneReadinessAsync);
        _tabs.Items.Add(compliance);
        _tabs.Items.Add(SetupGitOpsTab());

        var cloud = Tab("Optional cloud & local AI", out var cloudPanel);
        cloudPanel.Children.Add(Label("Tenant GUID:"));
        cloudPanel.Children.Add(_tenant);
        cloudPanel.Children.Add(Label("App GUID:"));
        cloudPanel.Children.Add(_appId);
        AddAction(cloudPanel, "Graph Intune inventory (up to 500)...", GraphIntuneAsync);
        cloudPanel.Children.Add(Label("Local model:"));
        cloudPanel.Children.Add(_aiModel);
        AddAction(cloudPanel, "Local AI analysis...", LocalAiAsync);
        cloudPanel.Children.Add(new TextBlock
        {
            Text = "Graph uses opt-in MSAL device code and delegated READ permission. " +
                "Local AI uses only 127.0.0.1 Ollama and sends anonymized finding counts. " +
                "Neither integration is contacted automatically.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(5, 7, 5, 5)
        });
        _tabs.Items.Add(cloud);
        _tabs.SelectedIndex = 0;
    }

    private static TabItem Tab(string name, out WrapPanel content)
    {
        content = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6)
        };
        return new TabItem
        {
            Header = name,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            }
        };
    }

    private static Button MakeButton(string title) => new()
    {
        Content = title,
        MinWidth = 125,
        MinHeight = 30,
        Margin = new Thickness(2, 3, 5, 3)
    };
    private static TextBlock Label(string text) => new()
    {
        Text = text, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(7, 4, 3, 4)
    };

    private void AddAction(
        WrapPanel panel, string name, Func<Task<string>> operation)
    {
        var button = MakeButton(name);
        button.Click += async (_, _) => await RunAsync(name, operation);
        panel.Children.Add(button);
    }

    private async Task RunAsync(string action, Func<Task<string>> operation)
    {
        if (!_tabs.IsEnabled)
            return;
        _tabs.IsEnabled = false;
        _status.Text = "Running read-only analysis: " + action;
        _status.Foreground = UiStyle.AccentBrush;
        try
        {
            var report = await operation();
            _report.Text = report;
            _report.ScrollToHome();
            _status.Text = "Completed: " + action + " (local report, no automatic upload)";
            _status.Foreground = UiStyle.SuccessBrush;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Analysis canceled; no GPO modified.";
            _status.Foreground = UiStyle.WarningBrush;
        }
        catch (Exception ex)
        {
            CrashLogService.Write("Advanced analysis: " + action, ex);
            _report.Text = "Analysis could not complete. No GPO modifications were attempted.\n\n" +
                ex.GetType().Name + ": " + ex.Message;
            _status.Text = "Incomplete: " + action;
            _status.Foreground = UiStyle.ErrorBrush;
        }
        finally
        {
            _tabs.IsEnabled = true;
        }
    }

    private RealSettingsScanResult Source() =>
        _active ?? throw new InvalidOperationException(
            "Capture a selected GPO or open a GPMC backup first.");

    private GpoInfo Target() =>
        _target ?? throw new InvalidOperationException(
            "Select a connected GPO in the GPO list before opening this window.");

    private async Task<string> CaptureCurrentAsync()
    {
        var gpo = Target();
        var result = await Task.Run(() =>
            new RealSettingsSourceService().Scan(gpo, _lifetime.Token),
            _lifetime.Token);
        _active = result;
        _offlineRoot = null;
        return "LIVE STORED-SOURCE SNAPSHOT\n" + result.Coverage + "\n" +
               "Records: " + result.Rows.Count + "\n" +
               string.Join("\n", result.Files.Select(file =>
                   "[" + file.Status + "] " + file.SourceFile + " — " +
                   file.Details));
    }

    private async Task<string> OpenBackupAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a local GPMC bkupInfo.xml (offline)",
            Filter = "GPMC backup manifest (bkupInfo.xml)|bkupInfo.xml",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return "No backup selected; previous data preserved.";
        var manifest = dialog.FileName;
        var result = await Task.Run(() =>
            OfflineGpoSourceService.ReadManifest(manifest, _lifetime.Token),
            _lifetime.Token);
        _active = result;
        _offlineRoot = Path.Combine(Path.GetDirectoryName(manifest)!,
            "DomainSysvol", "GPO");
        return "OFFLINE GPMC BACKUP LOADED\n" + result.Coverage + "\n" +
               "GPO: " + result.GpoName + " | " + result.GpoId + "\n" +
               "Domain metadata: " + result.Domain + "\n" +
               "No connection to AD, GPMC or Microsoft Graph was made.";
    }

    private async Task<string> CompareBackupAsync()
    {
        var older = Source();
        var dialog = new OpenFileDialog
        {
            Title = "Select second GPMC backup for same GPO",
            Filter = "GPMC backup manifest (bkupInfo.xml)|bkupInfo.xml"
        };
        if (dialog.ShowDialog(this) != true)
            return "Comparison canceled; no files changed.";
        var other = await Task.Run(() =>
            OfflineGpoSourceService.ReadManifest(dialog.FileName, _lifetime.Token),
            _lifetime.Token);
        if (older.GpoId != other.GpoId ||
            !older.Domain.Equals(other.Domain, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Backups are from different GPOs/domains; comparison refused.");
        var before = GpoTimelineService.Capture(older);
        var after = GpoTimelineService.Capture(other);
        var changes = GpoTimelineService.Compare(before, after);
        return "STORED SOURCE DIFFERENCE (HASH-ONLY)\n" +
               "First: " + older.Coverage + "\nSecond: " + other.Coverage +
               "\n" + changes.Count + " changed identities.\n\n" +
               string.Join("\n", changes.Take(3000).Select(c =>
                   c.Kind + ": " + c.Setting)) +
               (changes.Count > 3000 ? "\n[Truncated to first 3000 rows.]" : "");
    }

    private async Task<string> SaveTimelineAsync()
    {
        var snapshot = GpoTimelineService.Capture(Source());
        var path = await Task.Run(() => GpoTimelineService.Save(snapshot));
        return "Encrypted current-user-DPAPI timeline snapshot saved locally:\n" +
               path + "\n\nContains setting identities and SHA-256 fingerprints, " +
               "not plaintext stored values. Automatic retention: last 40 per GPO.";
    }

    private async Task<string> CompareTimelineAsync()
    {
        var current = GpoTimelineService.Capture(Source());
        var saved = GpoTimelineService.List(current.Domain, current.GpoId);
        if (saved.Count == 0)
            return "No previously saved timeline snapshots found for this GPO.";
        var older = await Task.Run(() => GpoTimelineService.Load(saved[0]));
        if (older.GpoId != current.GpoId ||
            !older.Domain.Equals(current.Domain, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Timeline scope differs; comparison blocked.");
        var changes = GpoTimelineService.Compare(older, current);
        return "LATEST SAVED TIMELINE vs CURRENT SOURCE\n" +
               "Previous snapshot: " + older.CapturedUtc.ToString("O") +
               "\nCurrent scan: " + current.CapturedUtc.ToString("O") +
               "\nChanges: " + changes.Count + "\n\n" +
               string.Join("\n", changes.Take(3000).Select(c =>
                   c.Kind + ": " + c.Setting)) +
               (changes.Count > 3000 ? "\n[Truncated to first 3000 rows.]" : "");
    }

    private async Task<string> SecurityScanAsync()
    {
        var current = Source();
        var root = _offlineRoot;
        if (root is null)
        {
            var gpo = Target();
            if (current.GpoId != gpo.Id)
                throw new InvalidOperationException(
                    "Source GPO differs from connected target.");
            root = Path.Combine(DomainConnectionState.BuildSysvolRoot(gpo.DomainName),
                "Policies", gpo.Id.ToString("B"));
        }
        _lastSecurity = await Task.Run(() =>
            GpoSecurityScannerService.Scan(root, current.GpoId, _lifetime.Token),
            _lifetime.Token);
        return _lastSecurity.ToText();
    }

    private async Task<string> CrossDcAsync()
    {
        var gpo = Target();
        var hosts = _controllers.Text;
        var report = await Task.Run(() =>
            GpoCrossDcSourceFingerprintService.Compare(
                gpo, hosts, _lifetime.Token), _lifetime.Token);
        return report.ToText();
    }

    private async Task<string> FullSysvolTreeAsync()
    {
        var gpo = Target();
        var hosts = _controllers.Text;
        var report = await Task.Run(() =>
            GpoSysvolTreeIntegrityService.Compare(gpo, hosts, _lifetime.Token),
            _lifetime.Token);
        return report.ToText();
    }

    private Task<string> ClientEventsAsync() =>
        ReadClientEventsAsync();

    private async Task<string> ReadClientEventsAsync()
    {
        var report = await GpoClientEventService.CollectAsync(
            _computer.Text.Trim(), _lifetime.Token);
        return report.ToText();
    }

    private async Task<string> ExplainRsopAsync()
    {
        var gpo = Target();
        var client = _computer.Text.Trim();
        var scope = (string?)_scope.SelectedItem ?? "Computer";
        var username = _user.Text.Trim();
        var sample = await Task.Run(() =>
            new GpoRsopSampleService().Verify(gpo, client, scope, username),
            _lifetime.Token);
        return "EXPLAIN LOGGED RSoP (LAST PROCESSED CLIENT SAMPLE)\n" +
               "GPO: " + gpo.DisplayName + "\n" +
               "Computer: " + sample.Computer + "\n" +
               "Scope: " + sample.Scope + "\n" +
               "Status: " + sample.Status + "\n" + sample.Details +
               "\nUnknown evidence is not proof that a GPO is blocked.";
    }

    private async Task<string> ExplainWhyAsync()
    {
        var gpo = Target();
        var computer = _computer.Text.Trim();
        var scope = (string?)_scope.SelectedItem ?? "Computer";
        var user = _user.Text.Trim();
        var lastLogged = await Task.Run(() =>
            new GpoRsopSampleService().Verify(gpo, computer, scope, user),
            _lifetime.Token);

        GpoClientScopeReport? location = null;
        string locationError = "";
        try
        {
            location = await Task.Run(() =>
                GpoClientScopeProbeService.Inspect(gpo, computer, _lifetime.Token),
                _lifetime.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            locationError = ex.GetType().Name + ": " + ex.Message;
            CrashLogService.Write("Explain Why: client AD path", ex);
        }

        GpoClientEventReport? events = null;
        string eventError = "";
        try
        {
            events = await GpoClientEventService.CollectAsync(
                computer, _lifetime.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            eventError = ex.GetType().Name + ": " + ex.Message;
            CrashLogService.Write("Explain Why: client events", ex);
        }

        var explanation = GpoExplainWhyService.Build(
            gpo, scope, lastLogged, location, events,
            locationError, eventError);
        new GpoExplanationWindow(explanation) { Owner = this }.ShowDialog();
        return explanation.ToText();
    }

    private async Task<string> CompareBaselineAsync()
    {
        var current = Source();
        var dialog = new OpenFileDialog
        {
            Title = "Choose an operator-reviewed normalized GPO baseline (JSON)",
            Filter = "Normalized GPO baseline (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true)
            return "Baseline selection canceled.";
        var result = await Task.Run(() =>
        {
            var baseline = GpoBaselineAssessmentService.Load(dialog.FileName);
            return GpoBaselineAssessmentService.Assess(current, baseline);
        }, _lifetime.Token);
        return result.ToText();
    }

    private async Task<string> ReferenceBaselineAsync()
    {
        var current = Source();
        var dialog = new OpenFileDialog
        {
            Title = "Choose a reviewed reference baseline GPMC backup",
            Filter = "GPMC backup manifest (bkupInfo.xml)|bkupInfo.xml"
        };
        if (dialog.ShowDialog(this) != true)
            return "No reference baseline was chosen.";
        var selected = dialog.FileName;
        var report = await Task.Run(() =>
        {
            var reference = OfflineGpoSourceService.ReadManifest(
                selected, _lifetime.Token);
            return GpoReferenceBaselineService.Compare(current, reference);
        }, _lifetime.Token);
        return report.ToText();
    }

    private async Task<string> LoadCspMappingAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a manually verified ADMX/Policy CSP mapping",
            Filter = "Normalized CSP mappings (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true)
            return "No new mapping selected.";
        _mapping = await Task.Run(() =>
            GpoIntuneMigrationService.LoadMapping(dialog.FileName), _lifetime.Token);
        return "Reviewed CSP mapping loaded from local file:\n" +
               _mapping.Source + "\n" +
               _mapping.Mappings.Count + " explicit mapping records.\n" +
               "No tenant connection or policy migration has occurred.";
    }

    private async Task<string> IntuneReadinessAsync()
    {
        var current = Source();
        var mapping = _mapping;
        var result = await Task.Run(() =>
            GpoIntuneMigrationService.Assess(current, mapping), _lifetime.Token);
        return result.ToText();
    }

    private Task<string> GitOpsAsync()
    {
        var report = GpoGitOpsExportService.Capture(Source());
        return Task.FromResult(GpoGitOpsExportService.ToJson(report));
    }

    private async Task<string> GraphIntuneAsync()
    {
        if (MessageBox.Show(this,
                "Connect to Microsoft Graph using device-code sign-in and " +
                "DeviceManagementConfiguration.Read.All (delegated). " +
                "This manually requested operation reads up to 500 Intune " +
                "policy metadata records across 10 pages (names, assignment flag, " +
                "technology, setting count and modification date). No tokens " +
                "are saved to disk and no policies are changed.\n\nContinue?",
                "Explicit Microsoft Graph permission", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return "Graph request canceled by operator.";
        var tenant = _tenant.Text.Trim();
        var client = _appId.Text.Trim();
        var result = await GpoIntuneGraphReadOnlyService.QueryAsync(
            tenant, client, async message =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _report.Text = message;
                    _status.Text = "Enter device code at the Microsoft sign-in site. " +
                                   "No password is entered in this application.";
                    _status.Foreground = UiStyle.WarningBrush;
                });
            }, _lifetime.Token);
        return result.ToText();
    }

    private async Task<string> LocalAiAsync()
    {
        if (_lastSecurity is null)
            throw new InvalidOperationException(
                "Run the Security source scan first; only aggregate findings go to local AI.");
        if (MessageBox.Show(this,
                "Send REDACTED COUNTS ONLY to a locally installed Ollama model " +
                "at 127.0.0.1:11434? No domain names, paths, passwords, " +
                "registry values or scripts are transmitted. " +
                "This is optional, never used for automatic changes.\n\nContinue?",
                "Local AI consent", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return "Local AI request canceled by operator.";
        return await GpoLocalAiService.ExplainAsync(
            _lastSecurity, _aiModel.Text.Trim(), _lifetime.Token);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnClosed(e);
    }
}