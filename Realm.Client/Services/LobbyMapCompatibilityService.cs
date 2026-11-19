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
			// A map Steam does not carry is the host's to send, and the lobby connection is the only way to be given it, so
			// the join goes ahead and the map arrives once this screen is admitted. Nothing is prepared here: there is
			// nothing on this machine to prepare, and the copy the host sends is what gets prepared afterwards.
			if (advertisedWorkshopId == 0)
			{
				GameLog.LogToConsole($"[LobbyMapCompatibility] '{mapName}' is not on this machine and the lobby states no Workshop item, so the host will send its copy.");
				return string.Empty;
			}

			return TranslationServer.Translate("This lobby's map is not on this machine yet. Subscribe to it on Steam Workshop, then join again.");
		}

		MapSessionCopyService.MapSessionIdentity identity = MapSessionCopyService.ReadIdentity(installedDirectory);
		if (identity.EngineGeneration > RealmVersion.EngineGeneration)
		{
			return TranslationServer.Translate("This map needs a newer version of Realm. Update the game and try again.");
		}

		// A package hash is the hash of the map's own manifest, which every machine holding the same package reads the same,
		// so two values that differ are genuinely two different packages rather than two ways of describing one. A lobby
		// published on the Workshop can only hand out the item Steam delivers, and a host running something else has to
		// publish it; a lobby running a local copy has no other distributor and sends it over the lobby itself.
		string localHash = MapSessionCopyService.ReadStatedPackageHash(installedDirectory);
		if (advertisedHash.Length > 0 && localHash.Length > 0 && !string.Equals(advertisedHash, localHash, StringComparison.OrdinalIgnoreCase))
		{
			if (advertisedWorkshopId == 0)
			{
				GameLog.LogToConsole($"[LobbyMapCompatibility] this machine holds a different package of '{mapName}' than the lobby runs, and the lobby states no Workshop item, so the host's copy will replace it.");
				return string.Empty;
			}

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
