using System;
using System.Numerics;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The Steam surface of the map API: achievements, stats and per-player cloud files. The authority has no Steam of its
///     own, so an achievement or a stat is written as a request the seat's own screen applies, and cloud files are the copy
///     each seat's screen uploaded when the map asked for it.
/// </summary>
public partial class ServerGameAPI
{
	private readonly ServerSteamCloud _steamCloud;
	private Func<int, bool>? _steamSeatProvider;

	/// <summary>
	///     States how the match decides whether a seat is a person playing inside Steam. The authority answers from the hello
	///     each screen sent, so a seat that is a computer, a seat nobody holds and a screen without Steam all answer false.
	/// </summary>
	/// <param name="steamSeatProvider">The predicate the match seats Steam clients with.</param>
	public void BindSteamSeats(Func<int, bool>? steamSeatProvider)
	{
		_steamSeatProvider = steamSeatProvider;
	}

	public bool IsSteamAvailable(int playerIndex)
	{
		if (playerIndex <= 0) return false;

		return _steamSeatProvider != null && _steamSeatProvider(playerIndex);
	}

	public void UnlockSteamAchievement(int playerIndex, string achievementId)
	{
		if (!CanAddressSteam(playerIndex, achievementId, "an achievement")) return;

		_presentation.Write(PresentationKind.SteamAchievementUnlocked, playerIndex, 0, achievementId, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void IndicateSteamAchievementProgress(int playerIndex, string achievementId, int progress, int maxProgress)
	{
		if (!CanAddressSteam(playerIndex, achievementId, "an achievement")) return;
		if (maxProgress <= 0)
		{
			_log.Write($"player {playerIndex} was shown progress toward '{achievementId}' out of {maxProgress}, which states no goal");
			return;
		}

		int clampedProgress = Math.Clamp(progress, 0, maxProgress);
		_presentation.Write(PresentationKind.SteamAchievementProgress, playerIndex, 0, achievementId, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, clampedProgress, maxProgress, 0f, 0f, 0f, false);
	}

	public void SetSteamStatInt(int playerIndex, string statId, int value)
	{
		if (!CanAddressSteam(playerIndex, statId, "a stat")) return;

		_presentation.Write(PresentationKind.SteamStatIntSet, playerIndex, 0, statId, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, value, 0, 0f, 0f, 0f, false);
	}

	public void AddSteamStatInt(int playerIndex, string statId, int delta)
	{
		if (!CanAddressSteam(playerIndex, statId, "a stat")) return;
		if (delta == 0) return;

		_presentation.Write(PresentationKind.SteamStatIntAdd, playerIndex, 0, statId, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, delta, 0, 0f, 0f, 0f, false);
	}

	public void SetSteamStatFloat(int playerIndex, string statId, float value)
	{
		if (!CanAddressSteam(playerIndex, statId, "a stat")) return;
		if (!float.IsFinite(value))
		{
			_log.Write($"player {playerIndex} was given the value {value} for Steam stat '{statId}', which is not a number Steam can store");
			return;
		}

		_presentation.Write(PresentationKind.SteamStatFloatSet, playerIndex, 0, statId, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, value, 0f, 0f, false);
	}

	public void RequestSteamCloudFiles(int playerIndex)
	{
		if (playerIndex <= 0) return;
		if (!IsSteamAvailable(playerIndex))
		{
			// A seat with no Steam behind it settles at once with nothing, so a map waiting for the answer is not left polling
			// a screen that will never speak.
			_steamCloud.SettleEmpty(playerIndex, _steamCloud.BeginRequest(playerIndex));
			return;
		}

		int requestId = _steamCloud.BeginRequest(playerIndex);
		_presentation.Write(PresentationKind.SteamCloudFilesRequested, playerIndex, requestId, string.Empty, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public bool IsSteamCloudSynced(int playerIndex) => _steamCloud.IsSynced(playerIndex);

	public int GetSteamCloudFileCount(int playerIndex) => _steamCloud.FileCount(playerIndex);

	public string GetSteamCloudFileName(int playerIndex, int index) => _steamCloud.FileName(playerIndex, index);

	public bool HasSteamCloudFile(int playerIndex, string fileName) => _steamCloud.Has(playerIndex, fileName);

	public string ReadSteamCloudFile(int playerIndex, string fileName) => _steamCloud.Read(playerIndex, fileName);

	public bool WriteSteamCloudFile(int playerIndex, string fileName, string content)
	{
		if (playerIndex <= 0) return false;
		if (!_steamCloud.Write(playerIndex, fileName, content))
		{
			_log.Write($"player {playerIndex} could not hold cloud file '{fileName}' ({content?.Length ?? 0} character(s)) for this map");
			return false;
		}

		if (!IsSteamAvailable(playerIndex)) return true;

		_presentation.Write(PresentationKind.SteamCloudFileWritten, playerIndex, 0, fileName, content ?? string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
		return true;
	}

	public bool DeleteSteamCloudFile(int playerIndex, string fileName)
	{
		if (playerIndex <= 0) return false;
		if (!_steamCloud.Delete(playerIndex, fileName)) return false;
		if (!IsSteamAvailable(playerIndex)) return true;

		_presentation.Write(PresentationKind.SteamCloudFileDeleted, playerIndex, 0, fileName, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
		return true;
	}

	/// <summary>
	///     Takes the cloud files one seat's screen answered a request with.
	/// </summary>
	/// <param name="playerSlot">The seat the answer came from.</param>
	/// <param name="upload">What that seat's screen holds for this map.</param>
	public void ApplySteamCloudUpload(int playerSlot, in SteamCloudUpload upload) => _steamCloud.ApplyUpload(playerSlot, in upload);

	/// <summary>
	///     Whether a call that names one player and one identifier can be addressed anywhere: the seat has to be a person's
	///     and the identifier has to state something, and a call naming anything else is dropped instead of queued.
	/// </summary>
	/// <param name="playerIndex">The seat the call names.</param>
	/// <param name="identifier">The achievement or stat identifier the call names.</param>
	/// <param name="description">What the identifier is, for the log line.</param>
	/// <returns>True when the call may be sent to that seat's screen.</returns>
	private bool CanAddressSteam(int playerIndex, string identifier, string description)
	{
		if (playerIndex <= 0) return false;
		if (string.IsNullOrWhiteSpace(identifier))
		{
			_log.Write($"player {playerIndex} was given no identifier for {description}");
			return false;
		}

		return IsSteamAvailable(playerIndex);
	}
}
