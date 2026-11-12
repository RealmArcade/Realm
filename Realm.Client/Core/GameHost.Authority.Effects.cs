using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.Client.Core;


public partial class GameHost
{
	private bool TryPresentUnit(int serverUnitId, [NotNullWhen(true)] out Unit3D? unit3D)
	{
		unit3D = null;
		if (serverUnitId <= 0) return false;
		if (!TryGetLocalEntity(serverUnitId, out Entity entity)) return false;
		if (GameContext.World == null || !GameContext.World.IsAlive(entity)) return false;
		if (!TryGetUnit3D(entity, out Unit3D? found) || !GodotObject.IsInstanceValid(found)) return false;

		unit3D = found;
		return true;
	}

	private void PresentFloatingText(PresentationMessage message, System.Numerics.Vector3 position, System.Numerics.Vector3 color)
	{
		if (string.IsNullOrEmpty(message.Text)) return;

		CreateFloatingTextInternal(
			message.Text,
			new Vector3(position.X, position.Y, position.Z),
			new Color(color.X, color.Y, color.Z),
			message.Duration > 0f ? message.Duration : 1.5f);
	}

	private void PresentTargetIndicator(System.Numerics.Vector3 position, System.Numerics.Vector3 color)
	{
		SpawnTargetIndicator(new Vector3(position.X, position.Y, position.Z), new Color(color.X, color.Y, color.Z));
	}

	private void PresentBarrageVolley(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;

		string effectTypeId = message.Key;
		var center = new Vector3(message.Position.X, message.Position.Y, message.Position.Z);
		int impacts = Math.Clamp(message.Count, 1, 64);
		float spacing = MathF.Max(0.01f, message.Duration);
		float spread = MathF.Max(0f, message.Radius);

		Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(this)) return;

