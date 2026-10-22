using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Pre-calculated matrix storing pairwise relationship states between all players.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct PlayerRelationshipMatrix
{
	public const int MaxPlayers = 25;
	public const int TotalEntries = MaxPlayers * MaxPlayers;

	public fixed byte Relationships[TotalEntries];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public PlayerRelationship GetRelationship(int playerIndex1, int playerIndex2)
	{
		if ((uint)playerIndex1 >= MaxPlayers || (uint)playerIndex2 >= MaxPlayers)
		{
			return PlayerRelationship.Neutral;
		}

		return (PlayerRelationship)Relationships[playerIndex1 * MaxPlayers + playerIndex2];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void SetRelationship(int playerIndex1, int playerIndex2, PlayerRelationship relationship)
	{
		if ((uint)playerIndex1 >= MaxPlayers || (uint)playerIndex2 >= MaxPlayers)
		{
			return;
		}

		byte relationshipByte = (byte)relationship;
		Relationships[playerIndex1 * MaxPlayers + playerIndex2] = relationshipByte;
		Relationships[playerIndex2 * MaxPlayers + playerIndex1] = relationshipByte;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool AreEnemies(int playerIndex1, int playerIndex2)
	{
		if (playerIndex1 == playerIndex2)
		{
			return false;
		}

		if ((uint)playerIndex1 >= MaxPlayers || (uint)playerIndex2 >= MaxPlayers)
		{
			return false;
		}

		return (Relationships[playerIndex1 * MaxPlayers + playerIndex2] & (byte)PlayerRelationship.Enemy) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool AreAllied(int playerIndex1, int playerIndex2)
	{
		if (playerIndex1 == playerIndex2)
		{
			return true;
		}

		if ((uint)playerIndex1 >= MaxPlayers || (uint)playerIndex2 >= MaxPlayers)
		{
			return false;
		}

		return (Relationships[playerIndex1 * MaxPlayers + playerIndex2] & (byte)PlayerRelationship.Ally) != 0;
	}
}
