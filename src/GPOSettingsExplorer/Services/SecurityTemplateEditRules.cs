using System.Globalization;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Finite, explicitly reviewed SecEdit settings only. No arbitrary INF/ACL/SID/rights
/// editor: unknown sections, unknown fields and malformed data fail closed.
/// </summary>
public sealed record SecurityEditChoice(int Value, string Label);

public sealed record SecurityEditSpecification(
    string Section, string Key, int CurrentValue,
    IReadOnlyList<SecurityEditChoice> Choices, string Warning)
{
    public bool Allows(int value) => Choices.Any(choice => choice.Value == value);
}

public static class SecurityTemplateEditRules
{
    private static readonly HashSet<string> AuditKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "AuditSystemEvents", "AuditLogonEvents", "AuditObjectAccess",
        "AuditPrivilegeUse", "AuditPolicyChange", "AuditAccountManage",
        "AuditProcessTracking", "AuditDSAccess", "AuditAccountLogon"
    };

    private static readonly HashSet<string> BooleanAccessKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordComplexity", "ClearTextPassword"
    };

    private static readonly SecurityEditChoice[] AuditChoices =
    {
        new(0, "0 - No auditing"),
        new(1, "1 - Success"),
        new(2, "2 - Failure"),
        new(3, "3 - Success and Failure")
    };

    private static readonly SecurityEditChoice[] BooleanChoices =
    {
        new(0, "0 - Disabled"),
        new(1, "1 - Enabled")
    };

    public static bool TryDescribe(RealSettingRecord record, out SecurityEditSpecification? result)
    {
        result = null;
        if (record.Scope != "Computer" || record.State != "Stored template value" ||
            record.ValueType != "INF string" ||
            !Path.GetFileName(record.SourceFile).Equals("GptTmpl.inf",
                StringComparison.OrdinalIgnoreCase) ||
            record.SourceSha256.Length != 64 ||
            !int.TryParse(record.Value.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var current))
            return false;

        const string prefix = "Security template > ";
        if (!record.Category.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var section = record.Category[prefix.Length..];
        IReadOnlyList<SecurityEditChoice>? choices = null;
        string warning;
        if (section.Equals("Event Audit", StringComparison.OrdinalIgnoreCase) &&
            AuditKeys.Contains(record.SettingName))
        {
            choices = AuditChoices;
            warning = "Legacy audit policy may be superseded by Advanced Audit Policy Configuration. " +
                "Confirm the effective policy with gpresult/auditpol on target computers.";
        }
        else if (section.Equals("System Access", StringComparison.OrdinalIgnoreCase) &&
                 BooleanAccessKeys.Contains(record.SettingName))
        {
            choices = BooleanChoices;
            warning = "Password policy changes can affect domain accounts. Check fine-grained password " +
                "policies, GPO linking and target scope before writing.";
        }
        else
            return false;

        if (!choices.Any(choice => choice.Value == current))
            return false;

        result = new SecurityEditSpecification(section, record.SettingName,
            current, choices, warning);
        return true;
    }

    public static string ChangeExistingValue(
        string source, SecurityEditSpecification rule, int selectedValue)
    {
        if (!rule.Allows(selectedValue))
            throw new ArgumentOutOfRangeException(nameof(selectedValue),
                "The proposed value is not supported by this security editor.");

        // Preserve existing newline style and unrelated sections and comments.
        var crlf = source.Contains("\r\n", StringComparison.Ordinal);
        var withoutCrLf = source.Replace("\r\n", "", StringComparison.Ordinal);
        if (crlf && (withoutCrLf.Contains('\n') || withoutCrLf.Contains('\r')))
            throw new InvalidDataException("Mixed line endings; refusing a non-lossless edit.");

        string newline = crlf ? "\r\n" :
            source.Contains('\n') ? "\n" : "\r";
        var lines = source.Split(new[] { newline }, StringSplitOptions.None);
        var section = "";
        var count = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('['))
            {
                if (!trimmed.EndsWith(']'))
                    throw new InvalidDataException("Malformed security section header.");
                section = trimmed[1..^1].Trim();
                continue;
            }
            if (!section.Equals(rule.Section, StringComparison.OrdinalIgnoreCase))
                continue;
            var index = lines[i].IndexOf('=');
            if (index <= 0 || !lines[i][..index].Trim().Equals(rule.Key,
                StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(lines[i][(index + 1)..].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var stored) ||
                stored != rule.CurrentValue)
                throw new InvalidDataException("The original value differs from the inspected source.");

            // Preserve indentation and whitespace to the left of '='.
            lines[i] = lines[i][..(index + 1)] +
                       selectedValue.ToString(CultureInfo.InvariantCulture);
            count++;
        }
        if (count != 1)
            throw new InvalidDataException(
                $"Expected one existing [{rule.Section}] {rule.Key}; found {count}. Nothing was added.");
        return string.Join(newline, lines);
    }

    /// <summary>
    /// Change exactly ONE existing DWORD boolean Registry Values entry. Never
    /// create policy entries or normalize unsupported types. Unrelated content
    /// remains byte-for-byte identical after encoding by the caller.
    /// </summary>
    public static string ChangeExistingRegistryBoolean(
        string source, string target, bool expectedOld, bool proposed)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(target) ||
            !target.StartsWith("MACHINE\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A complete machine registry policy key is required.");

        var hasCrLf = source.Contains("\r\n", StringComparison.Ordinal);
        var remaining = source.Replace("\r\n", "", StringComparison.Ordinal);
        if (hasCrLf && (remaining.Contains('\r') || remaining.Contains('\n')))
            throw new InvalidDataException("Mixed line endings are unsafe to preserve.");
        var newline = hasCrLf ? "\r\n" : source.Contains('\n') ? "\n" : "\r";
        var lines = source.Split(new[] { newline }, StringSplitOptions.None);
        var section = "";
        var found = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('['))
            {
                if (!trimmed.EndsWith(']'))
                    throw new InvalidDataException("Malformed security section header.");
                section = trimmed[1..^1].Trim();
                continue;
            }

            if (!section.Equals("Registry Values", StringComparison.OrdinalIgnoreCase))
                continue;
            var equals = lines[i].IndexOf('=');
            if (equals <= 0 ||
                !lines[i][..equals].Trim().Equals(target, StringComparison.OrdinalIgnoreCase))
                continue;

            var values = lines[i][(equals + 1)..].Trim().Split(',');
            if (values.Length != 2 ||
                !int.TryParse(values[0].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var type) ||
                type != 4 ||
                !int.TryParse(values[1].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var current) ||
                current is not (0 or 1) || (current == 1) != expectedOld)
                throw new InvalidDataException(
                    "Expected an existing DWORD boolean with exactly the displayed value.");

            lines[i] = lines[i][..(equals + 1)] +
                "4," + (proposed ? "1" : "0");
            found++;
        }

        if (found != 1)
            throw new InvalidDataException(
                $"Expected exactly one existing Registry Values entry for {target}; found {found}.");
        return string.Join(newline, lines);
    }
}
