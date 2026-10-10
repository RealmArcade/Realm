using Godot;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Realm.Client.Utils;

public static class PathUtils
{
	private static string _cachedProjectRoot;
	private static string[] _cachedDataDirs;
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cachedPaths = new(StringComparer.OrdinalIgnoreCase);

	public static bool IsDevelopmentBuild
	{
		get
		{
#if DEBUG
			return true;
#else
			string baseDir = AppDomain.CurrentDomain.BaseDirectory.Replace("\\", "/").TrimEnd('/');
			if (baseDir.Contains("data_Realm_windows_x86_64", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			string exeDir = OS.GetExecutablePath().GetBaseDir().Replace("\\", "/").TrimEnd('/');
			if (Directory.Exists(exeDir))
			{
				string[] dataDirs = GetDataDirs(exeDir);
				if (dataDirs.Length > 0 && baseDir.Equals(dataDirs[0].Replace("\\", "/").TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}

			return true;
#endif
		}
	}

	private static string[] GetDataDirs(string exeDir)
	{
		if (_cachedDataDirs != null)
		{
			return _cachedDataDirs;
		}

		if (string.IsNullOrEmpty(exeDir) || !Directory.Exists(exeDir))
		{
			_cachedDataDirs = Array.Empty<string>();
			return _cachedDataDirs;
		}

		_cachedDataDirs = Directory.GetDirectories(exeDir, "data_*");
		return _cachedDataDirs;
	}

	public static string GetProjectRoot()
	{
		if (!string.IsNullOrEmpty(_cachedProjectRoot))
		{
			return _cachedProjectRoot;
		}

		_cachedProjectRoot = ResolveProjectRoot();
		return _cachedProjectRoot;
	}

	private static string ResolveProjectRoot()
	{
		string resPath = ProjectSettings.GlobalizePath("res://");
		if (!string.IsNullOrWhiteSpace(resPath) && resPath != "." && resPath != "./" && Directory.Exists(resPath))
		{
			return resPath.Replace("\\", "/").TrimEnd('/');
		}

		string baseDir = AppDomain.CurrentDomain.BaseDirectory.Replace("\\", "/").TrimEnd('/');
		if (!string.IsNullOrWhiteSpace(baseDir) && Directory.Exists(baseDir))
		{
			return baseDir;
		}

		string exeDir = OS.GetExecutablePath().GetBaseDir().Replace("\\", "/").TrimEnd('/');
		if (!string.IsNullOrWhiteSpace(exeDir) && Directory.Exists(exeDir))
		{
			string[] dataDirs = GetDataDirs(exeDir);
			if (dataDirs.Length > 0)
			{
				return dataDirs[0].Replace("\\", "/").TrimEnd('/');
			}

			return exeDir;
		}

		return ".";
	}

	public static string FindPath(string relativePath)
	{
		if (string.IsNullOrEmpty(relativePath))
		{
			return GetProjectRoot();
		}

		string normalizedRelative = relativePath.Replace("\\", "/").TrimStart('/');
		if (_cachedPaths.TryGetValue(normalizedRelative, out string cached))
		{
			return cached;
		}

		string resolvedPath = ResolvePath(normalizedRelative);
		_cachedPaths[normalizedRelative] = resolvedPath;
		return resolvedPath;
	}

	private static string ResolvePath(string normalizedRelative)
	{
		string primaryPath = Path.Combine(GetProjectRoot(), normalizedRelative).Replace("\\", "/");
		if (File.Exists(primaryPath) || Directory.Exists(primaryPath))
		{
			return primaryPath;
		}

		string baseDirPath = ResolveFromBaseDir(normalizedRelative);
		if (baseDirPath != null)
		{
			return baseDirPath;
		}

		string exeDirPath = ResolveFromExeDir(normalizedRelative);
		if (exeDirPath != null)
		{
			return exeDirPath;
		}

		string globalizedResPath = ResolveFromGlobalizedRes(normalizedRelative);
		if (globalizedResPath != null)
		{
			return globalizedResPath;
		}

		string parentRootPath = ResolveFromParentRoot(normalizedRelative);
		if (parentRootPath != null)
		{
			return parentRootPath;
		}

		return primaryPath;
	}

	private static string ResolveFromBaseDir(string normalizedRelative)
	{
		string baseDir = AppDomain.CurrentDomain.BaseDirectory.Replace("\\", "/").TrimEnd('/');
		if (string.IsNullOrWhiteSpace(baseDir))
		{
			return null;
		}

		string directPath = Path.Combine(baseDir, normalizedRelative).Replace("\\", "/");
		if (File.Exists(directPath) || Directory.Exists(directPath))
		{
			return directPath;
		}

		return null;
	}

	private static string ResolveFromExeDir(string normalizedRelative)
	{
		string exeDir = OS.GetExecutablePath().GetBaseDir().Replace("\\", "/").TrimEnd('/');
		if (string.IsNullOrWhiteSpace(exeDir))
		{
			return null;
		}

		string exeDirectPath = Path.Combine(exeDir, normalizedRelative).Replace("\\", "/");
		if (File.Exists(exeDirectPath) || Directory.Exists(exeDirectPath))
		{
			return exeDirectPath;
		}

		string dataDirPath = FindInDataDirs(exeDir, normalizedRelative);
		if (dataDirPath != null)
		{
			return dataDirPath;
		}

		return null;
	}

	private static string ResolveFromGlobalizedRes(string normalizedRelative)
	{
		string globalizedRes = ProjectSettings.GlobalizePath("res://" + normalizedRelative).Replace("\\", "/");
		if (string.IsNullOrWhiteSpace(globalizedRes))
		{
			return null;
		}

		if (File.Exists(globalizedRes) || Directory.Exists(globalizedRes))
		{
			return globalizedRes;
		}

		return null;
	}

	private static string ResolveFromParentRoot(string normalizedRelative)
	{
		string parentRootPath = Path.GetFullPath(Path.Combine(GetProjectRoot(), "..", normalizedRelative)).Replace("\\", "/");
		if (File.Exists(parentRootPath) || Directory.Exists(parentRootPath))
		{
			return parentRootPath;
		}

		return null;
	}

	private static string FindInDataDirs(string exeDir, string normalizedRelative)
	{
		string[] dataDirs = GetDataDirs(exeDir);
		foreach (var dataDir in dataDirs)
		{
			string normalizedDataDir = dataDir.Replace("\\", "/").TrimEnd('/');
			string dataDirPath = Path.Combine(normalizedDataDir, normalizedRelative).Replace("\\", "/");
			if (File.Exists(dataDirPath) || Directory.Exists(dataDirPath))
			{
				return dataDirPath;
			}
		}
		return null;
	}

	public static string GlobalizePath(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return GetProjectRoot();
		}

		if (path.StartsWith("res://"))
		{
			string subPath = path.Substring(6);
			return FindPath(subPath);
		}

		return ProjectSettings.GlobalizePath(path);
	}

	public static void ClearCache()
	{
		_cachedProjectRoot = null;
		_cachedDataDirs = null;
		_cachedPaths.Clear();
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ByHandleFileInformation
	{
		public uint FileAttributes;
		public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
		public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
		public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
		public uint VolumeSerialNumber;
		public uint FileSizeHigh;
		public uint FileSizeLow;
		public uint NumberOfLinks;
		public uint FileIndexHigh;
		public uint FileIndexLow;
	}

	[DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetFileInformationByHandle(IntPtr hFile, out ByHandleFileInformation lpFileInformation);

	[DllImport("libc", EntryPoint = "link", SetLastError = true)]
	private static extern int PosixLink(string oldpath, string newpath);

	private static readonly System.Collections.Generic.HashSet<string> _mutableMapFiles = new(StringComparer.OrdinalIgnoreCase)
	{
		"terrain.json",
		"metadata.json",
		"manifest.json",
		"license.json"
	};

	private static readonly System.Collections.Generic.HashSet<string> _mutableMapExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".json",
		".cs",
		".csproj",
		".sln",
		".slnx",
		".wit",
		".gdshader",
		".shader",
		".txt",
		".md"
	};

	public static bool IsMutableMapFileType(string path)
	{
		if (string.IsNullOrEmpty(path)) return false;
		
		string fileName = Path.GetFileName(path);
		if (_mutableMapFiles.Contains(fileName))
		{
			return true;
		}

		string ext = Path.GetExtension(path);
		return _mutableMapExtensions.Contains(ext);
	}

	public static bool TryCreateHardLink(string sourceFile, string targetFile)
	{
		if (string.IsNullOrEmpty(sourceFile) || string.IsNullOrEmpty(targetFile)) return false;
		if (!File.Exists(sourceFile)) return false;

		try
		{
			string fullSource = Path.GetFullPath(sourceFile);
			string fullTarget = Path.GetFullPath(targetFile);

			if (AreSameFileOrHardLink(fullSource, fullTarget))
			{
				return true;
			}

			PrepareTargetDirectory(fullTarget);
			ClearTargetFileIfExists(fullTarget);

			if (OperatingSystem.IsWindows())
			{
				return CreateHardLinkW(fullTarget, fullSource, IntPtr.Zero);
			}

			if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
			{
				return PosixLink(fullSource, fullTarget) == 0;
			}
		}
		catch
		{
			return false;
		}

		return false;
	}

	public static void LinkOrCopyFile(string sourceFile, string targetFile, bool preferHardLink = true)
	{
		if (string.IsNullOrEmpty(sourceFile) || string.IsNullOrEmpty(targetFile)) return;
		if (!File.Exists(sourceFile)) return;

		if (preferHardLink && TryCreateHardLink(sourceFile, targetFile))
		{
			return;
		}

		CopyFileClearingReadOnly(sourceFile, targetFile);
	}

	public static void CopyFileClearingReadOnly(string sourceFile, string targetFile)
	{
		if (string.IsNullOrEmpty(sourceFile) || string.IsNullOrEmpty(targetFile)) return;
		if (!File.Exists(sourceFile)) return;

		string fullSource = Path.GetFullPath(sourceFile);
		string fullTarget = Path.GetFullPath(targetFile);

		if (AreSameFileOrHardLink(fullSource, fullTarget))
		{
			return;
		}

		PrepareTargetDirectory(fullTarget);
		RemoveReadOnlyAttributeIfExists(fullTarget);

		const int maxAttempts = 10;
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				File.Copy(fullSource, fullTarget, true);
				return;
			}
			catch (IOException) when (attempt < maxAttempts - 1)
			{
				System.Threading.Thread.Sleep(250);
			}
		}
	}

	private static void PrepareTargetDirectory(string fullTarget)
	{
		string targetDir = Path.GetDirectoryName(fullTarget);
		if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
		{
			Directory.CreateDirectory(targetDir);
		}
	}

	private static void ClearTargetFileIfExists(string fullTarget)
	{
		if (File.Exists(fullTarget))
		{
			RemoveReadOnlyAttributeIfExists(fullTarget);
			File.Delete(fullTarget);
		}
	}

	private static void RemoveReadOnlyAttributeIfExists(string fullTarget)
	{
		if (File.Exists(fullTarget))
		{
			var attrs = File.GetAttributes(fullTarget);
			if ((attrs & FileAttributes.ReadOnly) != 0)
			{
				File.SetAttributes(fullTarget, attrs & ~FileAttributes.ReadOnly);
			}
		}
	}

	public static bool AreSameFileOrHardLink(string path1, string path2)
	{
		if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2)) return false;

		string full1 = Path.GetFullPath(path1);
		string full2 = Path.GetFullPath(path2);

		if (string.Equals(full1, full2, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
		{
			return true;
		}

		if (!File.Exists(full1) || !File.Exists(full2))
		{
			return false;
		}

		if (OperatingSystem.IsWindows())
		{
			bool? winResult = CheckSameFileWindows(full1, full2);
			if (winResult.HasValue)
			{
				return winResult.Value;
			}
		}

		return CheckSameFileFallback(full1, full2);
	}

	private static bool? CheckSameFileWindows(string full1, string full2)
	{
		try
		{
			using var fs1 = new FileStream(full1, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
			using var fs2 = new FileStream(full2, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
			if (GetFileInformationByHandle(fs1.SafeFileHandle.DangerousGetHandle(), out var info1) &&
			    GetFileInformationByHandle(fs2.SafeFileHandle.DangerousGetHandle(), out var info2))
			{
				return info1.VolumeSerialNumber == info2.VolumeSerialNumber &&
				       info1.FileIndexHigh == info2.FileIndexHigh &&
				       info1.FileIndexLow == info2.FileIndexLow;
			}
		}
		catch
		{
		}
		return null;
	}

	private static bool CheckSameFileFallback(string full1, string full2)
	{
		try
		{
			var fi1 = new FileInfo(full1);
			var fi2 = new FileInfo(full2);
			if (fi1.Length != fi2.Length) return false;
			if (fi1.LastWriteTimeUtc == fi2.LastWriteTimeUtc)
			{
				return true;
			}
		}
		catch
		{
		}

		return false;
	}
}