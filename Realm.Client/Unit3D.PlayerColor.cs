using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Animation;

namespace Realm.Client;
public partial class Unit3D
{
	private int _player = 0;

	public int Player
	{
		get
		{
			if (GameContext.Host != null && GameContext.World is { } world
			                              && world.IsAlive(Entity) && world.Has<UnitOwnerPlayer>(Entity))
				return world.Get<UnitOwnerPlayer>(Entity).PlayerIndex;
			return _player;
		}
		set
		{
			_player = value;
			if (GameContext.Host != null && GameContext.World is { } world && world.IsAlive(Entity))
			{
				world.SetOrAdd(Entity, new UnitOwnerPlayer(value));
			}

			UpdatePlayerColorVisual();
		}
	}

	public int PlayerIndex
	{
		get => Player;
		set => Player = value;
	}

	public Color PlayerColor { get; private set; } = PlayerColorConfig.GetColor(0);

	public void SetPlayer(int playerIndex)
	{
		Player = playerIndex;
	}

	public void SetPlayerColor(Color color)
	{
		PlayerColor = color;
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			bool ignorePlayerColor = GameContext.Host != null && (GameContext.Host.GetModelIgnorePlayerColor(ModelPath) || GameContext.Host.GetModelIgnorePlayerColor(UnitId));
			ModelShaderManager.SetPlayerColor(_modelNode, color);
			ModelShaderManager.SetPlayerColorOnAttachments(_modelNode, color);
			ModelShaderManager.SetIgnorePlayerColor(_modelNode, ignorePlayerColor);
		}

		if (_playerColorRing != null && GodotObject.IsInstanceValid(_playerColorRing))
		{
			_playerColorRing.MaterialOverride = Realm.Client.Utils.SharedMaterials.GetOrCreatePlayerColorGlowMaterial(color);
		}

		foreach (var glowingSocket in _pseudoSocketGlows)
		{
			if (glowingSocket.Value != null && GodotObject.IsInstanceValid(glowingSocket.Value))
			{
				glowingSocket.Value.MaterialOverride = Realm.Client.Utils.SharedMaterials.GetOrCreatePlayerColorGlowMaterial(color);
			}
		}

		foreach (var socket in _pseudoSockets.Values)
		{
			if (socket == null || !GodotObject.IsInstanceValid(socket)) continue;
			ApplyPlayerColorToGroundAuraVfx(socket, color);
		}
	}

	private static void ApplyPlayerColorToGroundAuraVfx(Node node, Color color)
	{
		if (node is ProceduralVfxInstance3D vfx)
		{
			vfx.SetPlayerColor(color);
		}

		foreach (Node child in node.GetChildren())
		{
			if (GodotObject.IsInstanceValid(child))
			{
				ApplyPlayerColorToGroundAuraVfx(child, color);
			}
		}
	}

	public void InterpolatePlayerColor(Color targetColor, float weight)
	{
		Color blended = PlayerColor.Lerp(targetColor, weight);
		SetPlayerColor(blended);
	}

	public void UpdatePlayerColorVisual()
	{
		bool ignorePlayerColor = GameContext.Host != null && (GameContext.Host.GetModelIgnorePlayerColor(ModelPath) || GameContext.Host.GetModelIgnorePlayerColor(UnitId));
		if (ignorePlayerColor)
		{
			if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
			{
				ModelShaderManager.SetIgnorePlayerColor(_modelNode, true);
			}

			return;
		}

		MatchPlayerColorAssignment.RefreshIfSeatsChanged(GameContext.Lobby?.PlayerList);
		Color resolvedColor = MatchPlayerColorAssignment.Get(Player);

		SetPlayerColor(resolvedColor);
	}
}
