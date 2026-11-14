using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Shared.Network;

namespace Realm.Client;

public static class MatchPlayerColorAssignment
{
	private const int TrackedPlayerSlots = 32;

	private static readonly Color[] AssignedColors = BuildPaletteDefaults();
	private static readonly bool[] TakenPaletteIndices = new bool[PlayerColorConfig.Palette.Length];
	private static int _seatsSignature = int.MinValue;

	public static void RefreshIfSeatsChanged(IReadOnlyList<PlayerInfo>? seats)
	{
		int signature = ComputeSignature(seats);
		if (signature == _seatsSignature) return;

		_seatsSignature = signature;
		Assign(seats);
	}

	public static Color Get(int playerIndex)
	{
		if (playerIndex >= 0 && playerIndex < AssignedColors.Length) return AssignedColors[playerIndex];

		return PlayerColorConfig.GetColor(playerIndex);
	}

	/// <summary>
	///     Reads the colour one player's units are drawn in on a minimap of one scheme, when the match's map stated one.
	/// </summary>
	/// <param name="playerIndex">The player index.</param>
	/// <param name="scheme">Which of the minimap's two colourings is being drawn.</param>
	/// <param name="color">The colour the map stated for that player under that scheme.</param>
	/// <returns>
	///     Whether the map stated a colour, false when the scheme's own reading of the player decides how it is drawn.
	/// </returns>
	public static bool TryGetMinimapColor(int playerIndex, MinimapColorScheme scheme, out Color color)
	{
		color = Colors.Transparent;

		LobbyManager? lobby = GameContext.Lobby;
		if (lobby == null) return false;

		uint packed = LobbyManager.ColorAt(scheme == MinimapColorScheme.PlayerColors ? lobby.MinimapPlayerColors : lobby.MinimapTeamColors, playerIndex);
		if (packed == 0u) return false;

		color = Unpack(packed);
		return true;
	}

	private static Color[] BuildPaletteDefaults()
	{
		var defaults = new Color[TrackedPlayerSlots];
		for (int slot = 0; slot < defaults.Length; slot++)
		{
			defaults[slot] = PlayerColorConfig.GetColor(slot);
		}

		return defaults;
	}

	/// <summary>
	///     Identifies the seat list and the colours the match's map keeps for its player indexes, so a change to either is
	///     what makes the assignment run again.
	/// </summary>
	/// <param name="seats">The seats the lobby published.</param>
	/// <returns>A value that differs between two different seat-and-overlay readings.</returns>
	private static int ComputeSignature(IReadOnlyList<PlayerInfo>? seats)
	{
		LobbyManager? lobby = GameContext.Lobby;

		unchecked
		{
			int signature = seats?.Count ?? 0;

			if (seats != null)
			{
				for (int index = 0; index < seats.Count; index++)
				{
					PlayerInfo seat = seats[index];
					if (seat == null) continue;

					signature = (signature * 397) ^ seat.Slot;
					signature = (signature * 397) ^ (int)seat.Color.ToRgba32();
				}
			}

			for (int slot = 0; slot <= LobbyLimits.MaximumSeats; slot++)
			{
				signature = (signature * 397) ^ (int)LobbyManager.ColorAt(lobby?.ColorOverlays, slot);
			}

			return signature == 0 ? 1 : signature;
		}
	}

	private static void Assign(IReadOnlyList<PlayerInfo>? seats)
	{
		for (int slot = 0; slot < AssignedColors.Length; slot++)
		{
			AssignedColors[slot] = PlayerColorConfig.GetColor(slot);
		}

		Array.Fill(TakenPaletteIndices, false);
		TakenPaletteIndices[PlayerColorConfig.NEUTRAL_PLAYER_INDEX] = true;

		if (seats != null)
		{
			for (int index = 0; index < seats.Count; index++)
			{
				PlayerInfo seat = seats[index];
				if (seat == null || seat.Slot <= 0 || seat.Slot >= AssignedColors.Length) continue;

				AssignedColors[seat.Slot] = seat.Color;
				TakenPaletteIndices[PlayerColorConfig.GetColorIndex(seat.Color)] = true;
			}
		}

		LobbyManager? lobby = GameContext.Lobby;
		for (int slot = 1; slot < AssignedColors.Length; slot++)
		{
			uint overlay = LobbyManager.ColorAt(lobby?.ColorOverlays, slot);
			if (overlay != 0u) AssignedColors[slot] = Unpack(overlay);
		}

		int nextPaletteIndex = PlayerColorConfig.NEUTRAL_PLAYER_INDEX + 1;
		for (int slot = 1; slot < AssignedColors.Length; slot++)
		{
			if (IsSeated(seats, slot)) continue;
			if (LobbyManager.ColorAt(lobby?.ColorOverlays, slot) != 0u) continue;

			while (nextPaletteIndex < TakenPaletteIndices.Length && TakenPaletteIndices[nextPaletteIndex]) nextPaletteIndex++;
			if (nextPaletteIndex >= TakenPaletteIndices.Length) break;

			AssignedColors[slot] = PlayerColorConfig.GetColor(nextPaletteIndex);
			TakenPaletteIndices[nextPaletteIndex] = true;
		}
	}

	private static bool IsSeated(IReadOnlyList<PlayerInfo>? seats, int slot)
	{
		if (seats == null) return false;

		for (int index = 0; index < seats.Count; index++)
		{
			PlayerInfo seat = seats[index];
			if (seat != null && seat.Slot == slot) return true;
		}

		return false;
	}

	private static Color Unpack(uint rgba)
	{
		float red = ((rgba >> 24) & 0xFF) / 255.0f;
		float green = ((rgba >> 16) & 0xFF) / 255.0f;
		float blue = ((rgba >> 8) & 0xFF) / 255.0f;
		float alpha = (rgba & 0xFF) / 255.0f;
		return new Color(red, green, blue, alpha);
	}
}
