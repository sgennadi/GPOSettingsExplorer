using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public sealed class ConnectionWindow : Window
{
    private readonly DomainContextService _domainContextService =
        new();

    private readonly ConnectionProfileService _profileService =
        new();

    private readonly RadioButton _currentCredentialsRadio;
    private readonly RadioButton _alternateCredentialsRadio;
    private readonly TextBox _domainBox;
    private readonly TextBox _dcBox;
    private readonly TextBox _userBox;
    private readonly PasswordBox _passwordBox;
    private readonly ComboBox _persistenceCombo;
    private readonly TextBlock _statusText;
    private readonly Button _connectButton;

    public DomainContext? ConnectedContext { get; private set; }

    public bool RelaunchStarted { get; private set; }

    public ConnectionWindow()
    {
        Title =
            "Connect to Active Directory";

        Width =
            780;

        Height =
            590;

        MinWidth =
            620;

        MinHeight =
            500;

        WindowStartupLocation =
            WindowStartupLocation.CenterScreen;

        ResizeMode =
            ResizeMode.CanResize;

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(16)
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

        var detect =
            new Button
            {
                Content =
                    "Auto detect"
            };

        detect.Click +=
            Detect_Click;

        var test =
            new Button
            {
                Content =
                    "Test connection"
            };

        test.Click +=
            Test_Click;

        _connectButton =
            new Button
            {
                Content =
                    "Connect",
                IsDefault =
                    true
            };

        _connectButton.Click +=
            Connect_Click;

        footer.Children.Add(
            detect);

        footer.Children.Add(
            test);

        footer.Children.Add(
            _connectButton);

        footer.Children.Add(
            new Button
            {
                Content =
                    "Cancel",
                IsCancel =
                    true
            });

        var header =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        14)
            };

        DockPanel.SetDock(
            header,
            Dock.Top);

        header.Children.Add(
            new TextBlock
            {
                Text =
                    "Active Directory connection",
                FontSize =
                    UiStyle.HeadingFontSize,
                FontWeight =
                    FontWeights.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            });

        header.Children.Add(
            new TextBlock
            {
                Text =
                    "Choose the Windows session or alternate domain credentials. A specific domain controller can be pinned so LDAP, GPMC and SYSVOL stay on the same DC.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0)
            });

        var panel =
            new StackPanel();

        _currentCredentialsRadio =
            new RadioButton
            {
                Content =
                    $"Use current Windows session ({Environment.UserDomainName}\\{Environment.UserName})",
                IsChecked =
                    true,
                Margin =
                    new Thickness(
                        4,
                        4,
                        4,
                        8)
            };

        _alternateCredentialsRadio =
            new RadioButton
            {
                Content =
                    "Use alternate AD credentials",
                Margin =
                    new Thickness(
                        4,
                        4,
                        4,
                        12)
            };

        _currentCredentialsRadio.Checked +=
            CredentialMode_Changed;

        _alternateCredentialsRadio.Checked +=
            CredentialMode_Changed;

        panel.Children.Add(
            _currentCredentialsRadio);

        panel.Children.Add(
            _alternateCredentialsRadio);

        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        2,
                        0,
                        10)
            };

        for (var row = 0;
             row < 5;
             row++)
        {
            grid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });
        }

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        180)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        _domainBox =
            new TextBox();

        _dcBox =
            new TextBox();

        _userBox =
            new TextBox();

        _passwordBox =
            new PasswordBox();

        _persistenceCombo =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "Session only",
                        "Remember for current user (DPAPI)",
                        "Remember on this computer (DPAPI machine)"
                    },
                SelectedIndex =
                    0
            };

        AddRow(
            grid,
            0,
            "Domain:",
            _domainBox,
            "DNS domain, for example yosh.ac.il. Leave blank with current credentials to auto-detect.");

        AddRow(
            grid,
            1,
            "Domain controller:",
            _dcBox,
            "Optional. Pin a DC such as yosh-dc02.yosh.ac.il. Leave blank to let AD select one.");

        AddRow(
            grid,
            2,
            "User:",
            _userBox,
            @"DOMAIN\user or user@domain.");

        AddRow(
            grid,
            3,
            "Password:",
            _passwordBox,
            "The password is never written as plaintext.");

        AddRow(
            grid,
            4,
            "Credential storage:",
            _persistenceCombo,
            "Session-only is the safest default.");

        panel.Children.Add(
            grid);

        var safeMode =
            new Border
            {
                BorderBrush =
                    System.Windows.Media.Brushes.LightGray,
                BorderThickness =
                    new Thickness(1),
                Padding =
                    new Thickness(10),
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        10)
            };

        safeMode.Child =
            new TextBlock
            {
                Text =
                    "The application starts in Safe mode (read-only). You explicitly enable write operations after connecting.",
                TextWrapping =
                    TextWrapping.Wrap
            };

        panel.Children.Add(
            safeMode);

        _statusText =
            new TextBlock
            {
                Text =
                    "Ready to connect.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(
                        4,
                        8,
                        4,
                        4)
            };

        panel.Children.Add(
            _statusText);

        root.Children.Add(
            footer);

        root.Children.Add(
            header);

        root.Children.Add(
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                Content =
                    panel
            });

        Content =
            root;

        LoadStoredProfile();
        UpdateCredentialMode();
    }

    private void LoadStoredProfile()
    {
        var commandLine =
            CommandLineOptions.Current;

        if (!string.IsNullOrWhiteSpace(
                commandLine.DomainName) ||
            !string.IsNullOrWhiteSpace(
                commandLine.DomainController))
        {
            _domainBox.Text =
                commandLine.DomainName;

            _dcBox.Text =
                commandLine.DomainController;

            return;
        }

        var stored =
            _profileService.Load();

        if (stored is null)
            return;

        _domainBox.Text =
            stored.DomainName;

        _dcBox.Text =
            stored.DomainController;

        _userBox.Text =
            stored.UserName;

        if (!stored.UseCurrentCredentials)
        {
            _alternateCredentialsRadio.IsChecked =
                true;

            _passwordBox.Password =
                _profileService.LoadPassword(
                    stored);
        }

        _persistenceCombo.SelectedIndex =
            stored.PersistenceScope switch
            {
                CredentialPersistenceScope.CurrentUser => 1,
                CredentialPersistenceScope.LocalMachine => 2,
                _ => 0
            };
    }

    private async void Detect_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunConnectionTestAsync(
            updateFields: true);
    }

    private async void Test_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunConnectionTestAsync(
            updateFields: false);
    }

    private async void Connect_Click(
        object sender,
        RoutedEventArgs e)
    {
        _connectButton.IsEnabled =
            false;

        try
        {
            var profile =
                BuildProfile();

            var password =
                _passwordBox.Password;

            var context =
                await Task.Run(
                    () =>
                        _domainContextService.Detect(
                            profile,
                            password));

            profile =
                profile with
                {
                    DomainName =
                        context.DomainName,
                    DomainController =
                        context.ConnectedServer
                };

            _profileService.Save(
                profile,
                password);

            if (!profile.UseCurrentCredentials)
            {
                _statusText.Text =
                    "Connection succeeded. Relaunching with the supplied network credentials...";

                AlternateCredentialLauncher.Relaunch(
                    profile.UserName,
                    password,
                    profile.DomainName,
                    profile.DomainController);

                RelaunchStarted =
                    true;

                DialogResult =
                    true;

                return;
            }

            DomainConnectionState.SetProfile(
                profile);

            DomainConnectionState.SetContext(
                context);

            ConnectedContext =
                context;

            DialogResult =
                true;
        }
        catch (Exception ex)
        {
            _statusText.Text =
                "Connection failed.";

            MessageBox.Show(
                this,
                ex.Message,
                "Connect to Active Directory",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (IsLoaded)
            {
                _connectButton.IsEnabled =
                    true;
            }
        }
    }

    private async Task RunConnectionTestAsync(
        bool updateFields)
    {
        try
        {
            _statusText.Text =
                "Testing Active Directory connection...";

            var profile =
                BuildProfile();

            var context =
                await Task.Run(
                    () =>
                        _domainContextService.Detect(
                            profile,
                            _passwordBox.Password));

            if (updateFields)
            {
                _domainBox.Text =
                    context.DomainName;

                _dcBox.Text =
                    context.ConnectedServer;
            }

            _statusText.Text =
                $"Connected successfully: {context.DomainName} via {context.ConnectedServer}";
        }
        catch (Exception ex)
        {
            _statusText.Text =
                "Connection test failed.";

            MessageBox.Show(
                this,
                ex.Message,
                "Test Active Directory Connection",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private DomainConnectionProfile BuildProfile()
    {
        var alternate =
            _alternateCredentialsRadio.IsChecked ==
            true;

        if (alternate &&
            string.IsNullOrWhiteSpace(
                _userBox.Text))
        {
            throw new InvalidOperationException(
                "Enter the AD user name.");
        }

        if (alternate &&
            string.IsNullOrEmpty(
                _passwordBox.Password))
        {
            throw new InvalidOperationException(
                "Enter the AD password.");
        }

        return new DomainConnectionProfile(
            _domainBox.Text.Trim(),
            _dcBox.Text.Trim(),
            alternate
                ? _userBox.Text.Trim()
                : string.Empty,
            !alternate,
            alternate
                ? PersistenceFromSelection()
                : CredentialPersistenceScope.None);
    }

    private CredentialPersistenceScope PersistenceFromSelection() =>
        _persistenceCombo.SelectedIndex switch
        {
            1 => CredentialPersistenceScope.CurrentUser,
            2 => CredentialPersistenceScope.LocalMachine,
            _ => CredentialPersistenceScope.None
        };

    private void CredentialMode_Changed(
        object sender,
        RoutedEventArgs e)
    {
        UpdateCredentialMode();
    }

    private void UpdateCredentialMode()
    {
        if (_userBox is null ||
            _passwordBox is null ||
            _persistenceCombo is null)
        {
            return;
        }

        var alternate =
            _alternateCredentialsRadio.IsChecked ==
            true;

        _userBox.IsEnabled =
            alternate;

        _passwordBox.IsEnabled =
            alternate;

        _persistenceCombo.IsEnabled =
            alternate;
    }

    private static void AddRow(
        Grid grid,
        int row,
        string label,
        Control control,
        string toolTip)
    {
        var caption =
            new TextBlock
            {
                Text =
                    label,
                VerticalAlignment =
                    VerticalAlignment.Center,
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        4,
                        8,
                        10,
                        4)
            };

        control.Margin =
            new Thickness(4);

        control.ToolTip =
            toolTip;

        Grid.SetRow(
            caption,
            row);

        Grid.SetColumn(
            caption,
            0);

        Grid.SetRow(
            control,
            row);

        Grid.SetColumn(
            control,
            1);

        grid.Children.Add(
            caption);

        grid.Children.Add(
            control);
    }
}
