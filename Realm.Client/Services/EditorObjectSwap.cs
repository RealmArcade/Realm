using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.Utils;
using Realm.Client.VFX;

namespace Realm.Client.Services;

public enum ObjectSwapFailure
{
	None,
	MissingSelection,
	SameObject,
	NoTerrain,
	NoInstances
}

public readonly record struct ObjectSwapOutcome(bool Success, ObjectSwapFailure Failure, int ChangedObjects)
{
	public static ObjectSwapOutcome Failed(ObjectSwapFailure failure) => new(false, failure, 0);

	public static ObjectSwapOutcome Succeeded(int changedObjects) => new(true, ObjectSwapFailure.None, changedObjects);
}

public static class EditorObjectSwap
{
	private enum ObjectCategory
	{
		Unit,
		Prop,
		Decal,
		Item,
		Vfx
	}

	private readonly struct SwapRequest
	{
		public readonly Node Node;
		public readonly ObjectCategory SourceCategory;
		public readonly string OriginalId;
		public readonly string ReplacementId;
		public readonly Vector3 Position;
		public readonly Vector3 RotationDegrees;
		public readonly Vector3 Scale;
		public readonly int Player;
		public readonly int Level;

		public SwapRequest(Node node, ObjectCategory sourceCategory, string originalId, string replacementId, Vector3 position, Vector3 rotationDegrees, Vector3 scale, int player, int level)
		{
			Node = node;
			SourceCategory = sourceCategory;
			OriginalId = originalId;
			ReplacementId = replacementId;
			Position = position;
			RotationDegrees = rotationDegrees;
			Scale = scale;
			Player = player;
			Level = level;
		}
	}

	public static ObjectSwapOutcome Apply(string sourceId, string targetId, bool swap)
	{
		if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
		{
			return ObjectSwapOutcome.Failed(ObjectSwapFailure.MissingSelection);
		}

		if (string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase))
		{
			return ObjectSwapOutcome.Failed(ObjectSwapFailure.SameObject);
		}

		var host = GameContext.Host;
		if (host?.GroundTerrain == null)
		{
			return ObjectSwapOutcome.Failed(ObjectSwapFailure.NoTerrain);
		}

		if (MapStateSnapshot.CreateSnapshot() is not { } before)
		{
			return ObjectSwapOutcome.Failed(ObjectSwapFailure.NoTerrain);
		}

		var requests = new List<SwapRequest>();
		CollectRequests(host, sourceId, targetId, swap, requests);

		if (requests.Count == 0)
		{
			return ObjectSwapOutcome.Failed(ObjectSwapFailure.NoInstances);
		}

		int changedObjects = ApplyRequests(host, requests);
		if (changedObjects == 0)
		{
			return ObjectSwapOutcome.Failed(ObjectSwapFailure.NoInstances);
		}

		host.EditorHasUnsavedChanges = true;
		GameContext.PropMultiMesh?.RebuildAll();

		if (MapStateSnapshot.CreateSnapshot() is { } after)
		{
			EditorHistoryManager.RecordAction(new ObjectTemplateSwapAction(before, after));
		}

