using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MTC;

internal static class UnixAutoDetach
{
    private const string DetachedEnvVar = "MTC_UNIX_DETACHED";

    public static bool TryRelaunchDetached(string[] args)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return false;

        if (string.Equals(Environment.GetEnvironmentVariable(DetachedEnvVar), "1", StringComparison.Ordinal))
            return false;

        if (!IsForegroundTerminalProcess())
            return false;

        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        try
        {
            var startInfo = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(BuildShellCommand(processPath, args));
            using Process? child = Process.Start(startInfo);
            return child != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryLaunchAdditionalInstance(string[] args, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return false;

        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            processPath = Process.GetCurrentProcess().MainModule?.FileName;

        if (string.IsNullOrWhiteSpace(processPath))
        {
            error = "current executable path is unavailable";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(BuildShellCommand(processPath, args));
            using Process? child = Process.Start(startInfo);
            if (child == null)
            {
                error = "launcher process failed to start";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string BuildShellCommand(string processPath, IReadOnlyList<string> args)
    {
        var parts = new List<string>(args.Count + 2)
        {
            $"{DetachedEnvVar}=1",
            ShellQuote(processPath),
        };

        foreach (string arg in args)
            parts.Add(ShellQuote(arg));

        return string.Join(" ", parts) + " </dev/null >/dev/null 2>&1 &";
    }

    private static bool IsForegroundTerminalProcess()
    {
        try
        {
            int processGroup = getpgrp();
            if (processGroup <= 0)
                return false;

            for (int fd = 0; fd <= 2; fd++)
            {
                if (isatty(fd) != 1)
                    continue;

                int foregroundGroup = tcgetpgrp(fd);
                if (foregroundGroup > 0 && foregroundGroup == processGroup)
                    return true;
            }

            return false;
        }
        catch
        {
            return !Console.IsInputRedirected || !Console.IsOutputRedirected || !Console.IsErrorRedirected;
        }
    }

    private static string ShellQuote(string value)
        => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    [DllImport("libc")]
    private static extern int isatty(int fd);

    [DllImport("libc")]
    private static extern int getpgrp();

    [DllImport("libc")]
    private static extern int tcgetpgrp(int fd);
}
