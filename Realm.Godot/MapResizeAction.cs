using Godot;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Ecs.Components.Terrain;
using Realm.Godot.Services;
using Realm.Godot.VFX;

public class SavedUnit
{
	public string Id;
	public Vector3 Position;
	public float RotationY;
	public float Scale;
	public bool IsEnemy;
}

public class SavedProp
{
	public string Id;
	public Vector3 Position;
	public float RotationY;
	public float Scale;
}

public class SavedDecal
{
	public string Id;
	public Vector3 Position;
	public float RotationY;
	public float Scale;
}

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
					int cellX = System.Math.Clamp(x, 0, w - 1);
					int cellZ = System.Math.Clamp(z, 0, d - 1);
					res[x, z] = Cells[cellX, cellZ].Y_NW;
				}
			return res;
		}
		set
		{
			if (value == null) return;
			int w = value.GetLength(0) - 1;
			int d = value.GetLength(1) - 1;
			Cells = TerrainState.CalculateCells(w, d, value);
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

	public static MapStateSnapshot CreateSnapshot()
	{
		var host = GameHost.Instance;
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

		foreach (var unit in host.AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				snapshot.Units.Add(new SavedUnit
				{
					Id = unit.UnitId,
					Position = unit.Position,
					RotationY = unit.RotationDegrees.Y,
					Scale = unit.Scale.X,
					IsEnemy = unit.IsEnemy
				});
			}
		}

		foreach (var prop in host.AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				snapshot.Props.Add(new SavedProp
				{
					Id = prop.PropId,
					Position = prop.Position,
					RotationY = prop.RotationDegrees.Y,
					Scale = prop.Scale.X
				});
			}
		}

		foreach (var child in host.GetChildren())
		{
			if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
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

		if (host.AllVfx != null)
		{
			foreach (var vfx in host.AllVfx)
			{
				if (vfx != null && GodotObject.IsInstanceValid(vfx))
				{
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

		snapshot.Coordinates = host.EditorCoordinates.Select(r => new CoordinateSaveData
		{
			Name = r.Name,
			MinX = r.MinX,
			MinZ = r.MinZ,
			MaxX = r.MaxX,
			MaxZ = r.MaxZ
		}).ToList();

		return snapshot;
	}
}

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
		var host = GameHost.Instance;
		if (host == null || host.GroundTerrain == null) return;

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

		if (snapshot.Vfx != null)
		{
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

		if (snapshot.Coordinates != null)
		{
			host.EditorCoordinates.Clear();
			host.EditorCoordinates.AddRange(snapshot.Coordinates.Select(r => new GameHost.EditorCoordinate { Name = r.Name, MinX = r.MinX, MinZ = r.MinZ, MaxX = r.MaxX, MaxZ = r.MaxZ }));
			host.RebuildAllCoordinatePersistentMeshes();
			MapEditorHUD.Instance?.RefreshCoordinateListExternal();
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = MetadataService.ResolveMetadataPath(wsPath);
		if (System.IO.File.Exists(metaPath))
		{
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

		host.RebuildCameraBoundsOverlay();
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		MapEditorHUD.Instance?.RegenerateMinimap();
	}
}
