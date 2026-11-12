using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     The multiplier a player's damage carries against each other player, which is how a rule states that one side of a
///     contest is currently less effective than another without changing the units themselves.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PlayerDamageEffectiveness
{
	public const float FullEffectiveness = 1f;

	[InlineArray(EcsWorld.MAX_PLAYERS)]
	public struct EffectivenessAgainstPlayers
	{
		private float _firstAgainst;
	}

	public EffectivenessAgainstPlayers AgainstPlayers;

	public static PlayerDamageEffectiveness AtFullEffectiveness()
	{
		var table = new PlayerDamageEffectiveness();
		table.Fill(FullEffectiveness);
		return table;
	}

	public void Fill(float multiplier)
	{
		for (int playerIndex = 0; playerIndex < EcsWorld.MAX_PLAYERS; playerIndex++)
		{
			AgainstPlayers[playerIndex] = multiplier;
		}
	}

	public float GetAgainst(int playerIndex)
	{
		if (playerIndex < 0 || playerIndex >= EcsWorld.MAX_PLAYERS) return FullEffectiveness;

		float multiplier = AgainstPlayers[playerIndex];
		return float.IsFinite(multiplier) && multiplier >= 0f ? multiplier : FullEffectiveness;
	}

	public void SetAgainst(int playerIndex, float multiplier)
	{
		if (playerIndex < 0 || playerIndex >= EcsWorld.MAX_PLAYERS) return;

		AgainstPlayers[playerIndex] = float.IsFinite(multiplier) ? MathF.Max(0f, multiplier) : FullEffectiveness;
	}
}
