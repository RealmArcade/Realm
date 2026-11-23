using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services.Steam;
using Realm.Shared;

namespace Realm.Client.Services;

/// <summary>
///     Decides whether this machine can play the map a Steam lobby advertises. The lobby states the Workshop item, the
///     hash of the package it will run and the engine generation the map declares, and this compares them with what the
///     joining screen holds: the same item and the same hash means the two machines play the same build of the map, a
///     missing map is the Workshop's to deliver, a newer generation needs a newer game, and an older one is ordinary
///     because the map's data is migrated on a private copy.
/// </summary>
/// <remarks>
///     A lobby that names an item is answered by that item rather than by the map's name, so the copy this screen plays is
///     the one Steam installed for exactly that item. A name can be shared by two creators and by local folders this screen
///     happens to hold, and none of those is what the host is running.
/// </remarks>
public static class LobbyMapCompatibilityService
{
	/// <summary>
	///     Checks the map a lobby advertises and leaves a private copy of it ready to play when the join may go ahead.
	/// </summary>
	/// <param name="mapName">The map's path name, as the lobby states it.</param>
	/// <param name="advertisedWorkshopId">The Workshop item the lobby's map is published as, or zero when it states none.</param>
	/// <param name="advertisedHash">The hash of the package the host will run, or empty when it states none.</param>
	/// <param name="advertisedGeneration">The engine generation the lobby states, as text.</param>
	/// <returns>Why this screen cannot join, written for the player, or an empty string when it can.</returns>
	public static async Task<string> EvaluateAsync(string mapName, ulong advertisedWorkshopId, string advertisedHash, string advertisedGeneration)
	{
		if (string.IsNullOrWhiteSpace(mapName)) return string.Empty;

		if (int.TryParse(advertisedGeneration, out int lobbyGeneration) && lobbyGeneration > RealmVersion.EngineGeneration)
		{
			return TranslationServer.Translate("This map needs a newer version of Realm. Update the game and try again.");
		}

		if (advertisedWorkshopId != 0) return await EvaluateAdvertisedItemAsync(mapName, advertisedWorkshopId, advertisedHash);

		string? installedDirectory = GameHost.ResolveMapDirectory(mapName);
		if (string.IsNullOrEmpty(installedDirectory))
		{
			// A map Steam does not carry is the host's to send, and the lobby connection is the only way to be given it, so
			// the join goes ahead and the map arrives once this screen is admitted. Nothing is prepared here: there is
			// nothing on this machine to prepare, and the copy the host sends is what gets prepared afterwards.
			GameLog.LogToConsole($"[LobbyMapCompatibility] '{mapName}' is not on this machine and the lobby states no Workshop item, so the host will send its copy.");
			return string.Empty;
		}

		MapSessionCopyService.MapSessionIdentity identity = MapSessionCopyService.ReadIdentity(installedDirectory);
		if (identity.EngineGeneration > RealmVersion.EngineGeneration)
		{
			return TranslationServer.Translate("This map needs a newer version of Realm. Update the game and try again.");
		}

		// A package hash is the hash of the map's own manifest, which every machine holding the same package reads the same,
		// so two values that differ are genuinely two different packages rather than two ways of describing one. A lobby
		// running a local copy has no other distributor and sends it over the lobby itself, which replaces whatever this
		// screen holds under that name.
		string localHash = MapSessionCopyService.ReadStatedPackageHash(installedDirectory);
		if (advertisedHash.Length > 0 && localHash.Length > 0 && !string.Equals(advertisedHash, localHash, StringComparison.OrdinalIgnoreCase))
		{
			GameLog.LogToConsole($"[LobbyMapCompatibility] this machine holds a different package of '{mapName}' than the lobby runs, and the lobby states no Workshop item, so the host's copy will replace it.");
			return string.Empty;
		}

		return PrepareForPlay(installedDirectory);
	}

