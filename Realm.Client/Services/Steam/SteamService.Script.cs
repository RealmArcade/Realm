using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Realm.Shared.Steam;
using Steamworks;

namespace Realm.Client.Services;

/// <summary>
///     The half of Steam a map script may use: achievements, stats and the cloud files a map saves into. Every call is a
///     no-op while Steamworks is not running, so a map that uses these members on a screen without Steam behaves as if it
///     had asked for nothing.
/// </summary>
public partial class SteamService
{
	private bool _achievementStoreRequested;

	/// <summary>
	///     Whether Steam's cloud storage is usable on this screen, which is what decides if a map's saves can be read from
	///     and written to it at all.
	/// </summary>
	public bool IsCloudAvailable
	{
		get
		{
			if (!_initialized) return false;

			try
			{
				return SteamRemoteStorage.IsCloudEnabledForAccount() && SteamRemoteStorage.IsCloudEnabledForApp();
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[Steam] whether cloud storage is available could not be read: {exception.Message}");
				return false;
			}
		}
	}

	/// <summary>
	///     Unlocks Steam's achievement of that name for this player.
	/// </summary>
	/// <param name="achievementName">The name Steam knows the achievement by, already namespaced for the map it came from.</param>
	/// <returns>True when Steam was asked to unlock it.</returns>
	public bool UnlockAchievement(string achievementName)
	{
		if (!_initialized || string.IsNullOrEmpty(achievementName)) return false;

		try
		{
			if (!SteamUserStats.SetAchievement(achievementName))
			{
				GameLog.LogToConsole($"[Steam] achievement '{achievementName}' was refused; the application's Steamworks configuration does not hold it");
				return false;
			}

			RequestAchievementStore();
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] achievement '{achievementName}' could not be unlocked: {exception.Message}");
			return false;
		}
	}

	/// <summary>
	///     Shows this player how far they are toward an achievement Steam tracks as progress.
	/// </summary>
	/// <param name="achievementName">The name Steam knows the achievement by.</param>
	/// <param name="progress">How much is done, in the units the application's Steamworks configuration counts in.</param>
	/// <param name="maxProgress">The amount that completes it.</param>
	/// <returns>True when Steam was asked to show it.</returns>
	public bool IndicateAchievementProgress(string achievementName, int progress, int maxProgress)
	{
		if (!_initialized || string.IsNullOrEmpty(achievementName) || maxProgress <= 0) return false;

		try
		{
			uint clampedProgress = (uint)Math.Clamp(progress, 0, maxProgress);
			if (SteamUserStats.IndicateAchievementProgress(achievementName, clampedProgress, (uint)maxProgress)) return true;

			GameLog.LogToConsole($"[Steam] progress toward '{achievementName}' was refused; the application's Steamworks configuration does not hold it as a progress achievement");
			return false;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] progress toward '{achievementName}' could not be shown: {exception.Message}");
			return false;
		}
	}

	/// <summary>
	///     Stores an integer stat for this player.
	/// </summary>
	/// <param name="statName">The name Steam knows the stat by, already namespaced for the map it came from.</param>
	/// <param name="value">The value to store.</param>
	/// <returns>True when Steam accepted the value.</returns>
	public bool SetStatInt(string statName, int value)
	{
		if (!_initialized || string.IsNullOrEmpty(statName)) return false;

		try
		{
			if (!SteamUserStats.SetStat(statName, value))
			{
				GameLog.LogToConsole($"[Steam] stat '{statName}' was refused; the application's Steamworks configuration does not hold it as an integer stat");
				return false;
			}

			RequestAchievementStore();
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] stat '{statName}' could not be stored: {exception.Message}");
			return false;
		}
	}

	/// <summary>
	///     Adds to the integer stat this player already holds and stores the result, which keeps a running count without the
	///     caller having to know the value it had.
	/// </summary>
	/// <param name="statName">The name Steam knows the stat by.</param>
	/// <param name="delta">The amount to add, which may be negative.</param>
	/// <returns>True when Steam accepted the new value.</returns>
	public bool AddStatInt(string statName, int delta)
	{
		if (!_initialized || string.IsNullOrEmpty(statName) || delta == 0) return false;

		try
		{
			if (!SteamUserStats.GetStat(statName, out int current))
			{
				GameLog.LogToConsole($"[Steam] stat '{statName}' was refused; the application's Steamworks configuration does not hold it as an integer stat");
				return false;
			}

			return SetStatInt(statName, current + delta);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] stat '{statName}' could not be added to: {exception.Message}");
			return false;
		}
	}

	/// <summary>
	///     Stores a floating point stat for this player.
	/// </summary>
	/// <param name="statName">The name Steam knows the stat by.</param>
	/// <param name="value">The value to store.</param>
	/// <returns>True when Steam accepted the value.</returns>
	public bool SetStatFloat(string statName, float value)
	{
		if (!_initialized || string.IsNullOrEmpty(statName) || !float.IsFinite(value)) return false;

		try
		{
			if (!SteamUserStats.SetStat(statName, value))
			{
				GameLog.LogToConsole($"[Steam] stat '{statName}' was refused; the application's Steamworks configuration does not hold it as a floating point stat");
				return false;
			}

			RequestAchievementStore();
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] stat '{statName}' could not be stored: {exception.Message}");
			return false;
		}
	}

	/// <summary>
	///     Reads every cloud file this map keeps for this player.
	/// </summary>
	/// <param name="mapSlug">The map whose folder inside the cloud is read.</param>
	/// <returns>The files found, and whether cloud storage could be read at all.</returns>
	public (bool Available, List<(string Name, string Content)> Files) ReadCloudFiles(string mapSlug)
	{
		var files = new List<(string Name, string Content)>();
		if (!_initialized) return (false, files);

		string folder = SteamScriptNames.CloudFolder(mapSlug);

		try
		{
			if (!SteamRemoteStorage.IsCloudEnabledForAccount() || !SteamRemoteStorage.IsCloudEnabledForApp()) return (false, files);

			int fileCount = SteamRemoteStorage.GetFileCount();
			for (int index = 0; index < fileCount; index++)
			{
				string fileName = SteamRemoteStorage.GetFileNameAndSize(index, out int sizeInBytes);
				if (string.IsNullOrEmpty(fileName) || sizeInBytes <= 0) continue;
				if (!fileName.StartsWith(folder, StringComparison.Ordinal)) continue;

				byte[] buffer = new byte[sizeInBytes];
				int read = SteamRemoteStorage.FileRead(fileName, buffer, sizeInBytes);
				if (read <= 0) continue;

				files.Add((fileName.Substring(folder.Length), Encoding.UTF8.GetString(buffer, 0, read)));
			}

			return (true, files);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the cloud files of map '{mapSlug}' could not be read: {exception.Message}");
			return (false, files);
		}
	}

	/// <summary>
	///     Writes one cloud file this map keeps for this player.
	/// </summary>
	/// <param name="mapSlug">The map whose folder inside the cloud is written to.</param>
	/// <param name="fileName">The file's name, which is a plain name with no folder in it.</param>
	/// <param name="content">The file's content.</param>
	/// <returns>True when Steam accepted the write.</returns>
	public bool WriteCloudFile(string mapSlug, string fileName, string content)
	{
		if (!_initialized || string.IsNullOrEmpty(fileName) || content == null) return false;

		try
		{
			byte[] bytes = Encoding.UTF8.GetBytes(content);
			return SteamRemoteStorage.FileWrite(SteamScriptNames.CloudFolder(mapSlug) + fileName, bytes, bytes.Length);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] cloud file '{fileName}' of map '{mapSlug}' could not be written: {exception.Message}");
			return false;
		}
	}

	/// <summary>
	///     Deletes one cloud file this map keeps for this player.
	/// </summary>
	/// <param name="mapSlug">The map whose folder inside the cloud is written to.</param>
	/// <param name="fileName">The file's name, which is a plain name with no folder in it.</param>
	/// <returns>True when Steam accepted the delete, which a file that was not there also counts as.</returns>
	public bool DeleteCloudFile(string mapSlug, string fileName)
	{
		if (!_initialized || string.IsNullOrEmpty(fileName)) return false;

		try
		{
			string path = SteamScriptNames.CloudFolder(mapSlug) + fileName;
			if (!SteamRemoteStorage.FileExists(path)) return true;

			return SteamRemoteStorage.FileDelete(path);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] cloud file '{fileName}' of map '{mapSlug}' could not be deleted: {exception.Message}");
			return false;
		}
	}

	/// <summary>
	///     Asks Steam to upload the achievements and stats changed since the last store.
	/// </summary>
	/// <remarks>
	///     Steam batches these itself, but a match can end without another change to trigger a store, so everything that
	///     changes one asks for a store. The flag keeps a burst of unlocks inside one tick from queueing a store each.
	/// </remarks>
	private void RequestAchievementStore()
	{
		if (_achievementStoreRequested) return;

		_achievementStoreRequested = true;
		Callable.From(StoreAchievementsAfterTick).CallDeferred();
	}

	private void StoreAchievementsAfterTick()
	{
		_achievementStoreRequested = false;
		if (!_initialized) return;

		try
		{
			if (!SteamUserStats.StoreStats())
			{
				GameLog.LogToConsole("[Steam] the achievements and stats changed by the map could not be stored; Steam keeps them and the next store retries");
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the achievements and stats changed by the map could not be stored: {exception.Message}");
		}
	}
}
