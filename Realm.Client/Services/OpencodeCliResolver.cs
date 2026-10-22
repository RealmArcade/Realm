using System;
using System.IO;
using Godot;
using Environment = System.Environment;

namespace Realm.Client.Services;

public static class OpencodeCliResolver
{
	private static readonly string[] ExecutableFileNames = new[] { "opencode.exe", "opencode.cmd", "opencode.bat" };

	public static string GetRealmUserDataDirectory()
	{
		try
		{
			string appDataDir = OS.GetUserDataDir();
			if (!string.IsNullOrWhiteSpace(appDataDir))
			{
				return appDataDir;
			}
		}
		catch
		{
		}

		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "Realm");
	}

	public static string GetManagedCliDirectory()
	{
		return Path.Combine(GetRealmUserDataDirectory(), "opencode_cli");
	}

	public static string GetDataDirectory()
	{
		return Path.Combine(GetRealmUserDataDirectory(), "opencode_data");
	}

	public static string GetManagedCliExecutablePath()
	{
		return Path.Combine(GetManagedCliDirectory(), "bin", "opencode.exe");
	}

	public static string ResolveExecutablePath()
	{
		if (TryGetManagedExecutable(out string managedExecutable))
		{
			return managedExecutable;
		}

		if (TryGetPathEnvironmentExecutable(out string pathExecutable))
		{
			return pathExecutable;
		}

		if (TryGetUserProfileExecutable(out string userProfileExecutable))
		{
			return userProfileExecutable;
		}

		if (TryGetNpmGlobalExecutable(out string npmExecutable))
		{
			return npmExecutable;
		}

		if (TryGetDesktopAppExecutable(out string desktopAppExecutable))
		{
			return desktopAppExecutable;
		}

		return string.Empty;
	}

	public static bool IsAvailable()
	{
		return !string.IsNullOrEmpty(ResolveExecutablePath());
	}

	private static bool TryGetManagedExecutable(out string path)
	{
		path = string.Empty;
		string managedExecutable = GetManagedCliExecutablePath();
		if (!IsExecutableFile(managedExecutable))
		{
			return false;
		}

		path = managedExecutable;
		return true;
	}

	private static bool TryGetPathEnvironmentExecutable(out string path)
	{
		path = string.Empty;
		string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		foreach (string directory in pathVariable.Split(Path.PathSeparator))
		{
			if (string.IsNullOrWhiteSpace(directory))
			{
				continue;
			}

			foreach (string executableFileName in ExecutableFileNames)
			{
				string candidatePath = Path.Combine(directory.Trim('"', ' '), executableFileName);
				if (IsExecutableFile(candidatePath))
				{
					path = candidatePath;
					return true;
				}
			}
		}

		return false;
	}

	private static bool TryGetUserProfileExecutable(out string path)
	{
		path = string.Empty;
		string userProfileDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfileDir))
		{
			return false;
		}

		return TryGetExecutableFromCandidates(new[]
		{
			Path.Combine(userProfileDir, ".opencode", "bin", "opencode.exe"),
			Path.Combine(userProfileDir, ".opencode", "bin", "opencode")
		}, out path);
	}

	private static bool TryGetNpmGlobalExecutable(out string path)
	{
		path = string.Empty;
		string appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		if (string.IsNullOrWhiteSpace(appDataDir))
		{
			return false;
		}

		return TryGetExecutableFromCandidates(new[]
		{
			Path.Combine(appDataDir, "npm", "opencode.cmd"),
			Path.Combine(appDataDir, "npm", "opencode")
		}, out path);
	}

	private static bool TryGetDesktopAppExecutable(out string path)
	{
		path = string.Empty;
		string localAppDataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(localAppDataDir))
		{
			return false;
		}

		return TryGetExecutableFromCandidates(new[]
		{
			Path.Combine(localAppDataDir, "Programs", "@opencodedesktop", "resources", "opencode-cli.exe")
		}, out path);
	}

	private static bool TryGetExecutableFromCandidates(string[] candidatePaths, out string path)
	{
		path = string.Empty;
		foreach (string candidatePath in candidatePaths)
		{
			if (!IsExecutableFile(candidatePath))
			{
				continue;
			}

			path = candidatePath;
			return true;
		}

		return false;
	}

	private static bool IsExecutableFile(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
		{
			return false;
		}

		return new FileInfo(filePath).Length > 0;
	}
}
