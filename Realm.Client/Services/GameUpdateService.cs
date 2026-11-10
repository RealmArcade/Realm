using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Realm.Shared;

namespace Realm.Client.Services;

/// <summary>
///     Answers the two version questions the main menu shows: whether a newer release has been published than the
///     binary running, and which other build versions are installed on this machine to relaunch into.
/// </summary>
public static class GameUpdateService
{
	private const string LatestReleaseEndpoint = "https://api.github.com/repos/speige/Realm/releases/latest";

	/// <summary>
	///     True when the latest published tag differs from the running binary. A missing tag, an unreachable
	///     service or a malformed answer is "no newer release", because the only thing the caller does with this is
	///     show an "Outdated" hint, and a failed check must not produce one.
	/// </summary>
	public static async Task<bool> IsNewerReleasePublishedAsync()
	{
		try
		{
			using var client = new HttpClient();
			client.DefaultRequestHeaders.UserAgent.ParseAdd("Realm-Godot-Client");
			var response = await client.GetAsync(LatestReleaseEndpoint);
			if (!response.IsSuccessStatusCode) return false;

			string json = await response.Content.ReadAsStringAsync();
			using var doc = JsonDocument.Parse(json);
			if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp)) return false;

			string latestTag = tagProp.GetString() ?? "";
			if (latestTag.StartsWith("v")) latestTag = latestTag.Substring(1);

			return latestTag != RealmVersion.GameBinaryVersion;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MainMenu] Failed to check for updates: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	///     The build versions installed beside this one, newest directory name order as the filesystem lists them,
	///     excluding the running version because the caller offers the current one under its own entry.
	/// </summary>
	public static List<string> GetOtherInstalledVersions(string baseDirectory)
	{
		var versions = new List<string>();
		string versionsDirectory = Path.Combine(baseDirectory, "versions");
		if (!Directory.Exists(versionsDirectory)) return versions;

		foreach (string directory in Directory.GetDirectories(versionsDirectory))
		{
			string version = Path.GetFileName(directory);
			if (version != RealmVersion.GameBinaryVersion) versions.Add(version);
		}

		return versions;
	}
}
