using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GPOSettingsExplorer.Services;

public static class AlternateCredentialLauncher
{
    private const int LogonNetCredentialsOnly =
        0x00000002;

    private const int CreateUnicodeEnvironment =
        0x00000400;

    public static void Relaunch(
        string userName,
        string password,
        string domainName,
        string domainController)
    {
        ParseUserName(
            userName,
            domainName,
            out var user,
            out var domain);

        var executable =
            Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "The current executable path is unavailable.");

        var commandLine =
            new StringBuilder();

        commandLine.Append(
            '"')
            .Append(
                executable)
            .Append(
                '"');

        AppendArgument(
            commandLine,
            "--connected-session");

        AppendArgument(
            commandLine,
            "--domain",
            domainName);

        if (!string.IsNullOrWhiteSpace(
                domainController))
        {
            AppendArgument(
                commandLine,
                "--dc",
                domainController);
        }

        var startup =
            new StartupInfo
            {
                Size =
                    Marshal.SizeOf<StartupInfo>()
            };

        if (!CreateProcessWithLogonW(
                user,
                domain,
                password,
                LogonNetCredentialsOnly,
                executable,
                commandLine,
                CreateUnicodeEnvironment,
                IntPtr.Zero,
                AppContext.BaseDirectory,
                ref startup,
                out var process))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to relaunch with the supplied network credentials.");
        }

        try
        {
            if (process.ProcessHandle !=
                IntPtr.Zero)
            {
                CloseHandle(
                    process.ProcessHandle);
            }

            if (process.ThreadHandle !=
                IntPtr.Zero)
            {
                CloseHandle(
                    process.ThreadHandle);
            }
        }
        catch
        {
        }
    }

    private static void ParseUserName(
        string input,
        string defaultDomain,
        out string user,
        out string? domain)
    {
        var value =
            input.Trim();

        var slash =
            value.IndexOf(
                '\\');

        if (slash > 0)
        {
            domain =
                value[..slash];

            user =
                value[(slash + 1)..];

            return;
        }

        var at =
            value.IndexOf(
                '@');

        if (at > 0)
        {
            user =
                value;

            domain =
                null;

            return;
        }

        if (!string.IsNullOrWhiteSpace(
                defaultDomain))
        {
            user =
                $"{value}@{defaultDomain}";

            domain =
                null;

            return;
        }

        user =
            value;

        domain =
            null;
    }

    private static void AppendArgument(
        StringBuilder builder,
        string name,
        string? value = null)
    {
        builder.Append(
            ' ');

        builder.Append(
            name);

        if (value is null)
            return;

        builder.Append(
            " \"");

        builder.Append(
            value.Replace(
                "\"",
                "\\\"",
                StringComparison.Ordinal));

        builder.Append(
            '\"');
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithLogonW(
        string userName,
        string? domain,
        string password,
        int logonFlags,
        string applicationName,
        StringBuilder commandLine,
        int creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(
        IntPtr handle);
}
