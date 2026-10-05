using GPOSettingsExplorer.Models;
using System.Windows;

namespace GPOSettingsExplorer;

public partial class WmiFilterEditorWindow : Window
{
    public WmiFilterInfo Filter { get; }

    public WmiFilterEditorWindow(WmiFilterInfo filter)
    {
        InitializeComponent();
        Filter = filter;
        DataContext = Filter;
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        Filter.Rules.Add(new WmiRuleInfo
        {
            QueryLanguage = "WQL",
            TargetNamespace = @"root\CIMv2",
            Query = "SELECT * FROM Win32_OperatingSystem"
        });

        RulesGrid.SelectedIndex = Filter.Rules.Count - 1;
        RulesGrid.ScrollIntoView(RulesGrid.SelectedItem);
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is WmiRuleInfo selected)
        {
            Filter.Rules.Remove(selected);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        RulesGrid.CommitEdit();
        RulesGrid.CommitEdit();

        if (string.IsNullOrWhiteSpace(Filter.Name))
        {
            MessageBox.Show(this, "Enter a WMI filter name.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Filter.Rules.Count == 0 ||
            Filter.Rules.Any(r => string.IsNullOrWhiteSpace(r.Query) ||
                                  string.IsNullOrWhiteSpace(r.TargetNamespace)))
        {
            MessageBox.Show(this, "Every WMI filter must contain at least one complete WQL rule.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
