using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client;

public class MapStateSnapshot
{
	public int Width;
	public int Depth;
	public TerrainCell[,] Cells;

	public float[,] Heights
	{
		get
		{
			if (Cells == null) return null;
			int w = Cells.GetLength(0);
			int d = Cells.GetLength(1);
			float[,] res = new float[w + 1, d + 1];
			for (int z = 0; z <= d; z++)
			for (int x = 0; x <= w; x++)
			{
				res[x, z] = RuntimeTerrain.GetGridNodeHeight(x, z, Cells, w, d);
			}

			return res;
		}
		set
		{
			if (value == null) return;
			int w = value.GetLength(0) - 1;
			int d = value.GetLength(1) - 1;
			Cells = TerrainState.CalculateCells(w, d, value, Cells);
		}
	}

	public int[,] PathingCodes;
	public TerrainSplatWeights[,] SplatMap;
	public TerrainSplatWeights[,] CliffSplatMap;

	public float CameraBoundsLeft;
	public float CameraBoundsRight;
	public float CameraBoundsTop;
	public float CameraBoundsBottom;

	public List<SavedUnit> Units = new List<SavedUnit>();
	public List<SavedProp> Props = new List<SavedProp>();
	public List<SavedDecal> Decals = new List<SavedDecal>();
	public List<VfxSaveData> Vfx = new List<VfxSaveData>();
	public List<CoordinateSaveData> Coordinates = new List<CoordinateSaveData>();

	/// <summary>
	///     Puts the world under the host back to what this snapshot describes: the terrain it was taken from, the units,
	///     props, decals and effects that were standing on it, and the coordinates a map had named. Restoring is what
	///     lets a world be torn down for a playtest and come back afterwards without a file being written in between.
	/// </summary>
	public static void Restore(MapStateSnapshot snapshot, bool updateMapSize)
	{
		var host = GameContext.Host;
		if (host == null || host.GroundTerrain == null) return;

		ClearRestoredWorld(host);
		RestoreTerrain(host, snapshot);
		RestoreEntities(host, snapshot);
		RestoreCoordinates(host, snapshot);
		if (updateMapSize) UpdateMetadataMapSize(snapshot);
		RebuildRestoredWorldViews(host);
	}

	private static void ClearRestoredWorld(GameHost host)
	{
		var unitsCopy = new List<Unit3D>(host.AllUnits);
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
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
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
		foreach (var unit in snapshot.Units)
		{
			host.SpawnUnitExternal(unit.Id, unit.Position, unit.RotationY, unit.Scale, unit.Player);
		}

		foreach (var prop in snapshot.Props)
		{
			host.SpawnPropExternalWithParams(prop.Id, prop.Position, prop.RotationY, prop.Scale);
		}

		foreach (var decal in snapshot.Decals)
		{
			host.SpawnDecalExternalWithParams(decal.Id, decal.Position, decal.RotationY, decal.Scale);
		}

		if (snapshot.Vfx == null) return;
		foreach (var vfx in snapshot.Vfx)
		{
			host.SpawnVfxExternalWithParams(
				vfx.VfxId,
				new Vector3(vfx.PosX, vfx.PosY, vfx.PosZ),
				new Vector3(vfx.RotationX, vfx.RotationY, vfx.RotationZ),
				new Vector3(vfx.ScaleX <= 0f ? 1f : vfx.ScaleX, vfx.ScaleY <= 0f ? 1f : vfx.ScaleY, vfx.ScaleZ <= 0f ? 1f : vfx.ScaleZ),
				vfx.NormalOffset,
				vfx.Config
			);
		}
	}

	private static void RestoreCoordinates(GameHost host, MapStateSnapshot snapshot)
	{
		if (snapshot.Coordinates == null) return;

		GameContext.EditorService.EditorCoordinates.Clear();
		GameContext.EditorService.EditorCoordinates.AddRange(snapshot.Coordinates.Select(coordinate => new EditorCoordinate { Name = coordinate.Name, MinX = coordinate.MinX, MinZ = coordinate.MinZ, MaxX = coordinate.MaxX, MaxZ = coordinate.MaxZ }));
		host.RebuildAllCoordinatePersistentMeshes();
		GameContext.MapEditorHud?.RefreshCoordinateListExternal();
	}

	private static void UpdateMetadataMapSize(MapStateSnapshot snapshot)
	{
		string workspacePath = MapWorkspaceService.GetActiveWorkspacePath();
		string metadataPath = MetadataService.ResolveMetadataPath(workspacePath);
		if (!File.Exists(metadataPath)) return;

		try
		{
			GameContext.MetadataService.UpdateMetadata(workspacePath, metadata =>
			{
				metadata.MapProperties.MapWidth = snapshot.Width;
				metadata.MapProperties.MapHeight = snapshot.Depth;
			});
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Failed to update metadata.json during snapshot restore: {exception.Message}");
		}
	}

