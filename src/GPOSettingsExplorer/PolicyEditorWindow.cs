using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class PolicyEditorWindow : Window
{
    private readonly AdmxPolicyDefinition _definition;
    private readonly PolicyEditSession _session;
    private readonly ComboBox _stateCombo;
    private readonly Dictionary<string, Control> _editors = new(StringComparer.OrdinalIgnoreCase);

    public PolicyEditSession Session => _session;

    public PolicyEditorWindow(
        GpoInfo gpo,
        PolicySettingInfo setting,
        AdmxPolicyDefinition definition,
        PolicyEditSession session)
    {
        _definition = definition;
        _session = session;

        Title = $"Edit Policy - {setting.SettingName}";
        Width = 900;
        Height = 760;
        MinWidth = 720;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(14) };

        var footer = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var apply = new Button { Content = "Apply", IsDefault = true };
        apply.Click += Apply_Click;
        footer.Children.Add(cancel);
        footer.Children.Add(apply);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = setting.SettingName,
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 4, 8),
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(Meta("GPO", gpo.DisplayName));
        panel.Children.Add(Meta("Scope", setting.Scope));
        panel.Children.Add(Meta("Category", string.IsNullOrWhiteSpace(definition.Category) ? setting.Category : definition.Category));
        panel.Children.Add(Meta("ADMX", definition.AdmxFile));
        panel.Children.Add(Meta("Registry", BuildRegistrySummary(definition)));

        if (!string.IsNullOrWhiteSpace(definition.ExplainText))
        {
            panel.Children.Add(new GroupBox
            {
                Header = "Help",
                Margin = new Thickness(4, 10, 4, 8),
                Content = new TextBlock
                {
                    Text = definition.ExplainText,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8)
                }
            });
        }

        var statePanel = new Grid { Margin = new Thickness(4, 10, 4, 10) };
        statePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        statePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var stateLabel = new TextBlock
        {
            Text = "Policy state:",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };

        _stateCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<PolicyEditState>(),
            SelectedItem = session.State,
            MinWidth = 220
        };
        _stateCombo.SelectionChanged += (_, _) => UpdateEditorsEnabledState();

        Grid.SetColumn(stateLabel, 0);
        Grid.SetColumn(_stateCombo, 1);
        statePanel.Children.Add(stateLabel);
        statePanel.Children.Add(_stateCombo);
        panel.Children.Add(statePanel);

        if (definition.Elements.Count > 0)
        {
            var settingsPanel = new StackPanel { Margin = new Thickness(4, 0, 4, 8) };

            foreach (var element in definition.Elements)
            {
                settingsPanel.Children.Add(BuildElementEditor(element));
            }

            panel.Children.Add(new GroupBox
            {
                Header = "Setting values",
                Content = settingsPanel,
                Margin = new Thickness(4)
            });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "This policy has no additional editable values.",
                Margin = new Thickness(8),
                Foreground = System.Windows.Media.Brushes.DimGray
            });
        }

        panel.Children.Add(new Border
        {
            Margin = new Thickness(4, 10, 4, 10),
            Padding = new Thickness(8),
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "Before a change is written, GPO Settings Explorer creates a GPMC backup of the selected GPO. Registry-based Administrative Template values are saved through the Windows Group Policy API.",
                TextWrapping = TextWrapping.Wrap
            }
        });

        scroll.Content = panel;
        root.Children.Add(footer);
        root.Children.Add(scroll);
        Content = root;

        UpdateEditorsEnabledState();

        Loaded += (_, _) =>
        {
            var target =
                FindPreferredValueEditor(
                    setting,
                    definition);

            target.BringIntoView();
            target.Focus();

            if (target is TextBox textBox)
            {
                textBox.SelectAll();
            }
        };
    }

    private FrameworkElement BuildElementEditor(AdmxElementDefinition element)
    {
        var grid = new Grid { Margin = new Thickness(6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(element.Label) ? element.Id : element.Label,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 12, 0)
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        Control editor;
        _session.Values.TryGetValue(element.Id, out var currentValue);

        switch (element.Type)
        {
            case AdmxElementType.Boolean:
                editor = new CheckBox
                {
                    IsChecked = currentValue is null ? false : Convert.ToBoolean(currentValue),
                    VerticalAlignment = VerticalAlignment.Center
                };
                break;

            case AdmxElementType.Enum:
                var combo = new ComboBox
                {
                    ItemsSource = element.Choices,
                    DisplayMemberPath = nameof(AdmxEnumChoice.DisplayName),
                    MinWidth = 280
                };

                if (currentValue is not null)
                {
                    combo.SelectedItem = element.Choices.FirstOrDefault(choice =>
                        ValuesEqual(choice.RegistryValue.Value, currentValue));
                }

                combo.SelectedItem ??= element.Choices.FirstOrDefault();
                editor = combo;
                break;

            case AdmxElementType.Decimal:
                editor = new TextBox
                {
                    Text = currentValue is null
                        ? string.Empty
                        : Convert.ToString(currentValue, CultureInfo.InvariantCulture) ?? string.Empty
                };
                break;

            case AdmxElementType.List:
                editor = new TextBox
                {
                    Text = currentValue is IEnumerable<string> values
                        ? string.Join(Environment.NewLine, values)
                        : Convert.ToString(currentValue) ?? string.Empty,
                    AcceptsReturn = true,
                    MinHeight = 110,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                break;

            default:
                editor = new TextBox
                {
                    Text = Convert.ToString(currentValue) ?? string.Empty,
                    MinWidth = 300
                };
                break;
        }

        editor.Tag = element;
        _editors[element.Id] = editor;
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        if (element.Type == AdmxElementType.Decimal &&
            (element.MinValue.HasValue || element.MaxValue.HasValue))
        {
            var hint = new TextBlock
            {
                Text = $"Allowed: {element.MinValue?.ToString(CultureInfo.InvariantCulture) ?? "-∞"} .. {element.MaxValue?.ToString(CultureInfo.InvariantCulture) ?? "+∞"}",
                Foreground = System.Windows.Media.Brushes.DimGray,
                Margin = new Thickness(0, 3, 0, 0)
            };

            var stack = new StackPanel();
            grid.Children.Remove(editor);
            stack.Children.Add(editor);
            stack.Children.Add(hint);
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);
        }

        return grid;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_stateCombo.SelectedItem is not PolicyEditState state)
        {
            return;
        }

        _session.State = state;

        if (state == PolicyEditState.Enabled)
        {
            foreach (var element in _definition.Elements)
            {
                if (!_editors.TryGetValue(element.Id, out var editor))
                {
                    continue;
                }

                try
                {
                    _session.Values[element.Id] = ReadEditorValue(editor, element);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        $"{element.Label}: {ex.Message}",
                        "Invalid value",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    editor.Focus();
                    return;
                }
            }
        }

        DialogResult = true;
    }

    private static object? ReadEditorValue(Control editor, AdmxElementDefinition element)
    {
        switch (element.Type)
        {
            case AdmxElementType.Boolean:
                return ((CheckBox)editor).IsChecked == true;

            case AdmxElementType.Enum:
                var selected = ((ComboBox)editor).SelectedItem as AdmxEnumChoice;
                if (element.Required && selected is null)
                {
                    throw new InvalidOperationException("Select a value.");
                }
                return selected;

            case AdmxElementType.Decimal:
                var decimalText = ((TextBox)editor).Text.Trim();
                if (string.IsNullOrWhiteSpace(decimalText))
                {
                    if (element.Required)
                    {
                        throw new InvalidOperationException("A numeric value is required.");
                    }
                    return null;
                }

                if (!long.TryParse(decimalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    throw new InvalidOperationException("Enter a valid integer.");
                }

                if (element.MinValue.HasValue && number < element.MinValue.Value)
                {
                    throw new InvalidOperationException($"Minimum value is {element.MinValue.Value}.");
                }

                if (element.MaxValue.HasValue && number > element.MaxValue.Value)
                {
                    throw new InvalidOperationException($"Maximum value is {element.MaxValue.Value}.");
                }

                return number;

            case AdmxElementType.List:
                return ((TextBox)editor).Text
                    .Split(new[] { "\r\n", "\n" },
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            default:
                var text = ((TextBox)editor).Text;
                if (element.Required && string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidOperationException("A value is required.");
                }

                return string.IsNullOrEmpty(text) ? null : text;
        }
    }

    private void UpdateEditorsEnabledState()
    {
        var enabled = _stateCombo.SelectedItem is PolicyEditState.Enabled;
        foreach (var editor in _editors.Values)
        {
            editor.IsEnabled = enabled;
        }
    }

    private Control FindPreferredValueEditor(
        PolicySettingInfo setting,
        AdmxPolicyDefinition definition)
    {
        if (_session.State == PolicyEditState.Enabled)
        {
            var wantedValueName =
                NormalizeValueName(
                    setting.RegistryValue);

            if (!string.IsNullOrWhiteSpace(wantedValueName))
            {
                var matchingElement =
                    definition.Elements.FirstOrDefault(element =>
                        NormalizeValueName(element.ValueName)
                            .Equals(
                                wantedValueName,
                                StringComparison.OrdinalIgnoreCase));

                if (matchingElement is not null &&
                    _editors.TryGetValue(
                        matchingElement.Id,
                        out var matchingEditor))
                {
                    return matchingEditor;
                }
            }

            var firstValueEditor =
                _editors.Values.FirstOrDefault();

            if (firstValueEditor is not null)
            {
                return firstValueEditor;
            }
        }

        return _stateCombo;
    }

    private static string NormalizeValueName(
        string value)
    {
        var text =
            (value ?? string.Empty)
            .Trim();

        var equals =
            text.IndexOf('=');

        return equals >= 0
            ? text[(equals + 1)..].Trim()
            : text;
    }

    private static FrameworkElement Meta(string name, string value)
    {
        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new TextBlock { Text = name + ":", FontWeight = FontWeights.SemiBold };
        var right = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static string BuildRegistrySummary(AdmxPolicyDefinition definition)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(definition.Key))
        {
            parts.Add(definition.Key);
        }

        if (!string.IsNullOrWhiteSpace(definition.ValueName))
        {
            parts.Add(definition.ValueName);
        }

        if (parts.Count == 0 && definition.Elements.Count > 0)
        {
            var first = definition.Elements.First();
            parts.Add(first.Key);
            if (!string.IsNullOrWhiteSpace(first.ValueName))
            {
                parts.Add(first.ValueName);
            }
        }

        return string.Join(" \\ ", parts);
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (long.TryParse(Convert.ToString(left), out var leftNumber) &&
            long.TryParse(Convert.ToString(right), out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(
            Convert.ToString(left),
            Convert.ToString(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
