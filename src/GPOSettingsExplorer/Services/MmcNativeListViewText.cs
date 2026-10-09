using System.Text;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Decodes exactly the number of UTF-16 characters returned by LVM_GETITEMTEXTW.
/// Win32 reuses the remote pszText buffer, so everything past that length can
/// contain fragments from previously read policies and must be ignored.
/// </summary>
public static class MmcNativeListViewText
{
    public static string DecodeUtf16(byte[] buffer, int characterCount)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (characterCount < 0 || characterCount > buffer.Length / sizeof(char))
            throw new ArgumentOutOfRangeException(nameof(characterCount));

        return characterCount == 0
            ? string.Empty
            : Encoding.Unicode.GetString(buffer, 0, characterCount * sizeof(char))
                .TrimEnd('\0').Trim();
    }

    public static string PolicyColumn(string? rowText) =>
        (rowText ?? string.Empty).Split(" | ", 2, StringSplitOptions.None)[0].Trim();
}