	/// <summary>
	///     Settles a join by the item the lobby names, which is the copy every screen is told to take and the only one that
	///     can be checked against the lobby's stated package before anything is transferred.
	/// </summary>
	/// <param name="mapName">The map's path name, as the lobby states it.</param>
	/// <param name="advertisedWorkshopId">The item the lobby names.</param>
	/// <param name="advertisedHash">The hash of the package the host will run, or empty when it states none.</param>
	/// <returns>Why this screen cannot join, or an empty string when it can.</returns>
	private static async Task<string> EvaluateAdvertisedItemAsync(string mapName, ulong advertisedWorkshopId, string advertisedHash)
	{
		string? itemDirectory = await EnsureAdvertisedItemAsync(advertisedWorkshopId, advertisedHash);
		if (itemDirectory == null)
		{
			// Steam no longer hands out the package this lobby states, which is what an update published under a lobby that
			// is still open looks like. The host still holds the bytes it plays and can send them, so the join falls back to
			// the lobby's own copy rather than refusing a player who could be seated.
			GameLog.LogToConsole($"[LobbyMapCompatibility] Workshop item {advertisedWorkshopId} no longer carries the package lobby '{mapName}' plays, so this screen takes the host's copy instead.");
			return string.Empty;
		}

		return PrepareForPlay(itemDirectory);
	}

	/// <summary>
	///     Answers the folder Steam installed an item into, once that folder is known to carry the package the lobby states,
	///     subscribing and waiting for the download when the item is missing or has an update pending.
	/// </summary>
	/// <param name="advertisedWorkshopId">The item to install.</param>
	/// <param name="advertisedHash">The hash of the package the lobby will run, or empty when it states none.</param>
	/// <returns>The item's folder, or null when Steam cannot supply the package the lobby states.</returns>
	private static async Task<string?> EnsureAdvertisedItemAsync(ulong advertisedWorkshopId, string advertisedHash)
	{
		string? installedDirectory = InstalledItemFolder(advertisedWorkshopId, advertisedHash);
		if (installedDirectory != null) return installedDirectory;

		if (!await SteamWorkshopService.InstallAsync(advertisedWorkshopId, null, CancellationToken.None))
		{
			GameLog.LogToConsole($"[LobbyMapCompatibility] Workshop item {advertisedWorkshopId} could not be installed, so this screen takes the host's copy instead.");
			return null;
		}

		return InstalledItemFolder(advertisedWorkshopId, advertisedHash);
	}

	/// <summary>
	///     Answers the folder Steam installed an item into, when the package that folder states is the package the lobby
	///     states. A folder that states a different one is a version the host is not running, and a folder that states
	///     nothing cannot be checked at all, so both answer null.
	/// </summary>
	/// <param name="advertisedWorkshopId">The item to locate.</param>
	/// <param name="advertisedHash">The hash of the package the lobby will run, or empty when it states none.</param>
	/// <returns>The item's folder, or null when it is not installed or carries another package.</returns>
	private static string? InstalledItemFolder(ulong advertisedWorkshopId, string advertisedHash)
	{
		string? itemDirectory = SteamWorkshopService.GetInstalledFolder(advertisedWorkshopId);
		if (string.IsNullOrEmpty(itemDirectory) || !Directory.Exists(itemDirectory)) return null;

		if (advertisedHash.Length == 0) return itemDirectory;

		string itemHash = MapSessionCopyService.ReadStatedPackageHash(itemDirectory);
		if (itemHash.Length == 0 || !string.Equals(itemHash, advertisedHash, StringComparison.OrdinalIgnoreCase)) return null;

		return itemDirectory;
	}

	/// <summary>
	///     Leaves the private copy this lobby plays from ready, which is the package migrated to this build and the folder
	///     the room, the authority and this screen then read.
	/// </summary>
	/// <param name="installedDirectory">The installed or authored folder the package was read from.</param>
	/// <returns>Why this screen cannot join, or an empty string when the copy is ready.</returns>
	private static string PrepareForPlay(string installedDirectory)
	{
		string? sessionDirectory = GameContext.MapSessionCopy.TryPrepare(installedDirectory);
		if (string.IsNullOrEmpty(sessionDirectory)) return GameContext.MapSessionCopy.LastRefusal;

		if (GameContext.Lobby != null) GameContext.Lobby.ActiveMapSessionDirectory = sessionDirectory;
		return string.Empty;
	}
}
