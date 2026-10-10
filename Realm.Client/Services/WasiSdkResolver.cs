using System;
using System.IO;

namespace Realm.Client.Services;

public static class WasiSdkResolver
{
	public static string ResolveWasiSdkPath()
	{
		if (TryGetEnvironmentWasiPath(out string environmentPath))
		{
			return environmentPath;
		}

		if (TryGetGodotUserDataWasiPath(out string godotPath))
		{
			return godotPath;
		}

		if (TryGetAppDataWasiPath(out string appDataPath))
		{
			return appDataPath;
		}

		if (TryGetEmbeddedWasiPath(out string embeddedPath))
		{
			return embeddedPath;
		}

		if (TryGetProjectRootWasiPath(out string projectRootPath))
		{
			return projectRootPath;
		}

		if (TryGetBaseDirectoryWasiPath(out string baseDirectoryPath))
		{
			return baseDirectoryPath;
		}

		string fallbackDirectoryPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "wasi_sdk_embedded"));
		return NormalizeDirectoryPath(fallbackDirectoryPath);
	}

	private static bool TryGetEnvironmentWasiPath(out string path)
	{
		path = string.Empty;
		string environmentPath = Environment.GetEnvironmentVariable("WASI_SDK_PATH");
		if (string.IsNullOrWhiteSpace(environmentPath) || !IsValidWasiSdkDirectory(environmentPath))
		{
			return false;
		}

		path = NormalizeDirectoryPath(environmentPath);
		return true;
	}

	private static bool TryGetGodotUserDataWasiPath(out string path)
	{
		path = string.Empty;
		try
		{
			string appDataDir = global::Godot.OS.GetUserDataDir();
			if (string.IsNullOrWhiteSpace(appDataDir))
			{
				return false;
			}

			string versionedPath = Path.Combine(appDataDir, "wasi_sdk", "wasi-sdk-34");
			if (IsValidWasiSdkDirectory(versionedPath))
			{
				path = NormalizeDirectoryPath(versionedPath);
				return true;
			}

			string wasiSdkParentDir = Path.Combine(appDataDir, "wasi_sdk");
			return TryFindCandidateWasiDirectory(wasiSdkParentDir, out path);
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetAppDataWasiPath(out string path)
	{
		path = string.Empty;
		try
		{
			string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			string appDataFallback = Path.Combine(appData, "Godot", "app_userdata", "Realm", "wasi_sdk", "wasi-sdk-34");
			if (IsValidWasiSdkDirectory(appDataFallback))
			{
				path = NormalizeDirectoryPath(appDataFallback);
				return true;
			}

			string fallbackSdkParent = Path.Combine(appData, "Godot", "app_userdata", "Realm", "wasi_sdk");
			return TryFindCandidateWasiDirectory(fallbackSdkParent, out path);
		}
		catch
		{
			return false;
		}
	}

	private static bool TryFindCandidateWasiDirectory(string parentDir, out string path)
	{
		path = string.Empty;
		if (!Directory.Exists(parentDir))
		{
			return false;
		}

		foreach (string candidate in System.Linq.Enumerable.OrderByDescending(Directory.GetDirectories(parentDir, "wasi-sdk-*"), d => d))
		{
			if (IsValidWasiSdkDirectory(candidate))
			{
				path = NormalizeDirectoryPath(candidate);
				return true;
			}
		}

		return false;
	}

	private static bool TryGetEmbeddedWasiPath(out string path)
	{
		path = string.Empty;
		try
		{
			string foundPath = PathUtils.FindPath("wasi_sdk_embedded");
			if (string.IsNullOrWhiteSpace(foundPath) || !IsValidWasiSdkDirectory(foundPath))
			{
				return false;
			}

			path = NormalizeDirectoryPath(foundPath);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetProjectRootWasiPath(out string path)
	{
		path = string.Empty;
		try
		{
			string projectRootPath = PathUtils.GetProjectRoot();
			if (string.IsNullOrWhiteSpace(projectRootPath))
			{
				return false;
			}

			string candidatePath = Path.Combine(projectRootPath, "wasi_sdk_embedded");
			if (IsValidWasiSdkDirectory(candidatePath))
			{
				path = NormalizeDirectoryPath(candidatePath);
				return true;
			}

			return false;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetBaseDirectoryWasiPath(out string path)
	{
		path = string.Empty;
		string baseDirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
		DirectoryInfo currentDirectoryInfo = new DirectoryInfo(baseDirectoryPath);

		while (currentDirectoryInfo != null)
		{
			string candidatePath = Path.Combine(currentDirectoryInfo.FullName, "wasi_sdk_embedded");
			if (IsValidWasiSdkDirectory(candidatePath))
			{
				path = NormalizeDirectoryPath(candidatePath);
				return true;
			}

			if (IsValidWasiSdkDirectory(currentDirectoryInfo.FullName))
			{
				path = NormalizeDirectoryPath(currentDirectoryInfo.FullName);
				return true;
			}

			currentDirectoryInfo = currentDirectoryInfo.Parent;
		}

		return false;
	}

	private static bool IsValidWasiSdkDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return false;
		}

		string clangExecutablePath = Path.Combine(directoryPath, "bin", "clang.exe");
		string clangUnixPath = Path.Combine(directoryPath, "bin", "clang");
		return (File.Exists(clangExecutablePath) && new FileInfo(clangExecutablePath).Length > 0)
		       || (File.Exists(clangUnixPath) && new FileInfo(clangUnixPath).Length > 0);
	}

	public static string GetDefaultIlcLlvmTarget(string? wasiSdkPath = null)
	{
		string path = !string.IsNullOrWhiteSpace(wasiSdkPath) ? wasiSdkPath : ResolveWasiSdkPath();
		if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
		{
			string libDir = Path.Combine(path, "share", "wasi-sysroot", "lib");
			if (Directory.Exists(libDir))
			{
				if (Directory.Exists(Path.Combine(libDir, "wasm32-wasip1")))
				{
					return "wasm32-unknown-wasip1";
				}
				if (Directory.Exists(Path.Combine(libDir, "wasm32-wasi")))
				{
					return "wasm32-unknown-wasi";
				}
			}
		}
		return "wasm32-unknown-wasip1";
	}

	private static string NormalizeDirectoryPath(string path)
	{
		return Path.GetFullPath(path);
	}
}