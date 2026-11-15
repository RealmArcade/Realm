using System;
using System.Collections.Generic;
using System.IO;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Shared.Network;
using Realm.Shared.Steam;

namespace Realm.Client.Services.Steam;

/// <summary>
///     Applies the Steam requests a map script raised through <c>IGameAPI</c>. The script runs in the authority and Steam
///     runs here, so this screen is the only place those requests can become something Steam knows: an achievement, a stat,
///     or a file in the player's cloud.
/// </summary>
/// <remarks>
///     Only the screen sitting in the seat the request names applies it. An ally or a spectator also controls what the request
///     is about, but their Steam account is not the one the map meant, so the seat is tested against this screen's own slot
///     and nothing else.
/// </remarks>
public static class SteamScriptBridge
{
	/// <summary>
	///     Applies one presentation request the map raised, when it is one of the Steam requests and it names this screen's
	///     seat.
	/// </summary>
	/// <param name="message">The request the match delivered.</param>
	public static void Apply(in PresentationMessage message)
	{
		PresentationKind kind = (PresentationKind)message.Kind;
		if (!IsSteamRequest(kind)) return;

		// A spectator controls no seat, so asking which one it is raises. Nothing a map raises for a seat may be applied by
		// somebody who does not own it, whether they are watching or fighting beside it.
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (message.PlayerIndex <= 0 || message.PlayerIndex != GameContext.PerspectiveFilter.ControlledPlayerIndex) return;

		SteamService steam = GameContext.Steam;
		if (!steam.IsAvailable) return;

		string mapSlug = CurrentMapSlug();

		switch (kind)
		{
			case PresentationKind.SteamAchievementUnlocked:
				steam.UnlockAchievement(SteamScriptNames.ForMap(mapSlug, message.Key));
				break;

			case PresentationKind.SteamAchievementProgress:
				steam.IndicateAchievementProgress(SteamScriptNames.ForMap(mapSlug, message.Key), message.Count, message.SecondCount);
				break;

			case PresentationKind.SteamStatIntSet:
				steam.SetStatInt(SteamScriptNames.ForMap(mapSlug, message.Key), message.Count);
				break;

			case PresentationKind.SteamStatIntAdd:
				steam.AddStatInt(SteamScriptNames.ForMap(mapSlug, message.Key), message.Count);
				break;

			case PresentationKind.SteamStatFloatSet:
				steam.SetStatFloat(SteamScriptNames.ForMap(mapSlug, message.Key), message.Value);
				break;

			case PresentationKind.SteamCloudFilesRequested:
				UploadCloudFiles(steam, mapSlug, message.Handle);
				break;

			case PresentationKind.SteamCloudFileWritten:
				steam.WriteCloudFile(mapSlug, message.Key, message.Text);
				break;

			case PresentationKind.SteamCloudFileDeleted:
				steam.DeleteCloudFile(mapSlug, message.Key);
				break;
		}
	}

	/// <summary>
	///     Whether a presentation request is one of the Steam requests, which are the only ones this bridge applies.
	/// </summary>
	/// <param name="kind">The request's kind.</param>
	/// <returns>True when the request is a Steam one.</returns>
	public static bool IsSteamRequest(PresentationKind kind)
	{
		return kind is PresentationKind.SteamAchievementUnlocked
			or PresentationKind.SteamAchievementProgress
			or PresentationKind.SteamStatIntSet
			or PresentationKind.SteamStatIntAdd
			or PresentationKind.SteamStatFloatSet
			or PresentationKind.SteamCloudFilesRequested
			or PresentationKind.SteamCloudFileWritten
			or PresentationKind.SteamCloudFileDeleted;
	}

	/// <summary>
	///     Reads this screen's cloud files for the map being played and hands them to the match, which is the answer a map's
	///     cloud request is waiting for.
	/// </summary>
	/// <param name="steam">This screen's Steamworks service.</param>
	/// <param name="mapSlug">The map whose folder inside the cloud is read.</param>
	/// <param name="requestId">The handle the map's request was opened with.</param>
	private static void UploadCloudFiles(SteamService steam, string mapSlug, int requestId)
	{
		(bool available, List<(string Name, string Content)> files) = steam.ReadCloudFiles(mapSlug);

		var uploadFiles = new SteamCloudUploadFile[files.Count];
		for (int index = 0; index < files.Count; index++)
		{
			uploadFiles[index] = new SteamCloudUploadFile
			{
				Name = files[index].Name,
				Content = files[index].Content
			};
		}

		var upload = new SteamCloudUpload
		{
			RequestId = requestId,
			CloudAvailable = available,
			Files = uploadFiles
		};

		GameContext.ServerSession?.SendSteamCloudUpload(upload);
	}

	/// <summary>
	///     The map being played, as the cloud folder and the achievement names take it: the name the match was started under,
	///     or the map folder this screen loaded when no match name was stated.
	/// </summary>
	/// <returns>The map's slug.</returns>
	private static string CurrentMapSlug()
	{
		string? matchMapName = GameContext.Lobby?.ActiveMapName;
		if (!string.IsNullOrWhiteSpace(matchMapName)) return Path.GetFileNameWithoutExtension(matchMapName.Replace('\\', '/'));

		string directory = GameContext.MapStorageService.CurrentMapDirectory;
		return string.IsNullOrWhiteSpace(directory) ? string.Empty : Path.GetFileName(directory.Replace('\\', '/').TrimEnd('/'));
	}
}
