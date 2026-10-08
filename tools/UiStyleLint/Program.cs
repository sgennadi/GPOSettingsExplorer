using System.Text.RegularExpressions;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: UiStyleLint <source-directory>");
    return 2;
}

var root = Path.GetFullPath(args[0]);
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"UI lint source directory not found: {root}");
    return 2;
}

var violations = new List<string>();

var files = Directory
    .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
    .Where(path =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
    .Where(path =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
    .ToArray();

foreach (var file in files)
{
    var relative = Path.GetRelativePath(root, file);
    var text = File.ReadAllText(file);

    if (relative.Equals("UiStyle.cs", StringComparison.OrdinalIgnoreCase) ||
        relative.Equals(
            Path.Combine("Themes", "WindowsCompact.xaml"),
            StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    Check(
        relative,
        text,
        @"ResizeMode\s*=\s*ResizeMode\.NoResize|ResizeMode\s*=\s*""NoResize""",
        "Do not use ResizeMode.NoResize. Windows must remain resizable at high DPI.");

    Check(
        relative,
        text,
        @"new\s+(?:System\.Windows\.Media\.)?FontFamily\s*\(",
        "Do not create local FontFamily instances. Use UiStyle or the shared theme.");

    Check(
        relative,
        text,
        @"FontSize\s*=\s*\d+(?:\.\d+)?\s*[,;]|FontSize\s*=\s*""\d+(?:\.\d+)?""",
        "Do not hard-code local font sizes. Use UiStyle or the shared theme.");

    Check(
        relative,
        text,
        @"TextTrimming\s*=\s*""(?:CharacterEllipsis|WordEllipsis)""|TextTrimming\s*=\s*TextTrimming\.(?:CharacterEllipsis|WordEllipsis)",
        "Do not trim UI text with ellipsis. Text must wrap and remain readable.");

    CheckMultilineTextBoxHeight(
        relative,
        text);

    if (relative.Equals(
            "MainWindow.xaml",
            StringComparison.OrdinalIgnoreCase))
    {
        CheckOverviewCheckBoxColumns(
            relative,
            text);
    }
}

var manifest = Path.Combine(root, "app.manifest");
if (File.Exists(manifest))
{
    var manifestText = File.ReadAllText(manifest);
    if (!manifestText.Contains(
            "PerMonitorV2",
            StringComparison.OrdinalIgnoreCase))
    {
        violations.Add(
            "app.manifest: PerMonitorV2 DPI awareness is required.");
    }
}
else
{
    violations.Add("app.manifest: required DPI manifest is missing.");
}

var appXaml = Path.Combine(root, "App.xaml");
if (File.Exists(appXaml))
{
    var appText = File.ReadAllText(appXaml);
    if (!appText.Contains(
            "Themes/WindowsCompact.xaml",
            StringComparison.OrdinalIgnoreCase))
    {
        violations.Add(
            "App.xaml: shared Themes/WindowsCompact.xaml must be merged.");
    }
}

var appCode = Path.Combine(root, "App.xaml.cs");
if (File.Exists(appCode))
{
    var appCodeText = File.ReadAllText(appCode);
    if (!appCodeText.Contains(
            "AdaptiveWindowManager.Register()",
            StringComparison.Ordinal))
    {
        violations.Add(
            "App.xaml.cs: AdaptiveWindowManager.Register() is required.");
    }
}

if (violations.Count == 0)
{
    Console.WriteLine(
        $"UI lint passed for {files.Length} C#/XAML files.");
    return 0;
}

Console.Error.WriteLine("UI lint failed:");
foreach (var violation in violations)
{
    Console.Error.WriteLine(" - " + violation);
}

return 1;

void Check(
    string relative,
    string text,
    string pattern,
    string message)
{
    var regex = new Regex(
        pattern,
        RegexOptions.CultureInvariant);

    foreach (Match match in regex.Matches(text))
    {
        var line =
            1 + text.AsSpan(0, match.Index).Count('\n');

        violations.Add(
            $"{relative}:{line}: {message}");
    }
}

void CheckOverviewCheckBoxColumns(
    string relative,
    string text)
{
    var columns =
        new Regex(
            @"<DataGridCheckBoxColumn\b(?<attrs>[\s\S]*?)/>",
            RegexOptions.CultureInvariant);

    foreach (Match match in columns.Matches(text))
    {
        var attrs =
            match.Groups["attrs"].Value;

        if (!Regex.IsMatch(
                attrs,
                @"Binding\s*=\s*""\{Binding\s+[^""]*\bMode\s*=\s*OneWay\b[^""]*\}""",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant))
        {
            var line =
                1 + text.AsSpan(0, match.Index).Count('\n');

            violations.Add(
                $"{relative}:{line}: MainWindow DataGridCheckBoxColumn bindings must use Mode=OneWay so computed/read-only properties cannot create TwoWay binding failures.");
        }

        if (!Regex.IsMatch(
                attrs,
                @"IsReadOnly\s*=\s*""True""",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant))
        {
            var line =
                1 + text.AsSpan(0, match.Index).Count('\n');

            violations.Add(
                $"{relative}:{line}: MainWindow DataGridCheckBoxColumn must be IsReadOnly=True. Changes belong in the dedicated editor/action workflow.");
        }
    }
}

void CheckMultilineTextBoxHeight(
    string relative,
    string text)
{
    var csTextBox = new Regex(
        @"new\s+TextBox\s*\{(?<body>.*?)\}",
        RegexOptions.Singleline |
        RegexOptions.CultureInvariant);

    foreach (Match match in csTextBox.Matches(text))
    {
        var body = match.Groups["body"].Value;

        if (!Regex.IsMatch(
                body,
                @"AcceptsReturn\s*=\s*true",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant))
        {
            continue;
        }

        if (!Regex.IsMatch(
                body,
                @"(?<!Min)Height\s*=\s*\d+(?:\.\d+)?",
                RegexOptions.CultureInvariant))
        {
            continue;
        }

        var line =
            1 + text.AsSpan(0, match.Index).Count('\n');

        violations.Add(
            $"{relative}:{line}: Multiline TextBox must use MinHeight, not fixed Height.");
    }

    var xamlTextBox = new Regex(
        @"<TextBox\b(?<attrs>[^>]*AcceptsReturn\s*=\s*""True""[^>]*)>",
        RegexOptions.IgnoreCase |
        RegexOptions.Singleline |
        RegexOptions.CultureInvariant);

    foreach (Match match in xamlTextBox.Matches(text))
    {
        var attrs = match.Groups["attrs"].Value;

        if (!Regex.IsMatch(
                attrs,
                @"(?<!Min)Height\s*=\s*""\d+(?:\.\d+)?""",
                RegexOptions.CultureInvariant))
        {
            continue;
        }

        var line =
            1 + text.AsSpan(0, match.Index).Count('\n');

        violations.Add(
            $"{relative}:{line}: Multiline TextBox must use MinHeight, not fixed Height.");
    }
}
