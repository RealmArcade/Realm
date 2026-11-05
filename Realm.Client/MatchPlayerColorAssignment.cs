using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Network;

namespace Realm.Client;

public static class MatchPlayerColorAssignment
{
	private const int TrackedPlayerSlots = 32;

	private static readonly Color[] AssignedColors = BuildPaletteDefaults();
	private static readonly bool[] TakenPaletteIndices = new bool[PlayerColorConfig.Palette.Length];
	private static int _seatsSignature = int.MinValue;

	public static void RefreshIfSeatsChanged(IReadOnlyList<PlayerInfo> seats)
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

	private static Color[] BuildPaletteDefaults()
	{
		var defaults = new Color[TrackedPlayerSlots];
		for (int slot = 0; slot < defaults.Length; slot++)
		{
			defaults[slot] = PlayerColorConfig.GetColor(slot);
		}

		return defaults;
	}

	private static int ComputeSignature(IReadOnlyList<PlayerInfo> seats)
	{
		if (seats == null || seats.Count == 0) return 0;

		unchecked
		{
			int signature = seats.Count;
			for (int index = 0; index < seats.Count; index++)
			{
				PlayerInfo seat = seats[index];
				if (seat == null) continue;

				signature = (signature * 397) ^ seat.Slot;
				signature = (signature * 397) ^ (int)seat.Color.ToRgba32();
			}

			return signature == 0 ? 1 : signature;
		}
	}

	private static void Assign(IReadOnlyList<PlayerInfo> seats)
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

		int nextPaletteIndex = PlayerColorConfig.NEUTRAL_PLAYER_INDEX + 1;
		for (int slot = 1; slot < AssignedColors.Length; slot++)
		{
			if (IsSeated(seats, slot)) continue;

			while (nextPaletteIndex < TakenPaletteIndices.Length && TakenPaletteIndices[nextPaletteIndex]) nextPaletteIndex++;
			if (nextPaletteIndex >= TakenPaletteIndices.Length) break;

			AssignedColors[slot] = PlayerColorConfig.GetColor(nextPaletteIndex);
			TakenPaletteIndices[nextPaletteIndex] = true;
		}
	}

	private static bool IsSeated(IReadOnlyList<PlayerInfo> seats, int slot)
	{
		if (seats == null) return false;

		for (int index = 0; index < seats.Count; index++)
		{
			PlayerInfo seat = seats[index];
			if (seat != null && seat.Slot == slot) return true;
		}

		return false;
	}
}
