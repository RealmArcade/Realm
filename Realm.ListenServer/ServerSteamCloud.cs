using System;
using System.Collections.Generic;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The cloud files each seat holds for the map being played, as that seat's screen last uploaded them. A map script
///     cannot read Steam itself, so it asks a seat's screen for its files, this store holds the answer, and the script reads
///     from here until it asks again. Writes and deletes are applied to this copy as well as being sent to the screen, so a
///     map that saves something and immediately reads it back sees what it wrote.
/// </summary>
public class ServerSteamCloud
{
	private const int PlayerSlots = Realm.Ecs.EcsSharedMemory.MAX_PLAYERS;

	private sealed class SeatCloud
	{
		public bool Synced;
		public int RequestId;
		public readonly List<(string Name, string Content)> Files = new();
	}

	private readonly SeatCloud[] _seats = new SeatCloud[PlayerSlots];
	private readonly ServerLog _log;
	private int _lastRequestId;

	public ServerSteamCloud(ServerLog log)
	{
		_log = log;
		for (int slot = 0; slot < _seats.Length; slot++) _seats[slot] = new SeatCloud();
	}

	/// <summary>
	///     Opens a request for one seat's files and answers its handle, which the screen echoes back so an answer to an older
	///     request can be told apart from the answer to this one.
	/// </summary>
	/// <param name="playerSlot">The seat whose files are wanted.</param>
	/// <returns>The handle of the request, or zero for a seat this match does not have.</returns>
	public int BeginRequest(int playerSlot)
	{
		if (!IsKnownSlot(playerSlot)) return 0;

		SeatCloud seat = _seats[playerSlot];
		seat.Synced = false;
		seat.RequestId = NextRequestId();

		return seat.RequestId;
	}

	/// <summary>
	///     Settles a request with nothing in it, which is what a seat that cannot answer at all looks like: the map's poll
	///     finishes rather than waiting for a screen that will never speak.
	/// </summary>
	/// <param name="playerSlot">The seat whose request settled.</param>
	/// <param name="requestId">The handle the request was opened with.</param>
	public void SettleEmpty(int playerSlot, int requestId)
	{
		if (!IsKnownSlot(playerSlot)) return;

		SeatCloud seat = _seats[playerSlot];
		if (seat.RequestId != requestId || seat.Synced) return;

		seat.Files.Clear();
		seat.Synced = true;
	}

	/// <summary>
	///     Takes the files a screen uploaded. An answer whose handle is not the seat's open request is dropped: the map has
	///     asked again since, and the copy it is waiting for is the newer one.
	/// </summary>
	/// <param name="playerSlot">The seat the upload came from.</param>
	/// <param name="upload">What that seat's screen holds.</param>
	/// <returns>Whether the upload was taken.</returns>
	public bool ApplyUpload(int playerSlot, in SteamCloudUpload upload)
	{
		if (!IsKnownSlot(playerSlot)) return false;

		SeatCloud seat = _seats[playerSlot];
		if (seat.RequestId == 0 || seat.RequestId != upload.RequestId)
		{
			_log.Write($"player {playerSlot} uploaded cloud files for request {upload.RequestId}, which is not the request this match is waiting on");
			return false;
		}

		seat.Files.Clear();

		SteamCloudUploadFile[]? files = upload.Files;
		if (files != null && SteamCloudLimits.IsAcceptableUpload(files))
		{
			for (int index = 0; index < files.Length; index++)
			{
				string? name = files[index].Name;
				string? content = files[index].Content;
				if (!SteamCloudLimits.IsAcceptableFileName(name) || !SteamCloudLimits.IsAcceptableContent(content)) continue;

				SetFile(seat, name!, content!);
			}
		}
		else if (files != null)
		{
			_log.Write($"player {playerSlot} uploaded {files.Length} cloud file(s), which is more than one map may hold; the upload was not kept");
		}

		seat.Synced = true;
		return true;
	}

