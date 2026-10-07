using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace GPOSettingsExplorer.Services;

public static class ItemLevelTargetingUi
{
    public static void Attach(
        Window window)
    {
        foreach (var tab in FindLogicalChildren<TabItem>(
                     window))
        {
            var header =
                Convert.ToString(
                    tab.Header)
                ?? string.Empty;

            if (!header.Contains(
                    "Item-level Targeting",
                    StringComparison.OrdinalIgnoreCase) ||
                tab.Content is not DockPanel panel)
            {
                continue;
            }

            if (panel.Children
                .OfType<Button>()
                .Any(
                    button =>
                        Convert.ToString(
                            button.Content)
                        ?.Equals(
                            "Visual editor...",
                            StringComparison.OrdinalIgnoreCase)
                        ==
                        true))
            {
                continue;
            }

            var textBox =
                FindLogicalChildren<TextBox>(
                        panel)
                    .FirstOrDefault(
                        box =>
                            box.AcceptsReturn);

            if (textBox is null)
                continue;

            var button =
                new Button
                {
                    Content =
                        "Visual editor...",
                    HorizontalAlignment =
                        HorizontalAlignment.Left,
                    Margin =
                        new Thickness(
                            4,
                            0,
                            4,
                            8),
                    ToolTip =
                        "Edit item-level targeting as a visual condition tree. Raw XML remains available below."
                };

            DockPanel.SetDock(
                button,
                Dock.Top);

            button.Click +=
                (_, _) =>
                {
                    try
                    {
                        var editor =
                            new ItemLevelTargetingWindow(
                                textBox.Text)
                            {
                                Owner =
                                    window
                            };

                        if (editor.ShowDialog() ==
                            true)
                        {
                            textBox.Text =
                                editor.FiltersXml;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            window,
                            ex.Message,
                            "Item-level Targeting",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                };

            var insertIndex =
                Math.Min(
                    1,
                    panel.Children.Count);

            panel.Children.Insert(
                insertIndex,
                button);
        }
    }

    private static IEnumerable<T> FindLogicalChildren<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            yield return match;
        }

        IEnumerable children;

        try
        {
            children =
                LogicalTreeHelper.GetChildren(
                    root);
        }
        catch
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (child is not DependencyObject dependency)
                continue;

            foreach (var nested in FindLogicalChildren<T>(
                         dependency))
            {
                yield return nested;
            }
        }
    }
}