			for (int index = 0; index < impacts; index++)
			{
				SceneTreeTimer timer = GetTree().CreateTimer(index * spacing);
				timer.Timeout += () =>
				{
					if (!GodotObject.IsInstanceValid(this)) return;

					float angle = GD.Randf() * MathF.Tau;
					float distance = GD.Randf() * spread;
					var impact = center + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
					SpawnVisualEffectViewLogic(effectTypeId, new System.Numerics.Vector3(impact.X, impact.Y, impact.Z), 1f);
				};
			}
		}).CallDeferred();
	}

	private void PresentShockwave(PresentationMessage message, System.Numerics.Vector3 position, ShockwaveType type)
	{
		var center = new Vector3(position.X, position.Y, position.Z);
		float maxRadius = message.Radius > 0f ? message.Radius : 8f;
		float speed = message.Value > 0f ? message.Value : 12f;
		float duration = message.Duration > 0f ? message.Duration : 1f;
		var waveColor = new Color(message.Color.X, message.Color.Y, message.Color.Z);

		Callable.From(() => GroundShockwave3D.Create(this, center, type, maxRadius, speed, duration, waveColor)).CallDeferred();
	}

	private void PresentTerrainLine(PresentationMessage message)
	{
		var start = new System.Numerics.Vector2(message.Position.X, message.Position.Z);
		var end = new System.Numerics.Vector2(message.Secondary.X, message.Secondary.Z);
		var lineColor = new Color(message.Color.X, message.Color.Y, message.Color.Z);
		float duration = message.Duration > 0f ? message.Duration : 1f;
		float width = message.Value > 0f ? message.Value : 0.2f;

		Callable.From(() => TerrainLine3D.Create(this, start, end, lineColor, duration, width)).CallDeferred();
	}

	private void PresentPersistentAura(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;
		if (!TryPresentUnit(message.Handle, out Unit3D? unit3D)) return;

		string auraVfxId = message.Key;
		float scale = message.Value > 0f ? message.Value : 1f;

		Callable.From(() => unit3D.SetSocketAttachment(HumanoidBone.Chest, auraVfxId, null, null, scale, null, null, false)).CallDeferred();
	}

	private void PresentProjectile(PresentationMessage message)
	{
		var start = new Vector3(message.Position.X, message.Position.Y, message.Position.Z);
		var target = new Vector3(message.Secondary.X, message.Secondary.Y, message.Secondary.Z);

		// Script-spawned projectiles target a fixed point the server already resolved, so the visual must fly all the way there;
		// the weapon's MaxLifetime/FailsafeRange would otherwise cut it short on long casts.
		Callable.From(() => GameContext.FXService.SpawnWeaponProjectile(this, start, target, message.Key, default, message.PlayerIndex, true)).CallDeferred();
	}

	private void PresentStaticText(PresentationMessage message)
	{
		bool destroyed = (PresentationKind)message.Kind == PresentationKind.StaticTextDestroyed;

		ApplyStaticTextDelta(new StaticTextDelta
		{
			Handle = message.Handle,
			Text = message.Text,
			PositionX = message.Position.X,
			PositionY = message.Position.Y,
			PositionZ = message.Position.Z,
			ColorX = message.Color.X,
			ColorY = message.Color.Y,
			ColorZ = message.Color.Z,
			FontSize = message.Count > 0 ? message.Count : 48,
			Visible = message.Flag ? (byte)1 : (byte)0,
			Destroyed = destroyed ? (byte)1 : (byte)0
		});
	}

	private void PresentUnitColor(PresentationMessage message)
	{
		if (!TryPresentUnit(message.Handle, out Unit3D? unit3D)) return;

		var tint = new Color(message.Color.X, message.Color.Y, message.Color.Z);
		Callable.From(() => unit3D.ApplyModelTint(tint)).CallDeferred();
	}

	private void PresentUnitAnimation(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;
		if (!TryPresentUnit(message.Handle, out Unit3D? unit3D)) return;

		string animationName = message.Key;
		Callable.From(() => unit3D.PlayAnimation(animationName)).CallDeferred();
	}

	private void PresentUnitHandAttachment(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;
		if (!TryPresentUnit(message.Handle, out Unit3D? unit3D)) return;

		HumanoidBone hand = ResolveHandBone(message.Key);
		string? attachmentId = string.IsNullOrWhiteSpace(message.Text) ? null : message.Text;
		Callable.From(() => unit3D.SetHandAttachment(hand, attachmentId)).CallDeferred();
	}

	private static HumanoidBone ResolveHandBone(string hand)
	{
		if (hand.Equals("LeftHand", StringComparison.OrdinalIgnoreCase)
		    || hand.Equals("left", StringComparison.OrdinalIgnoreCase)
		    || hand.Equals("hand_l", StringComparison.OrdinalIgnoreCase))
		{
			return HumanoidBone.LeftHand;
		}

		return HumanoidBone.RightHand;
	}

	private void PresentUnitImpulse(PresentationMessage message)
	{
		if (!TryPresentUnit(message.Handle, out Unit3D? unit3D)) return;

		float strength = message.Value > 0f ? message.Value : 1f;
		float duration = message.Duration > 0f ? message.Duration : 0.5f;
		float frequency = message.SecondCount > 0 ? message.SecondCount : 12f;

		Callable.From(() => unit3D.TriggerImpulse(strength, duration, frequency)).CallDeferred();
	}

	/// <summary>
	///     Drops the placed node the authority exhausted. The message names the point the node stood at because both
	///     processes load the board's props for themselves and therefore do not share an entity id for them.
	/// </summary>
	/// <param name="message">The authority's depletion message.</param>
	private void PresentResourceNodeDepleted(PresentationMessage message)
	{
		var point = message.Position.ToNumerics();
		Entity node = Entity.Null;
		string propId = string.Empty;
		float closestDistance = 2.5f;

		var query = QueryCache.AllPositionAndResourceNodeAndPropIdentityQuery;
		World.Query(in query, (Entity entity, ref Realm.Ecs.Components.Core.Position position, ref PropIdentity identity) =>
		{
			float distance = System.Numerics.Vector3.Distance(position.Value, point);
			if (distance >= closestDistance) return;

			closestDistance = distance;
			node = entity;
			propId = identity.PropId;
		});

		if (node == Entity.Null) return;
		RemoveResourceNodeFromBoard(node, propId);
	}

	private void RemoveResourceNodeFromBoard(Entity node, string propId)
	{
		if (GameContext.World == null || !World.IsAlive(node)) return;

		World.SetOrAdd(node, new Dead());

		if (SelectedResourceNodeEntity == node)
		{
			ClearSelection();
		}

		if (EntityToProp3D.TryGetValue(node, out Prop3D? prop) && prop != null)
		{
			EntityToProp3D.Remove(node);
			AllProps.Remove(prop);
			if (SelectedProp == prop) SelectedProp = null;
			prop.QueueFree();
		}

		GameContext.PropMultiMesh?.MarkDirty(propId);
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	private void PresentResourceImpulse(PresentationMessage message)
	{
		if (message.Handle <= 0) return;
		if (!TryGetResourceProp((ResourceNodeId)message.Handle, out Prop3D? prop) || prop == null) return;

		float strength = message.Value > 0f ? message.Value : 1f;
		float duration = message.Duration > 0f ? message.Duration : 0.5f;
		float frequency = message.SecondCount > 0 ? message.SecondCount : 12f;

		Callable.From(() => prop.TriggerImpulse(strength, duration, frequency)).CallDeferred();
	}

	private void PresentAbilityDetail(PresentationMessage message, Action apply)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;

		apply();
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	private static void PresentItemTooltip(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;

		var registry = GameContext.RegistryService;
		if (registry == null) return;

		var itemId = new StringName(message.Key);
		if (registry.ItemRegistry.TryGetValue(itemId, out ItemMetadata? item)) item.Description = message.Text;
	}

	private void PresentCastSound(PresentationMessage message, System.Numerics.Vector3 position)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;

		var place = new Vector3(position.X, position.Y, position.Z);
		string soundPath = message.Key;
		GameContext.AudioService?.PlaySound3D(soundPath, place);
	}

}
