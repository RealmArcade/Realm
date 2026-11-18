using System;
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

		string? installedDirectory = GameHost.ResolveMapDirectory(mapName);
		if (string.IsNullOrEmpty(installedDirectory) && advertisedWorkshopId != 0)
		{
			installedDirectory = await SubscribeAndResolveAsync(mapName, advertisedWorkshopId);
		}

		if (string.IsNullOrEmpty(installedDirectory))
		{
			return advertisedWorkshopId != 0
				? TranslationServer.Translate("This lobby's map is not on this machine yet. Subscribe to it on Steam Workshop, then join again.")
				: TranslationServer.Translate("This map is not on this machine, and the host's copy cannot be downloaded from Steam Workshop.");
		}

		MapSessionCopyService.MapSessionIdentity identity = MapSessionCopyService.ReadIdentity(installedDirectory);
		if (identity.EngineGeneration > RealmVersion.EngineGeneration)
		{
			return TranslationServer.Translate("This map needs a newer version of Realm. Update the game and try again.");
		}

		string localHash = MapSessionCopyService.ReadStatedPackageHash(installedDirectory);
		if (advertisedHash.Length > 0 && localHash.Length > 0 && !string.Equals(advertisedHash, localHash, StringComparison.OrdinalIgnoreCase))
		{
			return TranslationServer.Translate("The host has a different version of this map installed. The host needs to update it on Steam Workshop and open the lobby again.");
		}

		string? sessionDirectory = GameContext.MapSessionCopy.TryPrepare(installedDirectory);
		if (string.IsNullOrEmpty(sessionDirectory)) return GameContext.MapSessionCopy.LastRefusal;

		if (GameContext.Lobby != null) GameContext.Lobby.ActiveMapSessionDirectory = sessionDirectory;
		return string.Empty;
	}

	private static async Task<string?> SubscribeAndResolveAsync(string mapName, ulong workshopId)
	{
		bool subscribed = await SteamWorkshopService.InstallAsync(workshopId, null, System.Threading.CancellationToken.None);
		if (!subscribed)
		{
			GameLog.LogToConsole($"[LobbyMapCompatibility] Workshop item {workshopId} for '{mapName}' could not be installed, so the join stops here.");
			return null;
		}

		GameContext.AssetIndexService?.RescanAllDirectories();
		return GameHost.ResolveMapDirectory(mapName);
	}
}
