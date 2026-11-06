using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Godot;
using Environment = System.Environment;

namespace Realm.Client.Services;

public static class DotnetSdkResolver
{
	public const string SdkChannel = "10.0";

	private const int SdkProbeTimeoutMs = 20000;

	private static readonly string[] ExecutableFileNames = new[] { "dotnet.exe", "dotnet" };
	private static readonly object SdkProbeLock = new();

	private static string _probedExecutablePath = string.Empty;
	private static bool _probedExecutableHasSdk;

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

	public static string GetManagedSdkDirectory()
	{
		return Path.Combine(GetRealmUserDataDirectory(), "dotnet");
	}

	public static string GetManagedExecutablePath()
	{
		return Path.Combine(GetManagedSdkDirectory(), Environment.OSVersion.Platform == PlatformID.Win32NT ? "dotnet.exe" : "dotnet");
	}

	public static string ResolveExecutablePath()
	{
		if (TryGetManagedExecutable(out string managedExecutable)) return managedExecutable;
		if (TryGetEnvironmentRootExecutable(out string environmentExecutable)) return environmentExecutable;
		if (TryGetPathEnvironmentExecutable(out string pathExecutable)) return pathExecutable;
		if (TryGetWellKnownInstallExecutable(out string wellKnownExecutable)) return wellKnownExecutable;
		return string.Empty;
	}

	public static string ResolveSdkExecutablePath()
	{
		string executablePath = ResolveExecutablePath();
		if (string.IsNullOrEmpty(executablePath)) return string.Empty;

		return ReportsSdkInstalled(executablePath) ? executablePath : string.Empty;
	}

	public static bool IsSdkAvailable()
	{
		return !string.IsNullOrEmpty(ResolveSdkExecutablePath());
	}

	private static bool ReportsSdkInstalled(string executablePath)
	{
		lock (SdkProbeLock)
		{
			if (string.Equals(_probedExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)) return _probedExecutableHasSdk;
		}

		bool foundSdk = ProbeSdkVersions(executablePath);

		lock (SdkProbeLock)
		{
			_probedExecutablePath = executablePath;
			_probedExecutableHasSdk = foundSdk;
		}

		return foundSdk;
	}

	private static bool ProbeSdkVersions(string executablePath)
	{
		try
		{
			using var probeProcess = new Process();
			probeProcess.StartInfo.FileName = executablePath;
			probeProcess.StartInfo.Arguments = "--list-sdks";
			probeProcess.StartInfo.CreateNoWindow = true;
			probeProcess.StartInfo.UseShellExecute = false;
			probeProcess.StartInfo.RedirectStandardOutput = true;
			probeProcess.StartInfo.RedirectStandardError = true;
			probeProcess.Start();

			string installedSdkVersions = probeProcess.StandardOutput.ReadToEnd();
			if (!probeProcess.WaitForExit(SdkProbeTimeoutMs)) return false;

			return installedSdkVersions.Contains(SdkChannel + ".", StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	public static void PrependManagedDirectoryToProcessPath()
	{
		string managedDirectory = GetManagedSdkDirectory();
		if (!Directory.Exists(managedDirectory)) return;

		string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		if (pathVariable.IndexOf(managedDirectory, StringComparison.OrdinalIgnoreCase) >= 0) return;

		Environment.SetEnvironmentVariable("PATH", managedDirectory + Path.PathSeparator + pathVariable, EnvironmentVariableTarget.Process);
	}

	private static bool TryGetManagedExecutable(out string path)
	{
		return TryGetExecutableFromCandidates(new[] { GetManagedExecutablePath() }, out path);
	}

	private static bool TryGetEnvironmentRootExecutable(out string path)
	{
		path = string.Empty;
		string rootDirectory = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? string.Empty;
		if (string.IsNullOrWhiteSpace(rootDirectory)) return false;

		var candidates = new string[ExecutableFileNames.Length];
		for (int index = 0; index < ExecutableFileNames.Length; index++)
		{
			candidates[index] = Path.Combine(rootDirectory.Trim('"', ' '), ExecutableFileNames[index]);
		}

		return TryGetExecutableFromCandidates(candidates, out path);
	}

	private static bool TryGetPathEnvironmentExecutable(out string path)
	{
		path = string.Empty;
		string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		foreach (string directory in pathVariable.Split(Path.PathSeparator))
		{
			if (string.IsNullOrWhiteSpace(directory)) continue;

			var candidates = new string[ExecutableFileNames.Length];
			for (int index = 0; index < ExecutableFileNames.Length; index++)
			{
				candidates[index] = Path.Combine(directory.Trim('"', ' '), ExecutableFileNames[index]);
			}

			if (TryGetExecutableFromCandidates(candidates, out path)) return true;
		}

		return false;
	}

	private static bool TryGetWellKnownInstallExecutable(out string path)
	{
		path = string.Empty;
		var candidates = new List<string>();
		string programFilesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		string localAppDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

		if (!string.IsNullOrWhiteSpace(programFilesDirectory))
		{
			candidates.Add(Path.Combine(programFilesDirectory, "dotnet", "dotnet.exe"));
		}

		if (!string.IsNullOrWhiteSpace(localAppDataDirectory))
		{
			candidates.Add(Path.Combine(localAppDataDirectory, "Microsoft", "dotnet", "dotnet.exe"));
		}

		return TryGetExecutableFromCandidates(candidates.ToArray(), out path);
	}

	private static bool TryGetExecutableFromCandidates(string[] candidatePaths, out string path)
	{
		path = string.Empty;
		foreach (string candidatePath in candidatePaths)
		{
			if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath)) continue;
			if (new FileInfo(candidatePath).Length == 0) continue;

			path = candidatePath;
			return true;
		}

		return false;
	}
}
