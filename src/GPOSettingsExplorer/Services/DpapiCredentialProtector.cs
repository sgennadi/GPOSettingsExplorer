using System.Runtime.InteropServices;
using System.Text;

namespace GPOSettingsExplorer.Services;

public static class DpapiCredentialProtector
{
    private const int CryptprotectUiForbidden =
        0x1;

    private const int CryptprotectLocalMachine =
        0x4;

    public static string Protect(
        string plainText,
        CredentialPersistenceScope scope)
    {
        if (scope ==
            CredentialPersistenceScope.None)
        {
            return string.Empty;
        }

        var bytes =
            Encoding.UTF8.GetBytes(
                plainText ?? string.Empty);

        var input =
            ToBlob(
                bytes);

        var output =
            default(DataBlob);

        try
        {
            var flags =
                CryptprotectUiForbidden |
                (scope ==
                 CredentialPersistenceScope.LocalMachine
                    ? CryptprotectLocalMachine
                    : 0);

            if (!CryptProtectData(
                    ref input,
                    "GPOSettingsExplorer",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    flags,
                    ref output))
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            var protectedBytes =
                new byte[output.Size];

            Marshal.Copy(
                output.Data,
                protectedBytes,
                0,
                protectedBytes.Length);

            return Convert.ToBase64String(
                protectedBytes);
        }
        finally
        {
            FreeBlob(
                ref input);

            if (output.Data !=
                IntPtr.Zero)
            {
                LocalFree(
                    output.Data);
            }
        }
    }

    public static string Unprotect(
        string protectedText,
        CredentialPersistenceScope scope)
    {
        if (string.IsNullOrWhiteSpace(
                protectedText) ||
            scope ==
            CredentialPersistenceScope.None)
        {
            return string.Empty;
        }

        var bytes =
            Convert.FromBase64String(
                protectedText);

        var input =
            ToBlob(
                bytes);

        var output =
            default(DataBlob);

        try
        {
            var flags =
                CryptprotectUiForbidden |
                (scope ==
                 CredentialPersistenceScope.LocalMachine
                    ? CryptprotectLocalMachine
                    : 0);

            if (!CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    flags,
                    ref output))
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            var plain =
                new byte[output.Size];

            Marshal.Copy(
                output.Data,
                plain,
                0,
                plain.Length);

            return Encoding.UTF8.GetString(
                plain);
        }
        finally
        {
            FreeBlob(
                ref input);

            if (output.Data !=
                IntPtr.Zero)
            {
                LocalFree(
                    output.Data);
            }
        }
    }

    private static DataBlob ToBlob(
        byte[] data)
    {
        if (data.Length == 0)
        {
            return new DataBlob();
        }

        var pointer =
            Marshal.AllocHGlobal(
                data.Length);

        Marshal.Copy(
            data,
            0,
            pointer,
            data.Length);

        return new DataBlob
        {
            Size =
                data.Length,
            Data =
                pointer
        };
    }

    private static void FreeBlob(
        ref DataBlob blob)
    {
        if (blob.Data ==
            IntPtr.Zero)
        {
            return;
        }

        Marshal.FreeHGlobal(
            blob.Data);

        blob.Data =
            IntPtr.Zero;

        blob.Size =
            0;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport(
        "crypt32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport(
        "crypt32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    private static extern IntPtr LocalFree(
        IntPtr memory);
}
