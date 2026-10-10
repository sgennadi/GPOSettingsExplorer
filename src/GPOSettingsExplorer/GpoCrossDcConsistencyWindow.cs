using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

/// <summary>Explicit host list; cannot change or repair any DC.</summary>
public sealed class GpoCrossDcConsistencyWindow : Window
{
    public GpoCrossDcConsistencyWindow(
        GpoInfo gpo, string domainDn, string connectedDc)
    {
        Title = "Compare GPO Versions on DCs (READ ONLY)";
        Width = 1050;
        Height = 690;
        MinWidth = 640;
        MinHeight = 425;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);

        var outer = new DockPanel { Margin = new Thickness(12) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(new TextBlock
        {
            Text = gpo.DisplayName + " | " + gpo.DomainName,
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        top.Children.Add(new TextBlock
        {
            Text = "Enter actual DC hostnames, one per line (up to 16). " +
                "Start with the connected DC and add specific other DCs. " +
                "Do not use the domain name or DFS alias; the tool reads each DC directly.",
            TextWrapping = TextWrapping.Wrap
        });
        var hosts = new TextBox
        {
            Text = connectedDc,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 75,
            MaxHeight = 125,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = UiStyle.MonospaceFontFamily,
            FontSize = UiStyle.MonospaceFontSize,
            Margin = new Thickness(0, 8, 0, 5)
        };
        top.Children.Add(hosts);
        var run = new Button { Content = "Compare selected DCs...", MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left };
        top.Children.Add(run);
        var outcome = new TextBlock
        {
            Text = "Not checked. This is an AD versionNumber / SYSVOL GPT.INI version " +
                "comparison, NOT a DFSR or effective RSoP verdict.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.WarningBrush,
            Margin = new Thickness(0, 8, 0, 4)
        };
        top.Children.Add(outcome);
        DockPanel.SetDock(top, Dock.Top);
        outer.Children.Add(top);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var copy = new Button { Content = "Copy report", MinWidth = 115, IsEnabled = false };
        var export = new Button { Content = "Export TXT...", MinWidth = 115, IsEnabled = false };
        var close = new Button { Content = "Close", MinWidth = 85 };
        actions.Children.Add(copy);
        actions.Children.Add(export);
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom);
        outer.Children.Add(actions);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "DC", Binding = new Binding(nameof(GpoDcVersionEvidence.Dc)),
            Width = new DataGridLength(1.3, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status", Binding = new Binding(nameof(GpoDcVersionEvidence.Status)),
            Width = new DataGridLength(120)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Versions", Binding = new Binding(nameof(GpoDcVersionEvidence.Versions)),
            Width = new DataGridLength(1.5, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "AD evidence", Binding = new Binding(nameof(GpoDcVersionEvidence.AdEvidence)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "SYSVOL evidence", Binding = new Binding(nameof(GpoDcVersionEvidence.SysvolEvidence)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });
        UiStyle.ApplyDataGridDefaults(grid);
        outer.Children.Add(grid);
        Content = outer;

        GpoCrossDcConsistencyReport? last = null;
        run.Click += async (_, _) =>
        {
            string[] dcs;
            try
            {
                dcs = GpoCrossDcConsistencyService.ValidateControllers(hosts.Text, gpo.DomainName);
            }
            catch (Exception ex)
            {
                outcome.Text = ex.Message;
                outcome.Foreground = UiStyle.WarningBrush;
                return;
            }

            run.IsEnabled = false;
            copy.IsEnabled = false;
            export.IsEnabled = false;
            outcome.Text = "Reading AD and DC-specific SYSVOL version evidence...";
            try
            {
                last = await Task.Run(() =>
                    GpoCrossDcConsistencyService.Compare(gpo, domainDn, dcs));
                grid.ItemsSource = last.Controllers;
                outcome.Text = last.Summary;
                outcome.Foreground = last.Controllers.All(x => x.Status == "Match") &&
                    last.Controllers.Count > 1 ? UiStyle.SuccessBrush : UiStyle.WarningBrush;
                copy.IsEnabled = true;
                export.IsEnabled = true;
            }
            catch (Exception ex)
            {
                outcome.Text = "Cross-DC comparison incomplete: " + ex.Message;
                outcome.Foreground = UiStyle.WarningBrush;
            }
            finally
            {
                run.IsEnabled = true;
            }
        };
        copy.Click += (_, _) =>
        {
            if (last is not null)
                Clipboard.SetText(last.ToText());
        };
        export.Click += (_, _) =>
        {
            if (last is null)
                return;
            var dialog = new SaveFileDialog
            {
                Filter = "Text report (*.txt)|*.txt",
                FileName = $"GPO-CrossDC-{gpo.Id:N}.txt",
                AddExtension = true
            };
            if (dialog.ShowDialog(this) == true)
                File.WriteAllText(dialog.FileName, last.ToText());
        };
        close.Click += (_, _) => Close();
    }
}
