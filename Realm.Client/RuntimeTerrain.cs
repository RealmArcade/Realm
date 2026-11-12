using Realm.Shared.Maps;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotRecast.Detour;
using Godot;
using Godot.Collections;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Array = Godot.Collections.Array;

namespace Realm.Client;

[GlobalClass]
public partial class RuntimeTerrain : Node3D
{
	public const uint TerrainCollisionLayer = 2;
	public const uint TerrainVisualLayer = 1 | (1 << 2);
	public const uint TerrainDecalCullMask = 1 << 2;
	public static bool IsMinimapRendering { get; set; } = false;

	// Terrain data lives in the ECS world; asking for it before one exists is a caller bug. State changes go through
	// TerrainStateStore; this stays for the swatch-config writes that still edit the component in place.
	private static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("RuntimeTerrain used before a world existed.");

	public override void _ExitTree()
	{
		GameContext.UnregisterTerrain(this);
	}

	public RuntimeTerrain()
	{
		GameContext.RegisterTerrain(this);
	}

	protected virtual bool IsRuntimeOnly => true;
	public const int TargetTextureResolutionBits = 9;
	public const int TargetTextureResolution = 1 << TargetTextureResolutionBits;

	public static Image? ProcessAndResizeImage(Image? sourceImg, int targetSize)
	{
		if (sourceImg == null) return null;
		if (sourceImg.GetWidth() == targetSize && sourceImg.GetHeight() == targetSize)
		{
			return sourceImg;
		}

		Image resized = (Image)sourceImg.Duplicate();
		Image.Interpolation interpolationMode = sourceImg.GetWidth() > targetSize
			? Image.Interpolation.Lanczos
			: Image.Interpolation.Cubic;

		resized.Resize(targetSize, targetSize, interpolationMode);
		return resized;
	}

	public const float DefaultQuadSize = TerrainState.DefaultQuadSize;
	public const float DefaultCellSize = TerrainState.DefaultCellSize;

	public float CellSize
	{
		get => TerrainStateStore.Read().CellSize;
		protected set => TerrainStateStore.SetCellSize(value);
	}

	public DtNavMesh? NavMesh => TerrainStateStore.Read().NavMesh;
	public DtNavMeshQuery? NavMeshQuery => TerrainStateStore.Read().NavMeshQuery;

	public int Width
	{
		get => TerrainStateStore.Read().Width;
		protected set => TerrainStateStore.SetWidth(value);
	}

	public int Depth
	{
		get => TerrainStateStore.Read().Depth;
		protected set => TerrainStateStore.SetDepth(value);
	}

	public float QuadSize
	{
		get => TerrainStateStore.Read().QuadSize;
		protected set => TerrainStateStore.SetQuadSize(value);
	}

	protected TerrainCell[,] _localCells = null!;
	protected int[,] _localPathingCodes = null!;

	private void UpdateTerrainState(TerrainCell[,]? newCells, int[,]? newPathingCodes)
	{
		TerrainStateStore.ReplaceGrid(newCells, newPathingCodes);
	}

	public TerrainCell[,] Cells
	{
		get
		{
			if (GameContext.Host != null &&
			    GameContext.World is { } world &&
			    world.IsAlive(GameContext.Simulation.WorldEntity) &&
			    world.Has<TerrainState>(GameContext.Simulation.WorldEntity))
			{
				ref var state = ref world.Get<TerrainState>(GameContext.Simulation.WorldEntity);
				if (state.Cells == null)
				{
					state.Cells = new TerrainCell[state.Width, state.Depth];
				}

				return state.Cells;
			}

			if (_localCells == null)
			{
				_localCells = new TerrainCell[Width, Depth];
			}

			return _localCells;
		}
		set
		{
			_localCells = value;
				UpdateTerrainState(value, null);
		}
	}

	public sbyte[,]? MacroTiers
	{
		get
		{
			var c = Cells;
			if (c == null) return null;
			int w = Width;
			int d = Depth;
			var result = new sbyte[w, d];
			for (int z = 0; z < d; z++)
			for (int x = 0; x < w; x++)
				result[x, z] = c[x, z].MacroTier;
			return result;
		}
	}

	public float[,]? Heights
	{
		get => TerrainState.CalculateHeights(Width, Depth, Cells);
		set => SetHeights(value);
	}

	public void SetHeights(float[,]? heights)
	{
		var cells = TerrainState.CalculateCells(Width, Depth, heights);
		if (cells == null) return;
		Cells = cells;
	}

	public TerrainSplatWeights[,] SplatMap { get; set; } = null!;
	public TerrainSplatWeights[,]? CliffSplatMap { get; set; }

	public float BlendSoftness { get; set; } = 0.04f;
	public float BlendNoiseStrength { get; set; } = 0.22f;
	public float BlendNoiseScale { get; set; } = 0.22f;

	public int[,] PathingCodes
	{
		get
		{
			if (GameContext.Host != null &&
			    GameContext.World is { } world &&
			    world.IsAlive(GameContext.Simulation.WorldEntity) &&
			    world.Has<TerrainState>(GameContext.Simulation.WorldEntity))
			{
				ref var state = ref world.Get<TerrainState>(GameContext.Simulation.WorldEntity);
				if (state.PathingCodes == null)
				{
					state.PathingCodes = new int[state.Width, state.Depth];
					var cells = Cells;
					for (int z = 0; z < state.Depth; z++)
					{
						for (int x = 0; x < state.Width; x++)
						{
							state.PathingCodes[x, z] = GetDefaultPathingCode(cells[x, z]);
						}
					}
				}

				return state.PathingCodes;
			}

			if (_localPathingCodes == null)
			{
				_localPathingCodes = new int[Width, Depth];
				var cells = Cells;
				for (int z = 0; z < Depth; z++)
				{
					for (int x = 0; x < Width; x++)
					{
						_localPathingCodes[x, z] = GetDefaultPathingCode(cells[x, z]);
					}
				}
			}

			return _localPathingCodes;
		}
		set
		{
			_localPathingCodes = value;
				UpdateTerrainState(null, value);
		}
	}

}