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

        string? destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (CreateHardLinkW(destinationPath, sourcePath, IntPtr.Zero))
                {
                    return true;
                }

                if (!overwrite && Marshal.GetLastWin32Error() == 183) // ERROR_ALREADY_EXISTS
                {
                    return true;
                }

                if (overwrite)
                {
                    try { File.Delete(destinationPath); } catch { }
                    if (CreateHardLinkW(destinationPath, sourcePath, IntPtr.Zero))
                    {
                        return true;
                    }
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (link(sourcePath, destinationPath) == 0)
                {
                    return true;
                }

                if (overwrite)
                {
                    try { File.Delete(destinationPath); } catch { }
                    if (link(sourcePath, destinationPath) == 0)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
        }

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
