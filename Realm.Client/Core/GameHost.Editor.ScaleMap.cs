using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Client.Core;
public partial class GameHost
{
	private Node3D? _editorCoverageOverlayRoot { get; set; }

	public bool EditorCoverageOverlayEnabled { get; set; } = false;

	private void ClearAllUnits()
	{
		SelectedUnits.Clear();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.QueueFree();
			}
		}

		AllUnits.Clear();
		SetCurrentPopulation(GameContext.PerspectiveFilter.ControlledPlayerIndex, 0);
		SetMaxPopulation(GameContext.PerspectiveFilter.ControlledPlayerIndex, 0);

		foreach (var child in GetChildren())
		{
			if (child is Unit3D unit)
			{
				unit.QueueFree();
			}
			else if (child is Prop3D prop)
			{
				prop.QueueFree();
			}
			else if (child is Decal decal)
			{
				decal.QueueFree();
			}
			else if (child is ProceduralVfxInstance3D vfx)
			{
				vfx.QueueFree();
			}
		}

		AllProps.Clear();
		AllDecals.Clear();
		AllVfx.Clear();
		EntityToUnit3D.Clear();
		EntityIdToUnit3D.Clear();
		EntityToProp3D.Clear();
	}

	private MeshInstance3D _measureMeshInstance { get; set; } = null!;
	private ImmediateMesh _measureImmediateMesh { get; set; } = null!;

	private MeshInstance3D _symmetryPivotMarkerMesh { get; set; } = null!;

	public MeshInstance3D? GridOverlayMesh => null;
	public MeshInstance3D? PathingOverlayMesh => null;

	private Node3D? FindObjectNearPosition(Vector3 position, float searchRadius = 1.5f)
	{
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d && GodotObject.IsInstanceValid(n3d))
			{
				if (n3d is Unit3D || n3d is Prop3D || n3d is Decal)
				{
					float dist = new Vector2(n3d.GlobalPosition.X - position.X, n3d.GlobalPosition.Z - position.Z).Length();
					if (dist <= searchRadius)
					{
						return n3d;
					}
				}
			}
		}

		return null;
	}

	public void ScaleMapExternal(int newWidth, int newDepth)
	{
		if (GroundTerrain == null) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		if (MapStateSnapshot.CreateSnapshot() is not { } before) return;

		int oldWidth = GroundTerrain.Width;
		int oldDepth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;

		float oldHalfW = oldWidth / 2.0f * quadSize;
		float oldHalfD = oldDepth / 2.0f * quadSize;
		float newHalfW = newWidth / 2.0f * quadSize;
		float newHalfD = newDepth / 2.0f * quadSize;
		float scaleX = oldHalfW > 0f ? newHalfW / oldHalfW : 1f;
		float scaleZ = oldHalfD > 0f ? newHalfD / oldHalfD : 1f;

		GroundTerrain.ScaleTerrainData(newWidth, newDepth);

		ScaleEntityPositions(AllUnits, scaleX, scaleZ);
		ScaleEntityPositions(AllProps, scaleX, scaleZ);
		ScaleDecals(scaleX, scaleZ);
		ScaleVfx(scaleX, scaleZ);
		ScaleLootItems(scaleX, scaleZ);

		ScalePropEntitiesOnly(scaleX, scaleZ);

		ScaleCoordinateBounds(scaleX, scaleZ);

		MapCameraBounds oldMapBounds = MapCameraBounds.FromTerrain(oldWidth, oldDepth, quadSize);
		MapCameraBounds newMapBounds = MapCameraBounds.FromTerrain(newWidth, newDepth, quadSize);
		MapCameraBounds scaledCameraBounds = new MapCameraBounds(EditorCameraBoundsLeft, EditorCameraBoundsRight, EditorCameraBoundsTop, EditorCameraBoundsBottom).MapTo(oldMapBounds, newMapBounds);
		GameContext.EditorService.SetCameraBounds(GameContext.Simulation.WorldEntity, scaledCameraBounds);

		DeleteEntitiesOutsideBounds();
		GameContext.PropMultiMesh?.RebuildAll();

		GameContext.EditorService.Surface.AttachSplatMaps(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);

		UpdateScaleMetadata(newWidth, newDepth, scaledCameraBounds);
		RefreshUIForMapScale(newWidth, newDepth);

		if (MapStateSnapshot.CreateSnapshot() is { } after)
		{
			EditorHistoryManager.RecordAction(new MapResizeAction(before, after));
		}
	}

	private void ScaleEntityPositions<T>(IEnumerable<T> entities, float scaleX, float scaleZ) where T : Node3D
	{
		foreach (var entity in entities)
		{
			if (entity == null || !GodotObject.IsInstanceValid(entity)) continue;

			var pos = entity.Position;
			entity.Position = new Vector3(pos.X * scaleX, pos.Y, pos.Z * scaleZ);

			UpdateEntityEcsPosition(entity);
		}
	}

	private void UpdateEntityEcsPosition(Node3D entity)
	{
		if (entity is Unit3D u)
		{
			GameContext.EditorService.UpdateEntityEcsPosition(u.Entity, new System.Numerics.Vector3(u.Position.X, u.Position.Y, u.Position.Z));
		}
		else if (entity is Prop3D p)
		{
			GameContext.EditorService.UpdateEntityEcsPosition(p.Entity, new System.Numerics.Vector3(p.Position.X, p.Position.Y, p.Position.Z));
		}
	}

	private void ScalePropEntitiesOnly(float scaleX, float scaleZ)
	{
		GameContext.EditorService.ScalePropEntitiesOnly(scaleX, scaleZ, ent => EntityToProp3D.ContainsKey(ent));
	}

	private void ScaleDecals(float scaleX, float scaleZ)
	{
		foreach (var decal in AllDecals)
		{
			if (GodotObject.IsInstanceValid(decal))
			{
				decal.Position = new Vector3(decal.Position.X * scaleX, decal.Position.Y, decal.Position.Z * scaleZ);
				if (decal is Decal3D decal3D)
				{
					GameContext.EditorService.UpdateEntityEcsPosition(decal3D.Entity, new System.Numerics.Vector3(decal.Position.X, decal.Position.Y, decal.Position.Z));
				}
			}
		}
	}

	private void ScaleVfx(float scaleX, float scaleZ)
	{
		if (AllVfx != null)
		{
			foreach (var vfx in AllVfx)
			{
				if (vfx != null && GodotObject.IsInstanceValid(vfx))
				{
					vfx.Position = new Vector3(vfx.Position.X * scaleX, vfx.Position.Y, vfx.Position.Z * scaleZ);
				}
			}
		}
	}

	private void ScaleLootItems(float scaleX, float scaleZ)
	{
		foreach (var lootItem in AllLootItems)
		{
			if (lootItem == null || !GodotObject.IsInstanceValid(lootItem)) continue;

			lootItem.Position = new Vector3(lootItem.Position.X * scaleX, lootItem.Position.Y, lootItem.Position.Z * scaleZ);
		}
	}

	private void ScaleCoordinateBounds(float scaleX, float scaleZ)
	{
		GameContext.EditorService.ScaleCoordinateBounds(scaleX, scaleZ);
	}

	private void UpdateScaleMetadata(int newWidth, int newDepth, MapCameraBounds cameraBounds)
	{
		GameContext.EditorService.UpdateScaleMetadata(newWidth, newDepth, cameraBounds);
	}

	private void RefreshUIForMapScale(int newWidth, int newDepth)
	{
		RebuildAllCoordinatePersistentMeshes();
		GameContext.MapEditorHud?.RefreshCoordinateListExternal();
		RebuildCameraBoundsOverlay();
		GameContext.MapEditorHud?.UpdateCameraBoundsUI();
		GameContext.MapEditorHud?.RegenerateMinimap();

		EditorHasUnsavedChanges = true;
		GameContext.MapEditorHud?.ShowFeedbackExternal($"Map scaled to {newWidth}x{newDepth}");
	}

	private void DeleteEntitiesOutsideBounds()
	{
		if (GroundTerrain == null) return;

		float halfW = GroundTerrain.Width / 2.0f * GroundTerrain.QuadSize;
		float halfD = GroundTerrain.Depth / 2.0f * GroundTerrain.QuadSize;

		DeleteNodesOutsideBounds(AllUnits, halfW, halfD);
		DeleteNodesOutsideBounds(AllProps, halfW, halfD);
		DeleteNodesOutsideBounds(AllDecals, halfW, halfD);
		DeleteNodesOutsideBounds(AllLootItems, halfW, halfD);
		if (AllVfx != null)
		{
			DeleteNodesOutsideBounds(AllVfx, halfW, halfD);
		}
	}

	private void DeleteNodesOutsideBounds<T>(IEnumerable<T> nodes, float halfW, float halfD) where T : Node3D
	{
		var toDelete = new List<T>();
		foreach (var node in nodes)
		{
			if (node == null || !GodotObject.IsInstanceValid(node)) continue;
			var pos = node.Position;
			if (pos.X < -halfW || pos.X > halfW || pos.Z < -halfD || pos.Z > halfD)
			{
				toDelete.Add(node);
			}
		}

		foreach (var node in toDelete)
		{
			DeleteNodeExternal(node);
		}
	}


	private MeshInstance3D? _scaleMapSilhouetteMesh { get; set; }

	private List<MeshInstance3D> _symmetryHighlightMeshes { get; set; } = new();


	private bool _wasSelectionHighlightVisible { get; set; }
	private List<bool> _wasSymmetryHighlightsVisible { get; set; } = new();
	private bool _wasCameraBoundsVisible { get; set; }
	private bool _wasMeasureMeshVisible { get; set; }
	private bool _wasSymmetryPivotVisible { get; set; }
	private bool _wasCoordinatePreviewVisible { get; set; }
	private bool _wasCoordinateOutlineVisible { get; set; }
	private bool _wasScaleSilhouetteVisible { get; set; }
	private bool _wasCoverageOverlayVisible { get; set; }
}
