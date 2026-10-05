using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

public sealed class BackupImportWindow : Window
{
    private readonly RadioButton _existingRadio;
    private readonly RadioButton _newRadio;
    private readonly ComboBox _gpoCombo;
    private readonly TextBox _newNameBox;
    private readonly TextBox _migrationTableBox;

    public bool CreateNewTarget => _newRadio.IsChecked == true;
    public GpoInfo? SelectedTarget => _gpoCombo.SelectedItem as GpoInfo;
    public string NewGpoName => _newNameBox.Text.Trim();
    public string MigrationTablePath => _migrationTableBox.Text.Trim();

    public BackupImportWindow(
        GpoBackupInfo backup,
        IEnumerable<GpoInfo> gpos)
    {
        Title = "Import GPO Backup";
        Width = 820;
        Height = 470;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(14) };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var import = new Button { Content = "Import", IsDefault = true };
        import.Click += Import_Click;
        footer.Children.Add(cancel);
        footer.Children.Add(import);

        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "Backup:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 4, 4, 2)
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{backup.DisplayName}  |  {backup.Timestamp:yyyy-MM-dd HH:mm:ss}  |  {backup.BackupId:B}",
            Margin = new Thickness(4, 0, 4, 12),
            TextWrapping = TextWrapping.Wrap
        });

        _existingRadio = new RadioButton
        {
            Content = "Import into an existing GPO",
            IsChecked = true,
            GroupName = "ImportTarget",
            Margin = new Thickness(4)
        };

        _newRadio = new RadioButton
        {
            Content = "Create a new GPO and import the backup into it",
            GroupName = "ImportTarget",
            Margin = new Thickness(4)
        };

        _existingRadio.Checked += (_, _) => UpdateTargetMode();
        _newRadio.Checked += (_, _) => UpdateTargetMode();

        panel.Children.Add(_existingRadio);

        _gpoCombo = new ComboBox
        {
            ItemsSource = gpos.OrderBy(gpo => gpo.DisplayName).ToArray(),
            DisplayMemberPath = nameof(GpoInfo.DisplayName),
            IsTextSearchEnabled = true,
            Margin = new Thickness(26, 2, 4, 10)
        };
        _gpoCombo.SelectedIndex = _gpoCombo.Items.Count > 0 ? 0 : -1;
        panel.Children.Add(_gpoCombo);

        panel.Children.Add(_newRadio);

        _newNameBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(backup.DisplayName)
                ? "Imported GPO"
                : backup.DisplayName + " - Imported",
            Margin = new Thickness(26, 2, 4, 12)
        };
        panel.Children.Add(_newNameBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Migration table (optional):",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 8, 4, 2)
        });

        var migrationPanel = new DockPanel();
        var browse = new Button
        {
            Content = "Browse...",
            MinWidth = 90
        };
        browse.Click += BrowseMigrationTable_Click;
        DockPanel.SetDock(browse, Dock.Right);

        _migrationTableBox = new TextBox
        {
            Margin = new Thickness(4)
        };

        migrationPanel.Children.Add(browse);
        migrationPanel.Children.Add(_migrationTableBox);
        panel.Children.Add(migrationPanel);

        panel.Children.Add(new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(4, 12, 4, 4),
            Child = new TextBlock
            {
                Text = "Import replaces all policy settings in the destination GPO. It does not replace the destination GPO GUID, ACLs, existing links, or WMI-filter association. GPO Settings Explorer creates a safety backup of an existing destination GPO before importing.",
                TextWrapping = TextWrapping.Wrap
            }
        });

        root.Children.Add(footer);
        root.Children.Add(panel);
        Content = root;

        UpdateTargetMode();
    }

    private void BrowseMigrationTable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select GPMC migration table",
            Filter = "Migration tables (*.migtable;*.xml)|*.migtable;*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            _migrationTableBox.Text = dialog.FileName;
    }

    private void UpdateTargetMode()
    {
        if (_gpoCombo is null || _newNameBox is null)
            return;

        var createNew = _newRadio.IsChecked == true;
        _gpoCombo.IsEnabled = !createNew;
        _newNameBox.IsEnabled = createNew;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (CreateNewTarget)
        {
            if (string.IsNullOrWhiteSpace(NewGpoName))
            {
                MessageBox.Show(
                    this,
                    "Enter a display name for the new GPO.",
                    "Import GPO Backup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                _newNameBox.Focus();
                return;
            }
        }
        else if (SelectedTarget is null)
        {
            MessageBox.Show(
                this,
                "Select the destination GPO.",
                "Import GPO Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(MigrationTablePath) &&
            !File.Exists(MigrationTablePath))
        {
            MessageBox.Show(
                this,
                "The selected migration table does not exist.",
                "Import GPO Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _migrationTableBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