	public bool IsSynced(int playerSlot) => IsKnownSlot(playerSlot) && _seats[playerSlot].Synced;

	public int FileCount(int playerSlot) => IsKnownSlot(playerSlot) ? _seats[playerSlot].Files.Count : 0;

	public string FileName(int playerSlot, int index)
	{
		if (!IsKnownSlot(playerSlot)) return string.Empty;

		List<(string Name, string Content)> files = _seats[playerSlot].Files;
		return index >= 0 && index < files.Count ? files[index].Name : string.Empty;
	}

	public bool Has(int playerSlot, string fileName)
	{
		if (!IsKnownSlot(playerSlot) || !SteamCloudLimits.IsAcceptableFileName(fileName)) return false;

		return IndexOf(_seats[playerSlot].Files, fileName) >= 0;
	}

	public string Read(int playerSlot, string fileName)
	{
		if (!IsKnownSlot(playerSlot) || !SteamCloudLimits.IsAcceptableFileName(fileName)) return string.Empty;

		List<(string Name, string Content)> files = _seats[playerSlot].Files;
		int index = IndexOf(files, fileName);
		return index >= 0 ? files[index].Content : string.Empty;
	}

	/// <summary>
	///     Writes one file the map stated into the copy this match reads, refusing a write that would grow the seat past the
	///     limits one map's cloud is held to.
	/// </summary>
	/// <param name="playerSlot">The seat the file belongs to.</param>
	/// <param name="fileName">The file's name.</param>
	/// <param name="content">The file's content.</param>
	/// <returns>True when the file was written into the copy the match holds.</returns>
	public bool Write(int playerSlot, string fileName, string content)
	{
		if (!IsKnownSlot(playerSlot)) return false;
		if (!SteamCloudLimits.IsAcceptableFileName(fileName) || !SteamCloudLimits.IsAcceptableContent(content)) return false;

		SeatCloud seat = _seats[playerSlot];
		int existing = IndexOf(seat.Files, fileName);
		if (existing < 0 && seat.Files.Count >= SteamCloudLimits.MaximumFilesPerPlayer)
		{
			_log.Write($"player {playerSlot} cannot hold another cloud file for this map: it already holds {seat.Files.Count}");
			return false;
		}

		int othersTotal = 0;
		for (int index = 0; index < seat.Files.Count; index++)
		{
			if (index == existing) continue;
			othersTotal += seat.Files[index].Content.Length;
		}

		if (othersTotal + content.Length > SteamCloudLimits.MaximumUploadCharacters)
		{
			_log.Write($"player {playerSlot} cannot hold a {content.Length} character cloud file for this map: it already holds {othersTotal} characters");
			return false;
		}

		SetFile(seat, fileName, content);
		return true;
	}

	public bool Delete(int playerSlot, string fileName)
	{
		if (!IsKnownSlot(playerSlot) || !SteamCloudLimits.IsAcceptableFileName(fileName)) return false;

		List<(string Name, string Content)> files = _seats[playerSlot].Files;
		int index = IndexOf(files, fileName);
		if (index < 0) return false;

		files.RemoveAt(index);
		return true;
	}

	private static void SetFile(SeatCloud seat, string fileName, string content)
	{
		int index = IndexOf(seat.Files, fileName);
		if (index >= 0)
		{
			seat.Files[index] = (fileName, content);
			return;
		}

		seat.Files.Add((fileName, content));
	}

	private static int IndexOf(List<(string Name, string Content)> files, string fileName)
	{
		for (int index = 0; index < files.Count; index++)
		{
			if (string.Equals(files[index].Name, fileName, StringComparison.Ordinal)) return index;
		}

		return -1;
	}

	private int NextRequestId()
	{
		_lastRequestId = _lastRequestId == int.MaxValue ? 1 : _lastRequestId + 1;
		return _lastRequestId;
	}

	private static bool IsKnownSlot(int playerSlot) => playerSlot > 0 && playerSlot < PlayerSlots;
}
