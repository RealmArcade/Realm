using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Services;

public static class DotnetSdkInstaller
{
	private const string InstallScriptUrl = "https://dot.net/v1/dotnet-install.ps1";
	private const string InstallScriptFileName = "dotnet-install.ps1";
	private const int InstallTimeoutMs = 15 * 60 * 1000;

	private static readonly HttpClient InstallHttpClient = CreateInstallHttpClient();
	private static readonly SemaphoreSlim InstallSemaphore = new(1, 1);

	private static int _isInstalling;

	public static bool IsInstalling => Volatile.Read(ref _isInstalling) == 1;

	public static void EnsureInstalledInBackground()
	{
		if (!OperatingSystem.IsWindows()) return;

		if (DotnetSdkResolver.IsSdkAvailable())
		{
			DotnetSdkResolver.PrependManagedDirectoryToProcessPath();
			return;
		}

		if (IsInstalling) return;

		_ = Task.Run(EnsureInstalledAsync);
	}

	public static async Task<bool> EnsureInstalledAsync()
	{
		if (!OperatingSystem.IsWindows()) return false;
		if (DotnetSdkResolver.IsSdkAvailable()) return true;

		await InstallSemaphore.WaitAsync();
		try
		{
			if (DotnetSdkResolver.IsSdkAvailable()) return true;

			Volatile.Write(ref _isInstalling, 1);
			return await InstallSdkAsync();
		}
		finally
		{
			Volatile.Write(ref _isInstalling, 0);
			InstallSemaphore.Release();
		}
	}

	private static async Task<bool> InstallSdkAsync()
	{
		string managedDirectory = DotnetSdkResolver.GetManagedSdkDirectory();
		string downloadDirectory = Path.Combine(managedDirectory, "download");
		string installScriptPath = Path.Combine(downloadDirectory, InstallScriptFileName);

		try
		{
			GameLog.LogToConsole($"[DotnetSdkInstaller] .NET SDK not found. Downloading the map compiler into {managedDirectory}.");
			Directory.CreateDirectory(downloadDirectory);
			await DownloadInstallScriptAsync(installScriptPath);

			if (!RunInstallScript(installScriptPath, managedDirectory))
			{
				GameLog.LogError("[DotnetSdkInstaller] The .NET SDK installer did not finish successfully. Map scripts stay uncompilable until it is installed.");
				return false;
			}

			if (!DotnetSdkResolver.IsSdkAvailable())
			{
				GameLog.LogError($"[DotnetSdkInstaller] The .NET SDK install completed but {DotnetSdkResolver.GetManagedExecutablePath()} is missing.");
				return false;
			}

			DotnetSdkResolver.PrependManagedDirectoryToProcessPath();
			GameLog.LogToConsole($"[DotnetSdkInstaller] .NET SDK installed at {DotnetSdkResolver.GetManagedExecutablePath()}");
			return true;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[DotnetSdkInstaller] Failed to install the .NET SDK: {ex.Message}");
			return false;
		}
		finally
		{
			DeleteDirectoryIfExists(downloadDirectory);
		}
	}

	private static async Task DownloadInstallScriptAsync(string installScriptPath)
	{
		using HttpResponseMessage response = await InstallHttpClient.GetAsync(InstallScriptUrl, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();

		await using FileStream scriptStream = new(installScriptPath, FileMode.Create, FileAccess.Write, FileShare.None);
		await response.Content.CopyToAsync(scriptStream);
	}

	private static bool RunInstallScript(string installScriptPath, string managedDirectory)
	{
		string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{installScriptPath}\" -Channel {DotnetSdkResolver.SdkChannel} -InstallDir \"{managedDirectory}\" -NoPath";

		using var installProcess = new Process();
		installProcess.StartInfo.FileName = "powershell.exe";
		installProcess.StartInfo.Arguments = arguments;
		installProcess.StartInfo.CreateNoWindow = true;
		installProcess.StartInfo.UseShellExecute = false;
		installProcess.StartInfo.RedirectStandardOutput = true;
		installProcess.StartInfo.RedirectStandardError = true;

		installProcess.OutputDataReceived += (sender, eventArguments) =>
		{
			if (!string.IsNullOrEmpty(eventArguments.Data)) GameLog.LogToConsole("[dotnet-install] " + eventArguments.Data);
		};
		installProcess.ErrorDataReceived += (sender, eventArguments) =>
		{
			if (!string.IsNullOrEmpty(eventArguments.Data)) GameLog.LogError("[dotnet-install error] " + eventArguments.Data);
		};

		installProcess.Start();
		installProcess.BeginOutputReadLine();
		installProcess.BeginErrorReadLine();

		if (!installProcess.WaitForExit(InstallTimeoutMs))
		{
			GameLog.LogError($"[DotnetSdkInstaller] The .NET SDK installer did not finish within {InstallTimeoutMs / 60000} minutes and was stopped.");
			try
			{
				installProcess.Kill(entireProcessTree: true);
			}
			catch
			{
			}

			return false;
		}

		return installProcess.ExitCode == 0;
	}

	private static void DeleteDirectoryIfExists(string directoryPath)
	{
		try
		{
			if (Directory.Exists(directoryPath))
			{
				Directory.Delete(directoryPath, recursive: true);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[DotnetSdkInstaller] Failed to clean up '{directoryPath}': {ex.Message}");
		}
	}

	private static HttpClient CreateInstallHttpClient()
	{
		var httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(5)
		};
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RealmGameClient");
		return httpClient;
	}
}
