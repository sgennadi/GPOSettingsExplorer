using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

/// <summary>Potential link footprint plus optional observed, single-client RSoP.</summary>
public sealed class GpoImpactPreviewWindow : Window
{
    public GpoImpactPreviewWindow(GpoImpactPreview preview, GpoInfo gpo)
    {
        Title = "GPO Impact Preview - Links and RSoP Evidence";
        Width = 1060;
        Height = 730;
        MinWidth = 660;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);
        var outer = new DockPanel { Margin = new Thickness(12) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = preview.GpoName,
            FontWeight = FontWeights.SemiBold,
            FontSize = UiStyle.HeadingFontSize,
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock
        {
            Text = preview.Summary + " | " + preview.GpoSections,
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock
        {
            Text = preview.WmiEvidence,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.WarningBrush
        });
        header.Children.Add(new TextBlock
        {
            Text = preview.Notes,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.MutedBrush,
            Margin = new Thickness(0, 5, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        outer.Children.Add(header);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var copy = new Button { Content = "Copy preview", MinWidth = 115 };
        var export = new Button { Content = "Export TXT...", MinWidth = 115 };
        var close = new Button { Content = "Close", MinWidth = 85 };
        actions.Children.Add(copy);
        actions.Children.Add(export);
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom);
        outer.Children.Add(actions);
        var sampleText = new TextBlock
        {
            Text = "RSoP sample not requested; no client application was inferred.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.WarningBrush,
            Margin = new Thickness(0, 8, 0, 0)
        };
        string Report() => preview.ToText() + "\n\nCLIENT SAMPLE\n" + sampleText.Text;
        copy.Click += (_, _) => Clipboard.SetText(Report());
        export.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text report (*.txt)|*.txt",
                FileName = $"GPO-Impact-{preview.GpoId:N}.txt",
                AddExtension = true
            };
            if (dialog.ShowDialog(this) == true)
                File.WriteAllText(dialog.FileName, Report());
        };
        close.Click += (_, _) => Close();

        var sample = new StackPanel { Margin = new Thickness(0, 8, 0, 10) };
        sample.Children.Add(new TextBlock
        {
            Text = "Optional: verify LAST LOGGED RSoP for one real client (not a future simulation).",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        var controls = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        controls.Children.Add(new TextBlock
        {
            Text = "Computer:", VerticalAlignment = VerticalAlignment.Center
        });
        var computer = new TextBox
        {
            Text = ".", Width = 190, MinWidth = 120,
            ToolTip = "Computer name or '.' for the local computer."
        };
        controls.Children.Add(computer);
        controls.Children.Add(new TextBlock
        {
            Text = "Scope:", VerticalAlignment = VerticalAlignment.Center
        });
        var scope = new ComboBox
        {
            ItemsSource = new[] { "Computer", "User" },
            SelectedIndex = 0, MinWidth = 110
        };
        controls.Children.Add(scope);
        controls.Children.Add(new TextBlock
        {
            Text = "User:", VerticalAlignment = VerticalAlignment.Center
        });
        var user = new TextBox
        {
            Width = 165, MinWidth = 110,
            ToolTip = "Required only for User scope; DOMAIN\\user."
        };
        controls.Children.Add(user);
        var verify = new Button { Content = "Verify RSoP sample...", MinWidth = 160 };
        controls.Children.Add(verify);
        sample.Children.Add(controls);
        sample.Children.Add(sampleText);
        DockPanel.SetDock(sample, Dock.Top);
        outer.Children.Add(sample);
        verify.Click += async (_, _) =>
        {
            verify.IsEnabled = false;
            sampleText.Foreground = UiStyle.WarningBrush;
            sampleText.Text = "Reading logged RSoP from selected client...";
            try
            {
                var host = computer.Text;
                var selectedScope = scope.SelectedItem as string ?? "Computer";
                var selectedUser = user.Text;
                var result = await Task.Run(() =>
                    new GpoRsopSampleService().Verify(gpo, host, selectedScope, selectedUser));
                sampleText.Text = result.Status + " | " + result.Details;
                sampleText.Foreground = result.Status.StartsWith("Applied", StringComparison.Ordinal)
                    ? UiStyle.SuccessBrush : UiStyle.WarningBrush;
            }
            catch (Exception ex)
            {
                sampleText.Text = "RSoP sample unknown: " + ex.Message;
            }
            finally
            {
                verify.IsEnabled = true;
            }
        };

        var grid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            ItemsSource = preview.DirectLinks,
            CanUserAddRows = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Target", Binding = new Binding(nameof(GpoImpactLink.Target)),
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Type", Binding = new Binding(nameof(GpoImpactLink.TargetType)),
            Width = new DataGridLength(110)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Link", Binding = new Binding(nameof(GpoImpactLink.LinkState)),
            Width = new DataGridLength(100)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Order", Binding = new Binding(nameof(GpoImpactLink.Order)),
            Width = new DataGridLength(90)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Target DN", Binding = new Binding(nameof(GpoImpactLink.TargetDn)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Evidence / conditions",
            Binding = new Binding(nameof(GpoImpactLink.Evidence)),
            Width = new DataGridLength(2.8, DataGridLengthUnitType.Star)
        });
        UiStyle.ApplyDataGridDefaults(grid);
        outer.Children.Add(grid);
        Content = outer;
    }
}
