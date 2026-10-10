namespace GPOSettingsExplorer.Services;

/// <summary>
/// Labels known registry policy namespace families in source-file evidence.
/// Classification does NOT decode Firewall/AppLocker/CSE formats or claim a
/// setting is applied on a client. Unknown paths retain a generic label.
/// </summary>
public static class RegistryPolicySourceClassifier
{
    private static readonly (string Prefix, string Label)[] Prefixes =
    {
        (@"Software\Policies\Microsoft\WindowsFirewall",
            "Windows Firewall (stored registry source)"),
        (@"Software\Policies\Microsoft\Windows\SrpV2",
            "AppLocker (stored registry source)"),
        (@"Software\Policies\Microsoft\Windows\Safer\CodeIdentifiers",
            "Software Restriction Policies (stored registry source)"),
        (@"Software\Policies\Microsoft\Windows Defender",
            "Microsoft Defender (stored registry source)"),
        (@"Software\Policies\Microsoft\Windows\WindowsUpdate",
            "Windows Update (stored registry source)"),
        (@"Software\Policies\Microsoft\Edge",
            "Microsoft Edge ADMX (stored registry source)"),
        (@"Software\Policies\Google\Chrome",
            "Google Chrome ADMX (stored registry source)"),
        (@"Software\Policies\Microsoft\Windows NT\Terminal Services",
            "Remote Desktop Services (stored registry source)")
    };

    public static string Classify(string registryKey, bool specialOperation)
    {
        if (specialOperation)
            return "Registry policy operations";
        if (string.IsNullOrWhiteSpace(registryKey))
            return "Registry policy (source file)";
        var key = registryKey.Replace('/', '\\').TrimStart('\\');
        foreach (var root in new[]
        {
            "MACHINE\\", "USER\\", "HKEY_LOCAL_MACHINE\\",
            "HKEY_CURRENT_USER\\", "HKLM\\", "HKCU\\"
        })
            if (key.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                key = key[root.Length..];
                break;
            }

        foreach (var (prefix, label) in Prefixes)
            if (key.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
                return label;
        return "Registry policy (source file)";
    }
}