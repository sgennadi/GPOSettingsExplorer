using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Prevents source comparisons from asserting equality when a display-only
/// projection was truncated, redacted or only partly decoded.
/// Full file SHA-256 is separate source evidence; do not conflate the two.
/// </summary>
public static class GpoSourceValueCompleteness
{
    public static bool IsExactProjection(RealSettingRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (string.IsNullOrWhiteSpace(row.ValueType))
            return false;

        if (row.ValueType.Contains("partial projection",
                StringComparison.OrdinalIgnoreCase) ||
            row.Value.Contains("[REDACTED", StringComparison.OrdinalIgnoreCase) ||
            row.Value.Contains("[truncated", StringComparison.OrdinalIgnoreCase))
            return false;

        // Registry.pol binary previews show at most 48 bytes; two long
        // different values can have identical displayed prefixes. Such a
        // preview MUST NOT be treated as a full stored-value match.
        if (row.ValueType.StartsWith("REG_", StringComparison.OrdinalIgnoreCase) &&
            row.Value.Contains("... (", StringComparison.Ordinal))
            return false;

        return true;
    }
}
