using Godot;
using Realm.Client.Services;
using Realm.Client.VFX;
using System.Collections.Generic;
using System.Linq;
using Realm.Client.Core;

namespace Realm.Client;

public class MapResizeAction : IEditorAction
{
	private readonly MapStateSnapshot _before;
	private readonly MapStateSnapshot _after;

	public MapResizeAction(MapStateSnapshot before, MapStateSnapshot after)
	{
		_before = before;
		_after = after;
	}

	public void Undo()
	{
		RestoreSnapshot(_before);
	}

	public void Redo()
	{
		RestoreSnapshot(_after);
	}

	private void RestoreSnapshot(MapStateSnapshot snapshot)
	{
		var host = Realm.Client.Core.GameHost.Instance;
		if (host == null || host.GroundTerrain == null) return;

		ClearCurrentState(host);
		RestoreTerrain(host, snapshot);
		RestoreEntities(host, snapshot);
		RestoreCoordinates(host, snapshot);
		UpdateMetadata(snapshot);
		RebuildUI(host);
	}

	private static void ClearCurrentState(GameHost host)
	{
		var unitsCopy = new List<Realm.Client.Unit3D>(host.AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				host.DeleteNodeExternal(unit);
			}
		}
		host.SelectedUnits.Clear();
		host.AllUnits.Clear();

		var children = host.GetChildren();
		foreach (var child in children)
		{
			if (child is Realm.Client.Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				host.DeleteNodeExternal(prop);
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				host.DeleteNodeExternal(decal);
			}
			else if (child is ProceduralVfxInstance3D vfx && GodotObject.IsInstanceValid(vfx))
			{
				host.DeleteNodeExternal(vfx);
			}
		}
	}

	private static void RestoreTerrain(GameHost host, MapStateSnapshot snapshot)
	{
		host.GroundTerrain.RestoreTerrainFromSnapshot(
			snapshot.Width,
			snapshot.Depth,
			host.GroundTerrain.QuadSize,
			snapshot.Cells,
			snapshot.PathingCodes,
			snapshot.SplatMap,
			snapshot.CliffSplatMap
		);

		host.EditorCameraBoundsLeft = snapshot.CameraBoundsLeft;
		host.EditorCameraBoundsRight = snapshot.CameraBoundsRight;
		host.EditorCameraBoundsTop = snapshot.CameraBoundsTop;
		host.EditorCameraBoundsBottom = snapshot.CameraBoundsBottom;
	}

	private static void RestoreEntities(GameHost host, MapStateSnapshot snapshot)
	{
		foreach (var u in snapshot.Units)
		{
			host.SpawnUnitExternal(u.Id, u.Position, u.IsEnemy, u.RotationY, u.Scale);
		}
		foreach (var p in snapshot.Props)
		{
			host.SpawnPropExternalWithParams(p.Id, p.Position, p.RotationY, p.Scale);
		}
		foreach (var d in snapshot.Decals)
		{
			host.SpawnDecalExternalWithParams(d.Id, d.Position, d.RotationY, d.Scale);
		}

		if (snapshot.Vfx == null) return;
		foreach (var v in snapshot.Vfx)
		{
			host.SpawnVfxExternalWithParams(
				v.VfxId,
				new Vector3(v.PosX, v.PosY, v.PosZ),
				new Vector3(v.RotationX, v.RotationY, v.RotationZ),
				new Vector3(v.ScaleX <= 0f ? 1f : v.ScaleX, v.ScaleY <= 0f ? 1f : v.ScaleY, v.ScaleZ <= 0f ? 1f : v.ScaleZ),
				v.NormalOffset,
				v.Config
			);
		}
	}

	private static void RestoreCoordinates(GameHost host, MapStateSnapshot snapshot)
	{
		if (snapshot.Coordinates == null) return;
		
		host.EditorCoordinates.Clear();
		host.EditorCoordinates.AddRange(snapshot.Coordinates.Select(r => new Realm.Client.Core.GameHost.EditorCoordinate { Name = r.Name, MinX = r.MinX, MinZ = r.MinZ, MaxX = r.MaxX, MaxZ = r.MaxZ }));
		host.RebuildAllCoordinatePersistentMeshes();
		UI.MapEditorHUD.Instance?.RefreshCoordinateListExternal();
	}

	private static void UpdateMetadata(MapStateSnapshot snapshot)
	{
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!System.IO.File.Exists(metaPath)) return;
		
		try
		{
			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				meta.MapProperties.MapWidth = snapshot.Width;
				meta.MapProperties.MapHeight = snapshot.Depth;
			});
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Failed to update metadata.json during snapshot restore: {ex.Message}");
		}
	}

	private static void RebuildUI(GameHost host)
	{
		host.RebuildCameraBoundsOverlay();
		Realm.Client.PropMultiMeshManager.Instance?.RebuildAll();
		UI.MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		UI.MapEditorHUD.Instance?.RegenerateMinimap();
	}
}