		return ObjectSwapOutcome.Succeeded(changedObjects);
	}

	public static bool MatchesId(string candidateId, string referenceId)
	{
		if (string.IsNullOrWhiteSpace(candidateId) || string.IsNullOrWhiteSpace(referenceId)) return false;
		if (string.Equals(candidateId, referenceId, StringComparison.OrdinalIgnoreCase)) return true;

		string candidateType = TemplateIDHelper.ParseTemplateID(candidateId).ObjectType;
		string referenceType = TemplateIDHelper.ParseTemplateID(referenceId).ObjectType;
		if (candidateType.Length > 0 && referenceType.Length > 0) return false;

		string candidateSlug = TemplateIDHelper.ParseTemplateID(candidateId).Slug;
		string referenceSlug = TemplateIDHelper.ParseTemplateID(referenceId).Slug;

		return string.Equals(candidateSlug, referenceSlug, StringComparison.OrdinalIgnoreCase);
	}

	private static void CollectRequests(GameHost host, string sourceId, string targetId, bool swap, List<SwapRequest> requests)
	{
		Unit3D[] units = host.AllUnits.ToArray();
		foreach (Unit3D unit in units)
		{
			if (!GodotObject.IsInstanceValid(unit) || !TryMapId(unit.UnitId, sourceId, targetId, swap, out string replacementId)) continue;

			int level = SaveLoadService.ReadStatedPlacementLevel(unit.Entity, unit.UnitId);
			requests.Add(new SwapRequest(unit, ObjectCategory.Unit, unit.UnitId, replacementId, unit.Position, unit.RotationDegrees, unit.Scale, unit.Player, level));
		}

		Prop3D[] props = host.AllProps.ToArray();
		foreach (Prop3D prop in props)
		{
			if (!GodotObject.IsInstanceValid(prop) || !TryMapId(prop.PropId, sourceId, targetId, swap, out string replacementId)) continue;

			int level = SaveLoadService.ReadStatedPlacementLevel(prop.Entity, prop.PropId);
			requests.Add(new SwapRequest(prop, ObjectCategory.Prop, prop.PropId, replacementId, prop.Position, prop.RotationDegrees, prop.Scale, 0, level));
		}

		Decal[] decals = host.AllDecals.ToArray();
		foreach (Decal decal in decals)
		{
			string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
			if (!GodotObject.IsInstanceValid(decal) || !TryMapId(decalId, sourceId, targetId, swap, out string replacementId)) continue;

			requests.Add(new SwapRequest(decal, ObjectCategory.Decal, decalId, replacementId, decal.Position, decal.RotationDegrees, decal.Scale, 0, 0));
		}

		LootItem3D[] items = host.AllLootItems.ToArray();
		foreach (LootItem3D item in items)
		{
			if (item == null || !GodotObject.IsInstanceValid(item) || !TryMapId(item.ItemTypeId, sourceId, targetId, swap, out string replacementId)) continue;

			requests.Add(new SwapRequest(item, ObjectCategory.Item, item.ItemTypeId, replacementId, item.Position, Vector3.Zero, Vector3.One, 0, 0));
		}

		ProceduralVfxInstance3D[] vfxInstances = host.AllVfx.ToArray();
		foreach (ProceduralVfxInstance3D vfx in vfxInstances)
		{
			string vfxId = vfx?.Config?.VfxId ?? string.Empty;
			if (vfx == null || !GodotObject.IsInstanceValid(vfx) || !TryMapId(vfxId, sourceId, targetId, swap, out string replacementId)) continue;

			requests.Add(new SwapRequest(vfx, ObjectCategory.Vfx, vfxId, replacementId, vfx.Position, vfx.RotationDegrees, vfx.Scale, 0, 0));
		}
	}

	private static int ApplyRequests(GameHost host, List<SwapRequest> requests)
	{
		int changedObjects = 0;

		foreach (SwapRequest request in requests)
		{
			if (!GodotObject.IsInstanceValid(request.Node)) continue;

			host.DeleteNodeExternal(request.Node);

			ObjectCategory replacementCategory = ResolveCategory(request.ReplacementId, request.SourceCategory);
			if (SpawnReplacement(host, replacementCategory, request.ReplacementId, request))
			{
				changedObjects++;
				continue;
			}

			SpawnReplacement(host, request.SourceCategory, request.OriginalId, request);
		}

		return changedObjects;
	}

	private static bool SpawnReplacement(GameHost host, ObjectCategory category, string templateId, in SwapRequest request)
	{
		Vector3 scale = request.Scale;
		if (scale.X <= 0.001f)
		{
			scale = Vector3.One;
		}

		switch (category)
		{
			case ObjectCategory.Unit:
				return host.SpawnUnitExternal(templateId, request.Position, request.RotationDegrees.Y, scale.X, request.Player, request.Level) != null;
			case ObjectCategory.Prop:
				return host.SpawnPropExternalWithParams(templateId, request.Position, request.RotationDegrees.Y, scale.X, request.Level) != null;
			case ObjectCategory.Decal:
				return host.SpawnDecalExternalWithParams(templateId, request.Position, request.RotationDegrees, scale.X) != null;
			case ObjectCategory.Item:
				return host.SpawnItemExternal(templateId, request.Position) != null;
			case ObjectCategory.Vfx:
				return host.SpawnVfxExternalWithParams(templateId, request.Position, request.RotationDegrees, scale) != null;
			default:
				return false;
		}
	}

	private static ObjectCategory ResolveCategory(string templateId, ObjectCategory fallback)
	{
		string objectType = TemplateIDHelper.ParseTemplateID(templateId).ObjectType.ToLowerInvariant();

		switch (objectType)
		{
			case "unit":
			case "units":
			case "building":
			case "buildings":
				return ObjectCategory.Unit;
			case "prop":
			case "props":
			case "resource":
			case "resources":
				return ObjectCategory.Prop;
			case "decal":
			case "decals":
				return ObjectCategory.Decal;
			case "item":
			case "items":
			case "consumable":
			case "consumables":
				return ObjectCategory.Item;
			case "vfx":
				return ObjectCategory.Vfx;
			default:
				return fallback;
		}
	}

	private static bool TryMapId(string objectId, string sourceId, string targetId, bool swap, out string replacementId)
	{
		if (MatchesId(objectId, sourceId))
		{
			replacementId = targetId;
			return true;
		}

		if (swap && MatchesId(objectId, targetId))
		{
			replacementId = sourceId;
			return true;
		}

		replacementId = objectId;
		return false;
	}
}
