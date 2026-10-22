using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HttpClient = System.Net.Http.HttpClient;
using FileAccess = System.IO.FileAccess;

namespace Realm.Client.Services;

public static class OpencodeCliInstaller
{
	private const string LatestReleaseUrl = "https://api.github.com/repos/sst/opencode/releases/latest";
	private const string WindowsReleaseAssetName = "opencode-windows-x64.zip";
	private const string ExecutableFileName = "opencode.exe";

	private static readonly HttpClient ReleaseHttpClient = CreateReleaseHttpClient();
	private static readonly SemaphoreSlim InstallSemaphore = new(1, 1);

	private static bool _installAttempted;

	public static async Task<bool> EnsureInstalledAsync()
	{
		if (OpencodeCliResolver.IsAvailable())
		{
			return true;
		}

		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		await InstallSemaphore.WaitAsync();
		try
		{
			if (OpencodeCliResolver.IsAvailable())
			{
				return true;
			}

			if (_installAttempted)
			{
				return false;
			}

			_installAttempted = true;
			return await DownloadManagedCliAsync();
		}
		finally
		{
			InstallSemaphore.Release();
		}
	}

	private static async Task<bool> DownloadManagedCliAsync()
	{
		string managedExecutablePath = OpencodeCliResolver.GetManagedCliExecutablePath();
		string managedExecutableDirectory = Path.GetDirectoryName(managedExecutablePath) ?? OpencodeCliResolver.GetManagedCliDirectory();
		string downloadDirectory = Path.Combine(OpencodeCliResolver.GetManagedCliDirectory(), "download");
		string archivePath = Path.Combine(downloadDirectory, WindowsReleaseAssetName);

		try
		{
			GD.Print("[OpencodeCliInstaller] opencode CLI not found. Downloading the managed CLI...");
			Directory.CreateDirectory(managedExecutableDirectory);
			Directory.CreateDirectory(downloadDirectory);

			string releaseJson = await ReleaseHttpClient.GetStringAsync(LatestReleaseUrl);
			string downloadUrl = ResolveReleaseAssetUrl(releaseJson);
			if (string.IsNullOrEmpty(downloadUrl))
			{
				GD.PrintErr($"[OpencodeCliInstaller] Release asset '{WindowsReleaseAssetName}' was not found in the latest opencode release.");
				return false;
			}

			await DownloadArchiveAsync(downloadUrl, archivePath);

			string extractedExecutablePath = ExtractExecutable(archivePath, downloadDirectory);
			if (string.IsNullOrEmpty(extractedExecutablePath))
			{
				GD.PrintErr($"[OpencodeCliInstaller] '{ExecutableFileName}' was not found inside the downloaded opencode archive.");
				return false;
			}

			File.Move(extractedExecutablePath, managedExecutablePath, overwrite: true);
			GD.Print($"[OpencodeCliInstaller] opencode CLI installed at {managedExecutablePath}");
			return OpencodeCliResolver.IsAvailable();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[OpencodeCliInstaller] Failed to install opencode CLI: {ex.Message}");
			return false;
		}
		finally
		{
			DeleteDirectoryIfExists(downloadDirectory);
		}
	}

	private static string ResolveReleaseAssetUrl(string releaseJson)
	{
		using JsonDocument releaseDocument = JsonDocument.Parse(releaseJson);
		if (!releaseDocument.RootElement.TryGetProperty("assets", out JsonElement assetsElement))
		{
			return string.Empty;
		}

		foreach (JsonElement assetElement in assetsElement.EnumerateArray())
		{
			string assetName = assetElement.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
			if (!string.Equals(assetName, WindowsReleaseAssetName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			return assetElement.TryGetProperty("browser_download_url", out JsonElement downloadUrlElement)
				? downloadUrlElement.GetString() ?? string.Empty
				: string.Empty;
		}

		return string.Empty;
	}

	private static async Task DownloadArchiveAsync(string downloadUrl, string archivePath)
	{
		using HttpResponseMessage response = await ReleaseHttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();

		await using FileStream archiveStream = new(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
		await response.Content.CopyToAsync(archiveStream);
	}

	private static string ExtractExecutable(string archivePath, string workingDirectory)
	{
		string extractDirectory = Path.Combine(workingDirectory, "extract");
		DeleteDirectoryIfExists(extractDirectory);
		ZipFile.ExtractToDirectory(archivePath, extractDirectory);

		foreach (string candidatePath in Directory.GetFiles(extractDirectory, ExecutableFileName, SearchOption.AllDirectories))
		{
			if (new FileInfo(candidatePath).Length > 0)
			{
				return candidatePath;
			}
		}

		return string.Empty;
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
			GD.PrintErr($"[OpencodeCliInstaller] Failed to clean up '{directoryPath}': {ex.Message}");
		}
	}

	private static HttpClient CreateReleaseHttpClient()
	{
		var httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(5)
		};
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RealmGameClient");
		httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		return httpClient;
	}
}
