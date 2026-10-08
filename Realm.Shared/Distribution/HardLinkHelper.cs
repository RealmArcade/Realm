using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Realm.Shared.Distribution;

public static class HardLinkHelper
{
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);

    public static bool CreateHardLinkOrCopy(string destinationPath, string sourcePath, bool overwrite = true)
    {
        if (string.IsNullOrWhiteSpace(destinationPath) || string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        if (!File.Exists(sourcePath))
        {
            return false;
        }

        EnsureDirectoryExists(destinationPath);

        if (TryCreateHardLink(destinationPath, sourcePath, overwrite))
        {
            return true;
        }

        return TryCopyFile(destinationPath, sourcePath, overwrite);
    }

    private static void EnsureDirectoryExists(string destinationPath)
    {
        string? destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }
    }

    private static bool TryCreateHardLink(string destinationPath, string sourcePath, bool overwrite)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return TryCreateHardLinkWindows(destinationPath, sourcePath, overwrite);
            }
            
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return TryCreateHardLinkUnix(destinationPath, sourcePath, overwrite);
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryCreateHardLinkWindows(string destinationPath, string sourcePath, bool overwrite)
    {
        if (CreateHardLinkW(destinationPath, sourcePath, IntPtr.Zero))
        {
            return true;
        }

        if (!overwrite && Marshal.GetLastWin32Error() == 183) // ERROR_ALREADY_EXISTS
        {
            return true;
        }

        if (!overwrite)
        {
            return false;
        }

        TryDeleteFile(destinationPath);
        return CreateHardLinkW(destinationPath, sourcePath, IntPtr.Zero);
    }

    private static bool TryCreateHardLinkUnix(string destinationPath, string sourcePath, bool overwrite)
    {
        if (link(sourcePath, destinationPath) == 0)
        {
            return true;
        }

        if (!overwrite)
        {
            return false;
        }

        TryDeleteFile(destinationPath);
        return link(sourcePath, destinationPath) == 0;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static bool TryCopyFile(string destinationPath, string sourcePath, bool overwrite)
    {
        try
        {
            File.Copy(sourcePath, destinationPath, overwrite);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
