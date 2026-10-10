using Godot;
using Realm.Ecs.Components.Terrain;
using System.Collections.Generic;
using System.Linq;
using Realm.Client.Core;

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
				res[x, z] = Realm.Client.RuntimeTerrain.GetGridNodeHeight(x, z, Cells, w, d);
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

	public static MapStateSnapshot CreateSnapshot()
	{
		var host = Realm.Client.Core.GameHost.Instance;
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
				IsEnemy = unit.IsEnemy
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