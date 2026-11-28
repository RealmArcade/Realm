using System;
using Realm.Client.Core;
using Realm.Client.UI.MapEditor;
using Realm.Shared.Terrain;

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

		if (MapStateSnapshot.CreateSnapshot() is not { } before)
		{
			return ObjectSwapOutcome.Failed(ObjectSwapFailure.NoTerrain);
		}

		MapStateSnapshot mapped = MapObjectIds(before, sourceId, targetId, swap, out int changedObjects);
		if (changedObjects == 0)
		{
			return ObjectSwapOutcome.Failed(ObjectSwapFailure.NoInstances);
		}

		MapStateSnapshot.Restore(mapped, updateMapSize: false);

		if (MapStateSnapshot.CreateSnapshot() is { } after)
		{
			EditorHistoryManager.RecordAction(new ObjectTemplateSwapAction(before, after));
		}

		if (GameContext.Host != null)
		{
			GameContext.Host.EditorHasUnsavedChanges = true;
		}

		return ObjectSwapOutcome.Succeeded(changedObjects);
	}

	private static MapStateSnapshot MapObjectIds(MapStateSnapshot before, string sourceId, string targetId, bool swap, out int changedObjects)
	{
		var mapped = new MapStateSnapshot
		{
			Width = before.Width,
			Depth = before.Depth,
			Cells = before.Cells,
			PathingCodes = before.PathingCodes,
			SplatMap = before.SplatMap,
			CliffSplatMap = before.CliffSplatMap,
			CameraBoundsLeft = before.CameraBoundsLeft,
			CameraBoundsRight = before.CameraBoundsRight,
			CameraBoundsTop = before.CameraBoundsTop,
			CameraBoundsBottom = before.CameraBoundsBottom,
			Coordinates = before.Coordinates
		};

		changedObjects = 0;

		foreach (SavedUnit unit in before.Units)
		{
			mapped.Units.Add(new SavedUnit
			{
				Id = MapId(unit.Id, sourceId, targetId, swap, ref changedObjects),
				Position = unit.Position,
				RotationY = unit.RotationY,
				Scale = unit.Scale,
				Player = unit.Player,
				Level = unit.Level
			});
		}

		foreach (SavedProp prop in before.Props)
		{
			mapped.Props.Add(new SavedProp
			{
				Id = MapId(prop.Id, sourceId, targetId, swap, ref changedObjects),
				Position = prop.Position,
				RotationY = prop.RotationY,
				Scale = prop.Scale,
				Level = prop.Level
			});
		}

		foreach (SavedDecal decal in before.Decals)
		{
			mapped.Decals.Add(new SavedDecal
			{
				Id = MapId(decal.Id, sourceId, targetId, swap, ref changedObjects),
				Position = decal.Position,
				RotationY = decal.RotationY,
				Scale = decal.Scale
			});
		}

		foreach (SavedItem item in before.Items)
		{
			mapped.Items.Add(new SavedItem
			{
				Id = MapId(item.Id, sourceId, targetId, swap, ref changedObjects),
				Position = item.Position
			});
		}

		foreach (VfxSaveData vfx in before.Vfx)
		{
			mapped.Vfx.Add(new VfxSaveData
			{
				VfxId = MapId(vfx.VfxId, sourceId, targetId, swap, ref changedObjects),
				PosX = vfx.PosX,
				PosY = vfx.PosY,
				PosZ = vfx.PosZ,
				RotationX = vfx.RotationX,
				RotationY = vfx.RotationY,
				RotationZ = vfx.RotationZ,
				ScaleX = vfx.ScaleX,
				ScaleY = vfx.ScaleY,
				ScaleZ = vfx.ScaleZ,
				NormalOffset = vfx.NormalOffset,
				Config = vfx.Config?.Clone()
			});
		}

		return mapped;
	}

	private static string MapId(string objectId, string sourceId, string targetId, bool swap, ref int changedObjects)
	{
		if (string.Equals(objectId, sourceId, StringComparison.OrdinalIgnoreCase))
		{
			changedObjects++;
			return targetId;
		}

		if (swap && string.Equals(objectId, targetId, StringComparison.OrdinalIgnoreCase))
		{
			changedObjects++;
			return sourceId;
		}

		return objectId;
	}
}
