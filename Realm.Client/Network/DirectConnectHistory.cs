using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.Network;

public record DirectConnectHistoryEntry(string Address, DateTime LastJoinedUtc);

/// <summary>
///     Remembers the direct connect addresses this screen has actually joined, so a player whose host's lobby is not
///     listed keeps the address after closing the game instead of digging the invitation out of a chat again. Only an
///     address that completed a lobby handshake is recorded, and the list sits in this screen's own user data, because it
///     is this player's history rather than anything a lobby holds.
/// </summary>
public static class DirectConnectHistory
{
	private const string HistoryPath = "user://direct_connect_history.json";
	private const int MaximumRememberedAddresses = 8;

	public static List<DirectConnectHistoryEntry> LoadRecent()
	{
		if (!FileAccess.FileExists(HistoryPath)) return new List<DirectConnectHistoryEntry>();

		try
		{
			using FileAccess? file = FileAccess.Open(HistoryPath, FileAccess.ModeFlags.Read);
			if (file == null) return new List<DirectConnectHistoryEntry>();

			return JsonSerializer.Deserialize<List<DirectConnectHistoryEntry>>(file.GetAsText()) ?? new List<DirectConnectHistoryEntry>();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[DirectConnectHistory] The recent addresses cannot be read: {exception.Message}");
			return new List<DirectConnectHistoryEntry>();
		}
	}

	public static void Remember(string host, int port)
	{
		string address = DescribeAddress(host, port);
		var entries = LoadRecent().Where(entry => !string.Equals(entry.Address, address, StringComparison.OrdinalIgnoreCase)).ToList();
		entries.Insert(0, new DirectConnectHistoryEntry(address, DateTime.UtcNow));
		if (entries.Count > MaximumRememberedAddresses) entries.RemoveRange(MaximumRememberedAddresses, entries.Count - MaximumRememberedAddresses);

		try
		{
			using FileAccess? file = FileAccess.Open(HistoryPath, FileAccess.ModeFlags.Write);
			file?.StoreString(JsonSerializer.Serialize(entries));
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[DirectConnectHistory] The recent addresses cannot be written: {exception.Message}");
		}
	}

	/// <summary>
	///     Writes an address the way the direct connect field reads it back. An IPv6 host carries colons of its own, so it
	///     is bracketed or the field would take its last group for a port.
	/// </summary>
	/// <param name="host">The host to reach.</param>
	/// <param name="port">The port to reach it on.</param>
	/// <returns>The address in the form players paste.</returns>
	public static string DescribeAddress(string host, int port)
	{
		string trimmed = host.Trim().Trim('[', ']');
		return trimmed.Contains(':') ? $"[{trimmed}]:{port}" : $"{trimmed}:{port}";
	}
}