	private static void RebuildRestoredWorldViews(GameHost host)
	{
		host.GroundTerrain.UpdateMeshAndPhysics(true, true);
		host.GroundTerrain.ReloadTerrainTextures();
		GameContext.EditorService.SetTerrainSplatMap(host.GroundTerrain.SplatMap, host.GroundTerrain.CliffSplatMap);
		host.RebuildCameraBoundsOverlay();
		GameContext.PropMultiMesh?.RebuildAll();
		GameContext.MapEditorHud?.UpdateCameraBoundsUI();
		GameContext.MapEditorHud?.RegenerateMinimap();
	}

	public static MapStateSnapshot CreateSnapshot()
	{
		var host = GameContext.Host;
		if (host == null || host.GroundTerrain == null) return null;

		var snapshot = new MapStateSnapshot();
		snapshot.Width = host.GroundTerrain.Width;
		snapshot.Depth = host.GroundTerrain.Depth;
		snapshot.Cells = (TerrainCell[,])host.GroundTerrain.Cells.Clone();
		snapshot.PathingCodes = (int[,])host.GroundTerrain.PathingCodes.Clone();
		snapshot.SplatMap = (TerrainSplatWeights[,])host.GroundTerrain.SplatMap.Clone();
		snapshot.CliffSplatMap = host.GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])host.GroundTerrain.CliffSplatMap.Clone() : null;

		snapshot.CameraBoundsLeft = host.EditorCameraBoundsLeft;
		snapshot.CameraBoundsRight = host.EditorCameraBoundsRight;
		snapshot.CameraBoundsTop = host.EditorCameraBoundsTop;
		snapshot.CameraBoundsBottom = host.EditorCameraBoundsBottom;

		SaveUnitsToSnapshot(host, snapshot);
		SavePropsToSnapshot(host, snapshot);
		SaveDecalsToSnapshot(host, snapshot);
		SaveVfxToSnapshot(host, snapshot);

		snapshot.Coordinates = GameContext.EditorService.EditorCoordinates.Select(r => new CoordinateSaveData
		{
			Name = r.Name,
			MinX = r.MinX,
			MinZ = r.MinZ,
			MaxX = r.MaxX,
			MaxZ = r.MaxZ
		}).ToList();

		return snapshot;
	}

	private static void SaveUnitsToSnapshot(GameHost host, MapStateSnapshot snapshot)
	{
		foreach (var unit in host.AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			snapshot.Units.Add(new SavedUnit
			{
				Id = unit.UnitId,
				Position = unit.Position,
				RotationY = unit.RotationDegrees.Y,
				Scale = unit.Scale.X,
				Player = unit.Player
			});
		}
	}

	private static void SavePropsToSnapshot(GameHost host, MapStateSnapshot snapshot)
	{
		foreach (var prop in host.AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop)) continue;
			snapshot.Props.Add(new SavedProp
			{
				Id = prop.PropId,
				Position = prop.Position,
				RotationY = prop.RotationDegrees.Y,
				Scale = prop.Scale.X
			});
		}
	}

	private static void SaveDecalsToSnapshot(GameHost host, MapStateSnapshot snapshot)
	{
		foreach (var child in host.GetChildren())
		{
			if (!(child is Decal decal) || !GodotObject.IsInstanceValid(decal)) continue;
			string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
			snapshot.Decals.Add(new SavedDecal
			{
				Id = decalId,
				Position = decal.Position,
				RotationY = decal.RotationDegrees.Y,
				Scale = decal.Scale.X
			});
		}
	}

	private static void SaveVfxToSnapshot(GameHost host, MapStateSnapshot snapshot)
	{
		if (host.AllVfx == null) return;
		foreach (var vfx in host.AllVfx)
		{
			if (vfx == null || !GodotObject.IsInstanceValid(vfx)) continue;
			snapshot.Vfx.Add(new VfxSaveData
			{
				VfxId = vfx.Config?.VfxId ?? "vfx",
				PosX = vfx.Position.X,
				PosY = vfx.Position.Y,
				PosZ = vfx.Position.Z,
				RotationX = vfx.RotationDegrees.X,
				RotationY = vfx.RotationDegrees.Y,
				RotationZ = vfx.RotationDegrees.Z,
				ScaleX = vfx.Scale.X,
				ScaleY = vfx.Scale.Y,
				ScaleZ = vfx.Scale.Z,
				NormalOffset = vfx.Config?.SurfaceNormalOffset ?? 0f,
				Config = vfx.Config?.Clone()
			});
		}
	}
}