using Arch.Core;
using Realm.Ecs.Components.Terrain;
using DotRecast.Detour;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Realm.Godot.Services;
using Realm.Godot.Utils;

public partial class RuntimeTerrain : StaticBody3D
{
	public const uint TerrainCollisionLayer = 2;
	public const uint TerrainVisualLayer = 1 | (1 << 2);
	public const uint TerrainDecalCullMask = 1 << 2;
	public static RuntimeTerrain Instance { get; protected set; }
	public static bool IsMinimapRendering { get; set; } = false;

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	public RuntimeTerrain()
	{
		Instance = this;
		CollisionLayer = 1U | TerrainCollisionLayer;
	}

	protected virtual bool IsRuntimeOnly => true;
	public const int TargetTextureResolutionBits = 9;
	public const int TargetTextureResolution = 1 << TargetTextureResolutionBits;

	public static Image ProcessAndResizeImage(Image sourceImg, int targetSize)
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

	internal TerrainState GetTerrainStateSafe()
	{
		if (GameHost.Instance != null && 
			GameHost.Instance.EcsWorld != null && 
			GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
			GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
		{
			return GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
		}
		return new TerrainState(128, 128, DefaultQuadSize, DefaultCellSize, (TerrainCell[,])null, null, null, null);
	}

	public float CellSize
	{
		get => GetTerrainStateSafe().CellSize;
		protected set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, state.Depth, state.QuadSize, value,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public DtNavMesh NavMesh => GetTerrainStateSafe().NavMesh;
	public DtNavMeshQuery NavMeshQuery => GetTerrainStateSafe().NavMeshQuery;

	public int Width
	{
		get => GetTerrainStateSafe().Width;
		protected set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				value, state.Depth, state.QuadSize, state.CellSize,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public int Depth
	{
		get => GetTerrainStateSafe().Depth;
		protected set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, value, state.QuadSize, state.CellSize,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public float QuadSize
	{
		get => GetTerrainStateSafe().QuadSize;
		protected set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, state.Depth, value, state.CellSize,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public const float TIER_HEIGHT = TerrainCell.TIER_HEIGHT;
	public static float WATER_DELTA => (GameHost.Instance != null && GameHost.Instance.EditorBlockLevelHeight > 0.001f ? GameHost.Instance.EditorBlockLevelHeight : TIER_HEIGHT) * 0.30f;

	public const int PATHING_SHALLOW_WATER = (int)TerrainPathingFlags.ShallowWater;
	public const int PATHING_DEEP_WATER = (int)TerrainPathingFlags.DeepWater;
	public const int PATHING_FLYING = (int)TerrainPathingFlags.Flying;
	public const int PATHING_GROUND = (int)TerrainPathingFlags.Ground;
	public const int PATHING_BUILDABLE = (int)TerrainPathingFlags.Buildable;

	public static int GetDefaultPathingCode(WaterType waterMode)
	{
		switch (waterMode)
		{
			case WaterType.Shallow:
				return PATHING_SHALLOW_WATER | PATHING_FLYING;
			case WaterType.Deep:
				return PATHING_DEEP_WATER | PATHING_FLYING;
			case WaterType.None:
			default:
				return PATHING_GROUND | PATHING_BUILDABLE | PATHING_FLYING;
		}
	}

	public static int GetDefaultPathingCode(TerrainCell cell)
	{
		if (cell.WaterMode != WaterType.None && Instance != null)
		{
			var prof = Instance.GetWaterProfile(cell.WaterProfileIndex);
			if (prof != null) return prof.DefaultPathingCode;
		}
		return GetDefaultPathingCode(cell.WaterMode);
	}

	protected TerrainCell[,] _localCells;
	protected int[,] _localPathingCodes;

	public TerrainCell[,] Cells
	{
		get
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
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
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
				int targetWidth = value != null ? value.GetLength(0) : state.Width;
				int targetDepth = value != null ? value.GetLength(1) : state.Depth;
				GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
					targetWidth, targetDepth, state.QuadSize, state.CellSize,
					value, state.PathingCodes, state.NavMesh, state.NavMeshQuery
				));
			}
		}
	}

	public sbyte[,] MacroTiers
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

	public float[,] Heights
	{
		get => TerrainState.CalculateHeights(Width, Depth, Cells);
		set => SetHeights(value);
	}

	public void SetHeights(float[,] heights)
	{
		if (heights == null) return;
		Cells = TerrainState.CalculateCells(Width, Depth, heights);
	}

	public TerrainSplatWeights[,] SplatMap { get; set; }
	public TerrainSplatWeights[,] CliffSplatMap { get; set; }

	public float CliffJitterStrength { get; set; } = 1.0f;
	public float CliffJitterScale { get; set; } = 0.20f;
	public float CliffRimNoiseStrength { get; set; } = 0.30f;
	public float BlendSoftness { get; set; } = 0.04f;
	public float BlendNoiseStrength { get; set; } = 0.22f;
	public float BlendNoiseScale { get; set; } = 0.22f;

	public int[,] PathingCodes
	{
		get
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
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
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
				int targetWidth = value != null ? value.GetLength(0) : state.Width;
				int targetDepth = value != null ? value.GetLength(1) : state.Depth;
				GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
					targetWidth, targetDepth, state.QuadSize, state.CellSize,
					state.Cells, value, state.NavMesh, state.NavMeshQuery
				));
			}
		}
	}

	public const int CHUNK_SIZE = 16;
	protected int _chunkedWidth = 0;
	protected int _chunkedDepth = 0;

	public class TerrainChunk
	{
		public MeshInstance3D MeshInstance;
		public ArrayMesh ArrayMesh;
		public CollisionShape3D CollisionShape;
		public Dictionary<byte, (MeshInstance3D MeshInstance, ArrayMesh ArrayMesh)> ProfileWaterMeshes = new();
		public int StartX;
		public int StartZ;
		public int EndX;
		public int EndZ;
		public Vector3[] VerticesCache;
		public Color[] ColorsCache;
		public float[] TexIndicesCache;
		public float[] TexWeightsCache01;
		public float[] CliffIndicesCache;
		public float[] CliffWeightsCache;
		public Vector3[] NormalsCache;
		public Vector2[] UvsCache;
		public int[] IndicesCache;
		public float[] MapDataCache;
		public Dictionary<TerrainVertexKey, int> WeldVertexMap;
		public int[] WeldRemapTable;
		public float MinY;
		public float MaxY;
		public Aabb WorldAabb;
	}
	
	protected List<TerrainChunk> _chunks = new List<TerrainChunk>();
	protected ShaderMaterial _material;
	public ShaderMaterial Material => _material;
	protected ShaderMaterial _shallowWaterMaterial;
	protected ShaderMaterial _deepWaterMaterial;
	protected Dictionary<byte, WaterProfileSaveData> _waterProfiles = new();
	protected Dictionary<byte, ShaderMaterial> _waterMaterials = new();
	protected Shader _waterUberShader;

	public Dictionary<byte, WaterProfileSaveData> WaterProfiles => _waterProfiles;
	public Dictionary<byte, ShaderMaterial> WaterMaterials => _waterMaterials;

	public IReadOnlyDictionary<byte, WaterProfileSaveData> GetWaterProfiles()
	{
		if (_waterProfiles.Count == 0)
		{
			ReloadWaterProfiles();
		}
		return _waterProfiles;
	}

	public WaterProfileSaveData GetWaterProfile(byte profileIndex)
	{
		if (_waterProfiles.Count == 0)
		{
			ReloadWaterProfiles();
		}
		if (_waterProfiles.TryGetValue(profileIndex, out var prof)) return prof;
		var defaults = WaterProfileSaveData.CreateDefaultProfiles();
		var def = defaults.Find(d => d.ProfileIndex == profileIndex);
		if (def != null)
		{
			_waterProfiles[profileIndex] = def;
			return def;
		}
		return _waterProfiles.Values.FirstOrDefault() ?? new WaterProfileSaveData();
	}

	public ShaderMaterial GetWaterMaterial(byte profileIndex)
	{
		if (_waterProfiles.Count == 0)
		{
			ReloadWaterProfiles();
		}
		if (_waterMaterials.TryGetValue(profileIndex, out var mat) && mat != null) return mat;
		if (_waterUberShader == null) CreateWater();
		var newMat = new ShaderMaterial { Shader = _waterUberShader };
		var prof = GetWaterProfile(profileIndex);
		ApplyWaterProfileToMaterial(newMat, prof);
		_waterMaterials[profileIndex] = newMat;
		return newMat;
	}

	public void SetWaterProfiles(List<WaterProfileSaveData> profiles)
	{
		_waterProfiles.Clear();
		foreach (var def in WaterProfileSaveData.CreateDefaultProfiles())
		{
			_waterProfiles[def.ProfileIndex] = def;
		}
		foreach (var p in profiles)
		{
			_waterProfiles[p.ProfileIndex] = p;
		}
		foreach (var kvp in _waterProfiles)
		{
			byte idx = kvp.Key;
			var prof = kvp.Value;
			if (!_waterMaterials.TryGetValue(idx, out var mat) || mat == null)
			{
				mat = new ShaderMaterial { Shader = _waterUberShader };
				_waterMaterials[idx] = mat;
			}
			ApplyWaterProfileToMaterial(mat, prof);
		}
		RegenerateWaterMesh();
	}

	public void ApplyWaterProfileToMaterial(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		if (mat == null || profile == null) return;
		Color shallow = Color.HtmlIsValid(profile.ShallowColorHex) ? Color.FromHtml(profile.ShallowColorHex) : new Color(0.05f, 0.30f, 0.38f, 0.55f);
		Color deep = Color.HtmlIsValid(profile.DeepColorHex) ? Color.FromHtml(profile.DeepColorHex) : new Color(0.01f, 0.06f, 0.14f, 0.98f);
		Color foam = Color.HtmlIsValid(profile.FoamColorHex) ? Color.FromHtml(profile.FoamColorHex) : new Color(0.85f, 0.95f, 1.0f, 0.85f);
		Color emission = Color.HtmlIsValid(profile.EmissionColorHex) ? Color.FromHtml(profile.EmissionColorHex) : Colors.Black;
		Color core = Color.HtmlIsValid(profile.CoreColorHex) ? Color.FromHtml(profile.CoreColorHex) : Colors.White;
		Color sss = Color.HtmlIsValid(profile.SubsurfaceColorHex) ? Color.FromHtml(profile.SubsurfaceColorHex) : Colors.Black;

		mat.SetShaderParameter("shallow_color", shallow);
		mat.SetShaderParameter("deep_color", deep);
		mat.SetShaderParameter("foam_color", foam);
		mat.SetShaderParameter("max_depth", profile.MaxDepth);
		mat.SetShaderParameter("foam_depth", profile.FoamDepth);
		mat.SetShaderParameter("wave_speed", profile.WaveSpeed);
		mat.SetShaderParameter("wave_strength", profile.WaveStrength);

		mat.SetShaderParameter("use_normal_texture", profile.UseNormalTexture && !string.IsNullOrEmpty(profile.NormalTexturePath));
		if (profile.UseNormalTexture && !string.IsNullOrEmpty(profile.NormalTexturePath))
		{
			var tex = LoadTextureFromActiveWorkspace(profile.NormalTexturePath);
			if (tex != null) mat.SetShaderParameter("normal_texture", tex);
		}
		mat.SetShaderParameter("normal_scale", profile.NormalScale);
		mat.SetShaderParameter("flow_direction", new Vector2(profile.FlowDirectionX, profile.FlowDirectionY));
		mat.SetShaderParameter("flow_speed", profile.FlowSpeed);

		mat.SetShaderParameter("use_flow_map", profile.UseFlowMap && !string.IsNullOrEmpty(profile.FlowMapPath));
		if (profile.UseFlowMap && !string.IsNullOrEmpty(profile.FlowMapPath))
		{
			var ftex = LoadTextureFromActiveWorkspace(profile.FlowMapPath);
			if (ftex != null) mat.SetShaderParameter("flow_map_texture", ftex);
		}

		mat.SetShaderParameter("refraction_strength", profile.RefractionStrength);
		mat.SetShaderParameter("caustic_strength", profile.CausticStrength);
		mat.SetShaderParameter("caustic_scale", profile.CausticScale);
		mat.SetShaderParameter("caustic_speed", profile.CausticSpeed);

		mat.SetShaderParameter("emission_color", emission);
		mat.SetShaderParameter("emission_boost", profile.EmissionBoost);
		mat.SetShaderParameter("core_color", core);
		mat.SetShaderParameter("core_threshold", profile.CoreThreshold);
		mat.SetShaderParameter("subsurface_color", sss);
		mat.SetShaderParameter("subsurface_strength", profile.SubsurfaceStrength);

		mat.SetShaderParameter("use_detail_texture", profile.UseDetailTexture && !string.IsNullOrEmpty(profile.DetailTexturePath));
		if (profile.UseDetailTexture && !string.IsNullOrEmpty(profile.DetailTexturePath))
		{
			var dtex = LoadTextureFromActiveWorkspace(profile.DetailTexturePath);
			if (dtex != null) mat.SetShaderParameter("detail_texture", dtex);
		}
		mat.SetShaderParameter("detail_tile_mode", string.Equals(profile.DetailTileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
		mat.SetShaderParameter("detail_uv_scale", new Vector2(profile.DetailUvScaleX, profile.DetailUvScaleY));
		mat.SetShaderParameter("detail_uv_scroll", new Vector2(profile.DetailUvScrollX, profile.DetailUvScrollY));
		mat.SetShaderParameter("detail_stochastic_tile_size", profile.DetailStochasticTileSize > 0.001f ? profile.DetailStochasticTileSize : 1.0f);
		mat.SetShaderParameter("detail_cross_fade", Math.Clamp(profile.DetailCrossFade * 0.01f, 0.0f, 0.10f));
		mat.SetShaderParameter("detail_alpha", profile.DetailAlpha);
		mat.SetShaderParameter("detail_blend_mode", profile.DetailBlendMode);

		if (_currentShroudTexture != null)
		{
			mat.SetShaderParameter("shroud_texture", _currentShroudTexture);
			mat.SetShaderParameter("shroud_enabled", GameHost.Instance == null || !GameHost.Instance.IsMapEditorMode);
			float halfW = (Width * QuadSize) * 0.5f;
			float halfD = (Depth * QuadSize) * 0.5f;
			mat.SetShaderParameter("shroud_world_min", new Vector2(-halfW, -halfD));
			mat.SetShaderParameter("shroud_world_size", new Vector2(Width * QuadSize, Depth * QuadSize));
		}
	}

	private Texture2D? LoadTextureFromActiveWorkspace(string texturePath)
	{
		if (string.IsNullOrWhiteSpace(texturePath)) return null;
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string fullPath = Path.Combine(wsPath, "Assets", "textures", texturePath);
		if (!File.Exists(fullPath)) fullPath = Path.Combine(wsPath, "Assets", "noise", texturePath);
		if (!File.Exists(fullPath)) fullPath = Path.Combine(wsPath, "Assets", "decals", texturePath);
		if (!File.Exists(fullPath)) fullPath = Path.Combine(wsPath, "Assets", "ribbons", texturePath);
		if (!File.Exists(fullPath)) fullPath = Path.Combine(wsPath, "Assets", "vfx", texturePath);
		if (!File.Exists(fullPath)) fullPath = Path.Combine(wsPath, texturePath);
		if (!File.Exists(fullPath)) fullPath = PathUtils.FindPath($"Assets/textures/{texturePath}");
		if (!File.Exists(fullPath)) fullPath = PathUtils.FindPath($"Assets/noise/{texturePath}");
		if (!File.Exists(fullPath)) fullPath = PathUtils.FindPath(texturePath);
		if (File.Exists(fullPath))
		{
			if (fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				var (alb, _) = LoadRtexLayers(fullPath);
				return alb != null ? ImageTexture.CreateFromImage(alb) : null;
			}
			var img = Image.LoadFromFile(fullPath);
			if (img != null) return ImageTexture.CreateFromImage(img);
		}
		return null;
	}

	public void ReloadWaterProfiles()
	{
		if (_waterUberShader == null)
		{
			_waterUberShader = GD.Load<Shader>("res://Assets/shaders/water_uber.gdshader");
			if (_waterUberShader == null)
			{
				string shaderPath = PathUtils.FindPath("Assets/shaders/water_uber.gdshader");
				if (File.Exists(shaderPath))
				{
					_waterUberShader = new Shader { Code = File.ReadAllText(shaderPath) };
				}
			}
		}

		_waterProfiles.Clear();
		foreach (var def in WaterProfileSaveData.CreateDefaultProfiles())
		{
			_waterProfiles[def.ProfileIndex] = def;
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!string.IsNullOrEmpty(wsPath) && MetadataService.Instance.TryLoadMetadata(wsPath, out var metadataRoot) && metadataRoot?.CustomWaterProfiles != null)
		{
			foreach (var p in metadataRoot.CustomWaterProfiles)
			{
				_waterProfiles[p.ProfileIndex] = p;
			}
		}

		foreach (var kvp in _waterProfiles)
		{
			byte idx = kvp.Key;
			var prof = kvp.Value;
			if (!_waterMaterials.TryGetValue(idx, out var mat) || mat == null)
			{
				mat = new ShaderMaterial { Shader = _waterUberShader };
				_waterMaterials[idx] = mat;
			}
			ApplyWaterProfileToMaterial(mat, prof);
		}

		_shallowWaterMaterial = GetWaterMaterial(0);
		_deepWaterMaterial = GetWaterMaterial(1);
	}

	protected void CreateWater()
	{
		ReloadWaterProfiles();
		UpdateWaterTransform();
	}

	private (float waterY, WaterType waterMode, byte waterProfileIndex) GetCellWaterInfo(int x, int z, TerrainCell[,] cells, int w, int d)
	{
		var cell = cells[x, z];
		if (cell.WaterMode != WaterType.None)
		{
			return ((cell.MacroTier * TerrainCell.TIER_HEIGHT) + WATER_DELTA, cell.WaterMode, cell.WaterProfileIndex);
		}

		float minCornerH = Math.Min(Math.Min(cell.Y_NW, cell.Y_NE), Math.Min(cell.Y_SE, cell.Y_SW));
		float maxWaterY = -9999f;
		WaterType bestWaterMode = WaterType.None;
		byte bestProfileIndex = 0;

		for (int nz = Math.Max(0, z - 1); nz <= Math.Min(d - 1, z + 1); nz++)
		{
			for (int nx = Math.Max(0, x - 1); nx <= Math.Min(w - 1, x + 1); nx++)
			{
				if (nx == x && nz == z) continue;
				var nCell = cells[nx, nz];
				if (nCell.WaterMode != WaterType.None)
				{
					float nWaterY = (nCell.MacroTier * TerrainCell.TIER_HEIGHT) + WATER_DELTA;
					if (nWaterY > maxWaterY)
					{
						maxWaterY = nWaterY;
						bestWaterMode = nCell.WaterMode;
						bestProfileIndex = nCell.WaterProfileIndex;
					}
				}
			}
		}

		if (bestWaterMode != WaterType.None && minCornerH <= maxWaterY)
		{
			return (maxWaterY, bestWaterMode, bestProfileIndex);
		}

		return (0f, WaterType.None, 0);
	}

	public void RegenerateWaterMesh()
	{
		CreateWater();

		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;
		float quadSize = QuadSize;
		float halfWQuadSize = (w / 2.0f) * quadSize;
		float halfDQuadSize = (d / 2.0f) * quadSize;

		var profileVerts = new Dictionary<byte, List<Vector3>>();
		var profileNorms = new Dictionary<byte, List<Vector3>>();
		var profileUvs = new Dictionary<byte, List<Vector2>>();
		var profileIndices = new Dictionary<byte, List<int>>();

		(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> indices) GetGeomLists(byte pIdx)
		{
			if (!profileVerts.TryGetValue(pIdx, out var vList))
			{
				vList = new List<Vector3>();
				profileVerts[pIdx] = vList;
				profileNorms[pIdx] = new List<Vector3>();
				profileUvs[pIdx] = new List<Vector2>();
				profileIndices[pIdx] = new List<int>();
			}
			return (vList, profileNorms[pIdx], profileUvs[pIdx], profileIndices[pIdx]);
		}

		foreach (var chunk in _chunks)
		{
			profileVerts.Clear();
			profileNorms.Clear();
			profileUvs.Clear();
			profileIndices.Clear();

			for (int z = chunk.StartZ; z < chunk.EndZ; z++)
			{
				for (int x = chunk.StartX; x < chunk.EndX; x++)
				{
					var (waterY1, activeWaterMode1, activeWaterProfile1) = GetCellWaterInfo(x, z, cells, w, d);
					if (activeWaterMode1 == WaterType.None) continue;

					Vector3 nw = new Vector3(x * quadSize - halfWQuadSize, waterY1, z * quadSize - halfDQuadSize);
					Vector3 ne = new Vector3((x + 1) * quadSize - halfWQuadSize, waterY1, z * quadSize - halfDQuadSize);
					Vector3 se = new Vector3((x + 1) * quadSize - halfWQuadSize, waterY1, (z + 1) * quadSize - halfDQuadSize);
					Vector3 sw = new Vector3(x * quadSize - halfWQuadSize, waterY1, (z + 1) * quadSize - halfDQuadSize);

					var geom = GetGeomLists(activeWaterProfile1);
					int baseIdx = geom.verts.Count;

					geom.verts.Add(nw);
					geom.verts.Add(ne);
					geom.verts.Add(se);
					geom.verts.Add(sw);

					Vector3 normal = Vector3.Up;
					geom.norms.Add(normal);
					geom.norms.Add(normal);
					geom.norms.Add(normal);
					geom.norms.Add(normal);

					geom.uvs.Add(new Vector2(0, 0));
					geom.uvs.Add(new Vector2(1, 0));
					geom.uvs.Add(new Vector2(1, 1));
					geom.uvs.Add(new Vector2(0, 1));

					geom.indices.Add(baseIdx + 0);
					geom.indices.Add(baseIdx + 1);
					geom.indices.Add(baseIdx + 2);

					geom.indices.Add(baseIdx + 0);
					geom.indices.Add(baseIdx + 2);
					geom.indices.Add(baseIdx + 3);

					if (x + 1 < w)
					{
						var (waterY2, activeWaterMode2, activeWaterProfile2) = GetCellWaterInfo(x + 1, z, cells, w, d);
						if (activeWaterMode2 != WaterType.None && MathF.Abs(waterY1 - waterY2) > 0.01f)
						{
							float yMin = MathF.Min(waterY1, waterY2);
							float yMax = MathF.Max(waterY1, waterY2);
							float wallX = (x + 1) * quadSize - halfWQuadSize;
							float z0 = z * quadSize - halfDQuadSize;
							float z1 = (z + 1) * quadSize - halfDQuadSize;

							byte wallProfile = (waterY1 >= waterY2) ? activeWaterProfile1 : activeWaterProfile2;
							var wGeom = GetGeomLists(wallProfile);

							int wBase = wGeom.verts.Count;
							wGeom.verts.Add(new Vector3(wallX, yMin, z0));
							wGeom.verts.Add(new Vector3(wallX, yMin, z1));
							wGeom.verts.Add(new Vector3(wallX, yMax, z1));
							wGeom.verts.Add(new Vector3(wallX, yMax, z0));

							Vector3 wallNorm = (waterY1 > waterY2) ? Vector3.Right : Vector3.Left;
							wGeom.norms.Add(wallNorm); wGeom.norms.Add(wallNorm); wGeom.norms.Add(wallNorm); wGeom.norms.Add(wallNorm);
							wGeom.uvs.Add(new Vector2(0, 0)); wGeom.uvs.Add(new Vector2(1, 0)); wGeom.uvs.Add(new Vector2(1, 1)); wGeom.uvs.Add(new Vector2(0, 1));

							wGeom.indices.Add(wBase + 0); wGeom.indices.Add(wBase + 1); wGeom.indices.Add(wBase + 2);
							wGeom.indices.Add(wBase + 0); wGeom.indices.Add(wBase + 2); wGeom.indices.Add(wBase + 3);
							wGeom.indices.Add(wBase + 0); wGeom.indices.Add(wBase + 2); wGeom.indices.Add(wBase + 1);
							wGeom.indices.Add(wBase + 0); wGeom.indices.Add(wBase + 3); wGeom.indices.Add(wBase + 2);
						}
					}

					if (z + 1 < d)
					{
						var (waterY2, activeWaterMode2, activeWaterProfile2) = GetCellWaterInfo(x, z + 1, cells, w, d);
						if (activeWaterMode2 != WaterType.None && MathF.Abs(waterY1 - waterY2) > 0.01f)
						{
							float yMin = MathF.Min(waterY1, waterY2);
							float yMax = MathF.Max(waterY1, waterY2);
							float wallZ = (z + 1) * quadSize - halfDQuadSize;
							float x0 = x * quadSize - halfWQuadSize;
							float x1 = (x + 1) * quadSize - halfWQuadSize;

							byte wallProfile = (waterY1 >= waterY2) ? activeWaterProfile1 : activeWaterProfile2;
							var wGeom = GetGeomLists(wallProfile);

							int wBase = wGeom.verts.Count;
							wGeom.verts.Add(new Vector3(x0, yMin, wallZ));
							wGeom.verts.Add(new Vector3(x1, yMin, wallZ));
							wGeom.verts.Add(new Vector3(x1, yMax, wallZ));
							wGeom.verts.Add(new Vector3(x0, yMax, wallZ));

							Vector3 wallNorm = (waterY1 > waterY2) ? Vector3.Back : Vector3.Forward;
							wGeom.norms.Add(wallNorm); wGeom.norms.Add(wallNorm); wGeom.norms.Add(wallNorm); wGeom.norms.Add(wallNorm);
							wGeom.uvs.Add(new Vector2(0, 0)); wGeom.uvs.Add(new Vector2(1, 0)); wGeom.uvs.Add(new Vector2(1, 1)); wGeom.uvs.Add(new Vector2(0, 1));

							wGeom.indices.Add(wBase + 0); wGeom.indices.Add(wBase + 1); wGeom.indices.Add(wBase + 2);
							wGeom.indices.Add(wBase + 0); wGeom.indices.Add(wBase + 2); wGeom.indices.Add(wBase + 3);
							wGeom.indices.Add(wBase + 0); wGeom.indices.Add(wBase + 2); wGeom.indices.Add(wBase + 1);
							wGeom.indices.Add(wBase + 0); wGeom.indices.Add(wBase + 3); wGeom.indices.Add(wBase + 2);
						}
					}
				}
			}

			foreach (var (pIdx, gVerts) in profileVerts)
			{
				if (!chunk.ProfileWaterMeshes.TryGetValue(pIdx, out var meshPair))
				{
					var arrMesh = new ArrayMesh();
					var meshInst = new MeshInstance3D();
					meshInst.Name = $"WaterProfileChunk_{pIdx}_{chunk.StartX}_{chunk.StartZ}";
					meshInst.Mesh = arrMesh;
					meshInst.Layers = TerrainVisualLayer;
					meshInst.MaterialOverride = GetWaterMaterial(pIdx);
					AddChild(meshInst);
					meshPair = (meshInst, arrMesh);
					chunk.ProfileWaterMeshes[pIdx] = meshPair;
				}
				else
				{
					meshPair.MeshInstance.MaterialOverride = GetWaterMaterial(pIdx);
				}

				UpdateSingleWaterMesh(meshPair.MeshInstance, meshPair.ArrayMesh, gVerts, profileNorms[pIdx], profileUvs[pIdx], profileIndices[pIdx]);
			}

			foreach (var kvp in chunk.ProfileWaterMeshes)
			{
				if (!profileVerts.ContainsKey(kvp.Key))
				{
					kvp.Value.ArrayMesh.ClearSurfaces();
				}
			}
		}
	}

	private void UpdateSingleWaterMesh(MeshInstance3D meshInstance, ArrayMesh arrayMesh, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> indices)
	{
		if (meshInstance == null || arrayMesh == null) return;

		while (arrayMesh.GetSurfaceCount() > 0)
		{
			arrayMesh.SurfaceFindByName("");
			arrayMesh.ClearSurfaces();
		}

		if (verts.Count == 0)
		{
			meshInstance.Visible = false;
			return;
		}

		var surfaceArray = new Godot.Collections.Array();
		surfaceArray.Resize((int)Mesh.ArrayType.Max);
		surfaceArray[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		surfaceArray[(int)Mesh.ArrayType.Normal] = norms.ToArray();
		surfaceArray[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		surfaceArray[(int)Mesh.ArrayType.Index] = indices.ToArray();

		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
		meshInstance.Visible = true;
	}

	public void UpdateWaterTransform()
	{
		float halfW = (Width * QuadSize) * 0.5f;
		float halfD = (Depth * QuadSize) * 0.5f;
		var shroudMin = new Vector2(-halfW, -halfD);
		var shroudSize = new Vector2(Width * QuadSize, Depth * QuadSize);

		if (_material != null)
		{
			_material.SetShaderParameter("shroud_world_min", shroudMin);
			_material.SetShaderParameter("shroud_world_size", shroudSize);
		}
		foreach (var wMat in _waterMaterials.Values)
		{
			if (wMat != null)
			{
				wMat.SetShaderParameter("shroud_world_min", shroudMin);
				wMat.SetShaderParameter("shroud_world_size", shroudSize);
			}
		}
	}

	public void UpdateWaterSize()
	{
		UpdateWaterTransform();
		RegenerateWaterMesh();
	}

	public override void _Ready()
	{
		if (_material == null)
		{
			var shader = new Shader();
			shader.Code = @"
shader_type spatial;
render_mode blend_mix;

uniform sampler2DArray terrain_textures : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2DArray terrain_normals_pbr : hint_default_white, filter_linear_mipmap_anisotropic;
uniform float blend_softness = 0.04;
uniform float blend_noise_strength = 0.22;
uniform float blend_noise_scale = 0.22;
uniform float cliff_jitter_strength = 1.0;
uniform float cliff_jitter_scale = 0.20;
uniform sampler2D shroud_texture : hint_default_black;
uniform vec2 shroud_world_min = vec2(-125.0, -125.0);
uniform vec2 shroud_world_size = vec2(250.0, 250.0);
uniform bool shroud_enabled = false;

uniform sampler2D pathing_texture : hint_default_transparent, filter_nearest;
uniform bool pathing_visible = false;

uniform bool grid_visible = false;
uniform vec4 grid_color_thick = vec4(1.0, 0.9, 0.0, 0.85);
uniform vec4 grid_color_thin = vec4(1.0, 0.9, 0.0, 0.25);
uniform float grid_spacing = 2.0;
uniform vec2 terrain_size = vec2(1.0, 1.0);

uniform float texture_scale = 0.5;
uniform float macro_scale = 0.035;

uniform float uv_warp_strength = 0.8;
uniform float macro_albedo_contrast = 0.10;
uniform float macro_roughness_contrast = 0.08;
uniform float macro_normal_strength = 0.15;
uniform float macro_lacunarity = 2.0;
uniform float macro_gain = 0.5;

uniform bool enable_macro_noise = true;
uniform bool enable_height_blend = true;
uniform bool enable_normal_mapping = true;
uniform float specular_amt = 0.2;

uniform vec4 swatch_params[256];
uniform vec4 swatch_height_params[256];
uniform vec4 swatch_albedo_params[256];

varying flat vec4 v_tex_indices;
varying vec4 v_tex_weights;
varying flat vec4 v_cliff_tex_indices;
varying vec4 v_cliff_tex_weights;
varying vec3 v_world_pos;
varying vec3 v_world_normal;
varying vec4 v_color;
varying flat float v_cliff_factor;

float macro_hash(vec2 p) {
	ivec2 ip = ivec2(floor(p));
	uvec2 q = uvec2(ip);
	q = q * uvec2(1597334677u, 3812015801u);
	uint n = (q.x ^ q.y) * 1597334677u;
	return float(n) * (1.0 / 4294967295.0);
}

float macro_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(macro_hash(i + vec2(0.0, 0.0)), macro_hash(i + vec2(1.0, 0.0)), u.x),
	           mix(macro_hash(i + vec2(0.0, 1.0)), macro_hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float macro_fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	mat2 rot = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8));
	for (int i = 0; i < 4; ++i) {
		v += a * macro_noise(p);
		p = rot * p * 2.0 + vec2(100.0);
		a *= 0.5;
	}
	return v;
}

vec4 sample_quad_array(sampler2DArray tex_array, float layer, vec2 uv) {
	return texture(tex_array, vec3(uv, layer));
}

vec2 stochastic_hash(vec2 p) {
	ivec2 ip = ivec2(floor(p));
	uvec2 q = uvec2(ip);
	q = q * uvec2(1597334677u, 3812015801u);
	uint n = (q.x ^ q.y) * 1597334677u;
	return vec2(float(n & 0xFFFFu), float((n >> 16u) & 0xFFFFu)) * (1.0 / 65535.0);
}

vec4 sample_semi_grid(sampler2DArray tex_array, float layer, vec2 uv, vec2 dx, vec2 dy, float blend_width) {
	if (blend_width <= 0.0001) {
		return textureGrad(tex_array, vec3(uv, layer), dx, dy);
	}
	vec2 f = fract(uv);
	vec2 edge = smoothstep(vec2(0.0), vec2(blend_width), f) * 
	            smoothstep(vec2(0.0), vec2(blend_width), vec2(1.0) - f);
	float center_weight = edge.x * edge.y;

	vec4 col_center = textureGrad(tex_array, vec3(uv, layer), dx, dy);

	if (center_weight > 0.999) {
		return col_center;
	}

	vec4 col_border = textureGrad(tex_array, vec3(uv + vec2(0.5, 0.5), layer), dx, dy);

	return mix(col_border, col_center, center_weight);
}

vec4 sample_stochastic_layer(sampler2DArray tex_array, float layer, vec2 uv, vec2 dx, vec2 dy, float tile_mode, float stoch_tile_size, float cross_fade, bool is_vector_data) {
	if (tile_mode < 0.5) {
		return sample_semi_grid(tex_array, layer, uv, dx, dy, cross_fade);
	}

	const float F2 = 0.36602540378;
	const float G2 = 0.2113248654;

	vec2 stoch_uv = uv / max(0.01, stoch_tile_size);
	vec2 skew_uv = stoch_uv + (stoch_uv.x + stoch_uv.y) * F2;
	vec2 i = floor(skew_uv);
	vec2 f = fract(skew_uv);

	vec2 i0, i1, i2;
	vec3 w;

	if (f.x > f.y) {
		i0 = i;
		i1 = i + vec2(1.0, 0.0);
		i2 = i + vec2(1.0, 1.0);
		w = vec3(1.0 - f.x, f.x - f.y, f.y);
	} else {
		i0 = i;
		i1 = i + vec2(0.0, 1.0);
		i2 = i + vec2(1.0, 1.0);
		w = vec3(1.0 - f.y, f.y - f.x, f.x);
	}

	vec2 off0 = stochastic_hash(i0);
	vec2 off1 = stochastic_hash(i1);
	vec2 off2 = stochastic_hash(i2);

	vec4 col0 = textureGrad(tex_array, vec3(uv + off0, layer), dx, dy);
	vec4 col1 = textureGrad(tex_array, vec3(uv + off1, layer), dx, dy);
	vec4 col2 = textureGrad(tex_array, vec3(uv + off2, layer), dx, dy);

	vec3 h_scores;
	if (is_vector_data) {
		h_scores = vec3(col0.b, col1.b, col2.b);
	} else {
		const vec3 lum_weights = vec3(0.299, 0.587, 0.114);
		h_scores = vec3(dot(col0.rgb, lum_weights), dot(col1.rgb, lum_weights), dot(col2.rgb, lum_weights));
	}

	vec3 safe_w = max(w, vec3(0.0));
	vec3 combined_weights = pow(safe_w, vec3(4.0)) * exp(h_scores * 2.0);
	float sum_cw = combined_weights.x + combined_weights.y + combined_weights.z;
	vec3 sw = sum_cw > 0.0001 ? (combined_weights / sum_cw) : w;

	if (is_vector_data) {
		return col0 * sw.x + col1 * sw.y + col2 * sw.z;
	}

	vec3 mean_color = col0.rgb * sw.x + col1.rgb * sw.y + col2.rgb * sw.z;
	return vec4(mean_color, col0.a * sw.x + col1.a * sw.y + col2.a * sw.z);
}

vec4 sample_planar_layer(sampler2DArray tex_array, float layer, vec2 uv_y, vec2 dx_y, vec2 dy_y, bool is_vector_data) {
	int layer_idx = int(clamp(round(layer), 0.0, 255.0));
	vec4 params = swatch_params[layer_idx];
	float tile_mode = params.x;
	float uv_scale = params.y > 0.001 ? params.y : 1.0;
	float stoch_tile_size = params.z > 0.001 ? params.z : 1.0;
	float cross_fade = clamp(params.w, 0.0, 0.10);

	vec2 scaled_uv_y = uv_y * uv_scale;
	vec2 scaled_dx_y = dx_y * uv_scale;
	vec2 scaled_dy_y = dy_y * uv_scale;

	return sample_stochastic_layer(tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
}

vec4 sample_triplanar_layer(sampler2DArray tex_array, float layer, vec2 uv_x, vec2 uv_y, vec2 uv_z, vec2 dx_x, vec2 dy_x, vec2 dx_y, vec2 dy_y, vec2 dx_z, vec2 dy_z, vec3 weights, bool is_vector_data) {
	int layer_idx = int(clamp(round(layer), 0.0, 255.0));
	vec4 params = swatch_params[layer_idx];
	float tile_mode = params.x;
	float uv_scale = params.y > 0.001 ? params.y : 1.0;
	float stoch_tile_size = params.z > 0.001 ? params.z : 1.0;
	float cross_fade = clamp(params.w, 0.0, 0.10);

	vec2 scaled_uv_x = uv_x * uv_scale;
	vec2 scaled_uv_y = uv_y * uv_scale;
	vec2 scaled_uv_z = uv_z * uv_scale;

	vec2 scaled_dx_x = dx_x * uv_scale;
	vec2 scaled_dy_x = dy_x * uv_scale;
	vec2 scaled_dx_y = dx_y * uv_scale;
	vec2 scaled_dy_y = dy_y * uv_scale;
	vec2 scaled_dx_z = dx_z * uv_scale;
	vec2 scaled_dy_z = dy_z * uv_scale;

	if (weights.y > 0.99) {
		return sample_stochastic_layer(tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	}
	if (weights.x > 0.99) {
		return sample_stochastic_layer(tex_array, layer, scaled_uv_x, scaled_dx_x, scaled_dy_x, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	}
	if (weights.z > 0.99) {
		return sample_stochastic_layer(tex_array, layer, scaled_uv_z, scaled_dx_z, scaled_dy_z, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	}

	vec4 col_x = sample_stochastic_layer(tex_array, layer, scaled_uv_x, scaled_dx_x, scaled_dy_x, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	vec4 col_y = sample_stochastic_layer(tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	vec4 col_z = sample_stochastic_layer(tex_array, layer, scaled_uv_z, scaled_dx_z, scaled_dy_z, tile_mode, stoch_tile_size, cross_fade, is_vector_data);

	vec3 tri_h;
	if (is_vector_data) {
		tri_h = vec3(col_x.b, col_y.b, col_z.b);
	} else {
		const vec3 lum_weights = vec3(0.299, 0.587, 0.114);
		tri_h = vec3(dot(col_x.rgb, lum_weights), dot(col_y.rgb, lum_weights), dot(col_z.rgb, lum_weights));
	}

	vec3 safe_w = max(weights, vec3(0.0));
	vec3 combined_tri_w = pow(safe_w, vec3(6.0)) * exp(tri_h * 2.0);
	float sum_tri_w = combined_tri_w.x + combined_tri_w.y + combined_tri_w.z;
	vec3 tw = sum_tri_w > 0.0001 ? (combined_tri_w / sum_tri_w) : weights;

	return col_x * tw.x + col_y * tw.y + col_z * tw.z;
}

vec2 unpack_normal_xy(vec2 raw_rg) {
	return raw_rg * 2.0 - 1.0;
}

vec3 unpack_triplanar_normal(vec4 norm_pbr, vec3 blend_w, vec3 geom_norm, float normal_scale) {
	vec2 t_xy = unpack_normal_xy(norm_pbr.rg) * normal_scale;

	vec3 sign_n = sign(geom_norm);
	sign_n.x = sign_n.x == 0.0 ? 1.0 : sign_n.x;
	sign_n.y = sign_n.y == 0.0 ? 1.0 : sign_n.y;
	sign_n.z = sign_n.z == 0.0 ? 1.0 : sign_n.z;

	vec3 dp_x = vec3(0.0, t_xy.y, t_xy.x * sign_n.x);
	vec3 dp_y = vec3(t_xy.x, 0.0, t_xy.y * sign_n.y);
	vec3 dp_z = vec3(t_xy.x * sign_n.z, t_xy.y, 0.0);

	vec3 total_dp = dp_x * blend_w.x + dp_y * blend_w.y + dp_z * blend_w.z;
	return normalize(geom_norm + total_dp);
}

struct TriplanarPbrData {
	vec3 normal;
	float ao;
	float roughness;
	float height;
};

TriplanarPbrData sample_triplanar_pbr(sampler2DArray norm_tex_array, float layer, vec2 uv_x, vec2 uv_y, vec2 uv_z, vec2 dx_x, vec2 dy_x, vec2 dx_y, vec2 dy_y, vec2 dx_z, vec2 dy_z, vec3 weights, vec3 geom_norm, float normal_scale, float roughness_scale) {
	int layer_idx = int(clamp(round(layer), 0.0, 255.0));
	vec4 params = swatch_params[layer_idx];
	float tile_mode = params.x;
	float uv_scale = params.y > 0.001 ? params.y : 1.0;
	float stoch_tile_size = params.z > 0.001 ? params.z : 1.0;
	float cross_fade = clamp(params.w, 0.0, 0.10);

	vec2 scaled_uv_x = uv_x * uv_scale;
	vec2 scaled_uv_y = uv_y * uv_scale;
	vec2 scaled_uv_z = uv_z * uv_scale;

	vec2 scaled_dx_x = dx_x * uv_scale;
	vec2 scaled_dy_x = dy_x * uv_scale;
	vec2 scaled_dx_y = dx_y * uv_scale;
	vec2 scaled_dy_y = dy_y * uv_scale;
	vec2 scaled_dx_z = dx_z * uv_scale;
	vec2 scaled_dy_z = dy_z * uv_scale;

	vec3 sign_n = sign(geom_norm);
	sign_n.x = sign_n.x == 0.0 ? 1.0 : sign_n.x;
	sign_n.y = sign_n.y == 0.0 ? 1.0 : sign_n.y;
	sign_n.z = sign_n.z == 0.0 ? 1.0 : sign_n.z;

	TriplanarPbrData result;

	if (weights.y > 0.99) {
		vec4 ny = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, true);
		vec2 ty = unpack_normal_xy(ny.rg) * normal_scale;
		float tzy = sqrt(max(0.0, 1.0 - dot(ty, ty)));
		vec3 dp_y = vec3(ty.x, 0.0, ty.y * sign_n.y);
		result.normal = normalize(geom_norm + dp_y);
		result.ao = clamp((0.35 + 0.65 * tzy) * (0.55 + 0.45 * ny.b), 0.05, 1.0);
		result.roughness = ny.a * roughness_scale;
		result.height = ny.b;
		return result;
	}
	if (weights.x > 0.99) {
		vec4 nx = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_x, scaled_dx_x, scaled_dy_x, tile_mode, stoch_tile_size, cross_fade, true);
		vec2 tx = unpack_normal_xy(nx.rg) * normal_scale;
		float tzx = sqrt(max(0.0, 1.0 - dot(tx, tx)));
		vec3 dp_x = vec3(0.0, tx.y, tx.x * sign_n.x);
		result.normal = normalize(geom_norm + dp_x);
		result.ao = clamp((0.35 + 0.65 * tzx) * (0.55 + 0.45 * nx.b), 0.05, 1.0);
		result.roughness = nx.a * roughness_scale;
		result.height = nx.b;
		return result;
	}
	if (weights.z > 0.99) {
		vec4 nz = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_z, scaled_dx_z, scaled_dy_z, tile_mode, stoch_tile_size, cross_fade, true);
		vec2 tz = unpack_normal_xy(nz.rg) * normal_scale;
		float tzz = sqrt(max(0.0, 1.0 - dot(tz, tz)));
		vec3 dp_z = vec3(tz.x * sign_n.z, tz.y, 0.0);
		result.normal = normalize(geom_norm + dp_z);
		result.ao = clamp((0.35 + 0.65 * tzz) * (0.55 + 0.45 * nz.b), 0.05, 1.0);
		result.roughness = nz.a * roughness_scale;
		result.height = nz.b;
		return result;
	}

	vec4 nx = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_x, scaled_dx_x, scaled_dy_x, tile_mode, stoch_tile_size, cross_fade, true);
	vec4 ny = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, true);
	vec4 nz = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_z, scaled_dx_z, scaled_dy_z, tile_mode, stoch_tile_size, cross_fade, true);

	vec2 tx = unpack_normal_xy(nx.rg) * normal_scale;
	vec2 ty = unpack_normal_xy(ny.rg) * normal_scale;
	vec2 tz = unpack_normal_xy(nz.rg) * normal_scale;

	float tzx = sqrt(max(0.0, 1.0 - dot(tx, tx)));
	float tzy = sqrt(max(0.0, 1.0 - dot(ty, ty)));
	float tzz = sqrt(max(0.0, 1.0 - dot(tz, tz)));

	vec3 tri_h = vec3(nx.b, ny.b, nz.b);
	vec3 safe_w = max(weights, vec3(0.0));
	vec3 combined_tri_w = pow(safe_w, vec3(6.0)) * exp(tri_h * 2.0);
	float sum_tri_w = combined_tri_w.x + combined_tri_w.y + combined_tri_w.z;
	vec3 tw = sum_tri_w > 0.0001 ? (combined_tri_w / sum_tri_w) : weights;

	vec3 dp_x = vec3(0.0, tx.y, tx.x * sign_n.x);
	vec3 dp_y = vec3(ty.x, 0.0, ty.y * sign_n.y);
	vec3 dp_z = vec3(tz.x * sign_n.z, tz.y, 0.0);
	vec3 total_dp = dp_x * tw.x + dp_y * tw.y + dp_z * tw.z;

	result.normal = normalize(geom_norm + total_dp);

	float ao_x = clamp((0.35 + 0.65 * tzx) * (0.55 + 0.45 * nx.b), 0.05, 1.0);
	float ao_y = clamp((0.35 + 0.65 * tzy) * (0.55 + 0.45 * ny.b), 0.05, 1.0);
	float ao_z = clamp((0.35 + 0.65 * tzz) * (0.55 + 0.45 * nz.b), 0.05, 1.0);
	result.ao = ao_x * tw.x + ao_y * tw.y + ao_z * tw.z;

	result.roughness = (nx.a * tw.x + ny.a * tw.y + nz.a * tw.z) * roughness_scale;
	result.height = nx.b * tw.x + ny.b * tw.y + nz.b * tw.z;
	return result;
}

void vertex() {
	v_tex_indices = CUSTOM0;
	v_tex_weights = CUSTOM1;
	v_cliff_tex_indices = CUSTOM2;
	v_cliff_tex_weights = CUSTOM3;
	v_color = vec4(1.0, 1.0, 1.0, 1.0);
	v_cliff_factor = COLOR.r;

	vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float cliff_mask = clamp(COLOR.a, 0.0, 1.0);

	if (cliff_jitter_strength > 0.0 && cliff_mask > 0.001) {
		vec2 jitter_uv_x = (world_pos.xz + vec2(world_pos.y * 0.75, world_pos.y * 1.25)) * cliff_jitter_scale;
		vec2 jitter_uv_z = (world_pos.xz + vec2(world_pos.y * 1.35 + 43.1, world_pos.y * 0.65 + 19.7)) * cliff_jitter_scale;
		float noise_x = (macro_fbm(jitter_uv_x) - 0.5) * 2.0;
		float noise_z = (macro_fbm(jitter_uv_z) - 0.5) * 2.0;
		vec3 world_disp = vec3(noise_x, 0.0, noise_z) * (cliff_jitter_strength * cliff_mask);
		world_pos += world_disp;
		VERTEX = (inverse(MODEL_MATRIX) * vec4(world_pos, 1.0)).xyz;
	}

	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_world_pos = world_pos;
}

void fragment() {
	vec3 geom_normal = normalize(v_world_normal);

	vec3 pos_warped = v_world_pos;
	float base_fbm_val = 0.5;

	if (enable_macro_noise) {
		base_fbm_val = macro_fbm(v_world_pos.xz * macro_scale);
		if (uv_warp_strength > 0.0) {
			float warp_x = (base_fbm_val - 0.5) * 2.0;
			float warp_y = (macro_fbm((v_world_pos.xz + vec2(17.3, 31.7)) * macro_scale) - 0.5) * 2.0;
			vec2 warp_offset = vec2(warp_x, warp_y);
			pos_warped += vec3(warp_offset.x, warp_offset.y, warp_offset.y) * uv_warp_strength * 0.05 / max(0.001, macro_scale);
		}
	}

	float slope_cliff_factor = v_cliff_factor > 0.5 ? 1.0 : 0.0;

	vec3 terrain_color = vec3(0.0);
	vec3 blended_world_normal = geom_normal;
	float blended_ao = 1.0;
	float blended_roughness = 0.85;

	if (slope_cliff_factor < 0.5) {
		vec2 uv_y = pos_warped.xz * texture_scale;
		vec2 dx_y = dFdx(uv_y);
		vec2 dy_y = dFdy(uv_y);

		vec4 raw_weights = v_tex_weights;
		raw_weights.x = raw_weights.x < 0.001 ? 0.0 : raw_weights.x;
		raw_weights.y = raw_weights.y < 0.001 ? 0.0 : raw_weights.y;
		raw_weights.z = raw_weights.z < 0.001 ? 0.0 : raw_weights.z;
		raw_weights.w = raw_weights.w < 0.001 ? 0.0 : raw_weights.w;

		float weight_sum = raw_weights.x + raw_weights.y + raw_weights.z + raw_weights.w;
		vec4 norm_weights = weight_sum > 0.0001 ? raw_weights / weight_sum : vec4(1.0, 0.0, 0.0, 0.0);

		float max_incoming_weight = max(max(norm_weights.x, norm_weights.y), max(norm_weights.z, norm_weights.w));
		float blend_transition_factor = clamp((1.0 - max_incoming_weight) * 2.5, 0.0, 1.0);

		if (blend_noise_strength > 0.0 && blend_transition_factor > 0.001) {
			vec2 blend_noise_uv = v_world_pos.xz * blend_noise_scale;
			float noise_ch_a = (macro_noise(blend_noise_uv) * 0.70 + macro_noise(blend_noise_uv * 2.13 + vec2(11.7, 7.3)) * 0.30) - 0.5;
			float noise_ch_b = (macro_noise(blend_noise_uv + vec2(19.3, 37.7)) * 0.70 + macro_noise(blend_noise_uv * 2.13 + vec2(43.3, 19.7)) * 0.30) - 0.5;
			vec4 weight_noise_offset = vec4(noise_ch_a, -noise_ch_a, noise_ch_b, -noise_ch_b) * (blend_noise_strength * blend_transition_factor);

			vec4 active_channel_mask = step(vec4(0.001), norm_weights);
			vec4 perturbed_weights = max(vec4(0.0), norm_weights + weight_noise_offset * active_channel_mask);
			float perturbed_weight_sum = perturbed_weights.x + perturbed_weights.y + perturbed_weights.z + perturbed_weights.w;
			norm_weights = perturbed_weight_sum > 0.0001 ? perturbed_weights / perturbed_weight_sum : norm_weights;
		}

		vec4 gn0 = raw_weights.x > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.x), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 0.0, 1.0);
		vec4 gn1 = raw_weights.y > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.y), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 0.0, 1.0);
		vec4 gn2 = raw_weights.z > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.z), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 0.0, 1.0);
		vec4 gn3 = raw_weights.w > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.w), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 0.0, 1.0);

		vec4 c0 = raw_weights.x > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.x), uv_y, dx_y, dy_y, false) : vec4(0.0);
		vec4 c1 = raw_weights.y > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.y), uv_y, dx_y, dy_y, false) : vec4(0.0);
		vec4 c2 = raw_weights.z > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.z), uv_y, dx_y, dy_y, false) : vec4(0.0);
		vec4 c3 = raw_weights.w > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.w), uv_y, dx_y, dy_y, false) : vec4(0.0);

		int idx0 = int(clamp(round(v_tex_indices.x), 0.0, 31.0));
		int idx1 = int(clamp(round(v_tex_indices.y), 0.0, 31.0));
		int idx2 = int(clamp(round(v_tex_indices.z), 0.0, 31.0));
		int idx3 = int(clamp(round(v_tex_indices.w), 0.0, 31.0));

		vec3 col0 = (c0.rgb * c0.rgb) * swatch_albedo_params[idx0].rgb;
		vec3 col1 = (c1.rgb * c1.rgb) * swatch_albedo_params[idx1].rgb;
		vec3 col2 = (c2.rgb * c2.rgb) * swatch_albedo_params[idx2].rgb;
		vec3 col3 = (c3.rgb * c3.rgb) * swatch_albedo_params[idx3].rgb;

		vec4 blend_layer_weights = norm_weights;
		vec3 ground_albedo = vec3(0.0);

		if (enable_height_blend) {
			float edge_noise = 0.0;
			if (enable_macro_noise) {
				float max_w = max(max(norm_weights.x, norm_weights.y), max(norm_weights.z, norm_weights.w));
				float transition_factor = clamp((1.0 - max_w) * 2.0, 0.0, 1.0);
				edge_noise = (base_fbm_val - 0.5) * 0.12 * transition_factor;
			}

			vec4 heights = vec4(gn0.b, gn1.b, gn2.b, gn3.b);
			vec4 noise_offsets = vec4(edge_noise, -edge_noise, edge_noise * 0.75, -edge_noise * 0.75);

			vec4 hp0 = swatch_height_params[idx0];
			vec4 hp1 = swatch_height_params[idx1];
			vec4 hp2 = swatch_height_params[idx2];
			vec4 hp3 = swatch_height_params[idx3];

			vec4 h_scales = vec4(
				hp0.x > 0.001 ? hp0.x : 1.0,
				hp1.x > 0.001 ? hp1.x : 1.0,
				hp2.x > 0.001 ? hp2.x : 1.0,
				hp3.x > 0.001 ? hp3.x : 1.0
			);

			vec4 h_biases = vec4(hp0.y, hp1.y, hp2.y, hp3.y);

			vec4 h_powers = vec4(
				hp0.z > 0.001 ? hp0.z : 1.0,
				hp1.z > 0.001 ? hp1.z : 1.0,
				hp2.z > 0.001 ? hp2.z : 1.0,
				hp3.z > 0.001 ? hp3.z : 1.0
			);

			vec4 shaped_heights = vec4(
				pow(clamp(heights.x, 0.0, 1.0), h_powers.x),
				pow(clamp(heights.y, 0.0, 1.0), h_powers.y),
				pow(clamp(heights.z, 0.0, 1.0), h_powers.z),
				pow(clamp(heights.w, 0.0, 1.0), h_powers.w)
			);

			vec4 scaled_ground_heights = vec4(
				shaped_heights.x * h_scales.x + h_biases.x,
				shaped_heights.y * h_scales.y + h_biases.y,
				shaped_heights.z * h_scales.z + h_biases.z,
				shaped_heights.w * h_scales.w + h_biases.w
			);

			vec4 height_scores = vec4(
				norm_weights.x > 0.001 ? (norm_weights.x + scaled_ground_heights.x + noise_offsets.x) : -100.0,
				norm_weights.y > 0.001 ? (norm_weights.y + scaled_ground_heights.y + noise_offsets.y) : -100.0,
				norm_weights.z > 0.001 ? (norm_weights.z + scaled_ground_heights.z + noise_offsets.z) : -100.0,
				norm_weights.w > 0.001 ? (norm_weights.w + scaled_ground_heights.w + noise_offsets.w) : -100.0
			);

			float max_score = max(max(height_scores.x, height_scores.y), max(height_scores.z, height_scores.w));
			float transition_depth = max(0.001, blend_softness);

			vec4 raw_blend = vec4(
				smoothstep(max_score - transition_depth, max_score, height_scores.x) * step(0.001, norm_weights.x),
				smoothstep(max_score - transition_depth, max_score, height_scores.y) * step(0.001, norm_weights.y),
				smoothstep(max_score - transition_depth, max_score, height_scores.z) * step(0.001, norm_weights.z),
				smoothstep(max_score - transition_depth, max_score, height_scores.w) * step(0.001, norm_weights.w)
			);

			float blend_sum = raw_blend.x + raw_blend.y + raw_blend.z + raw_blend.w;
			blend_layer_weights = blend_sum > 0.0001 ? raw_blend / blend_sum : norm_weights;

			ground_albedo = (col0 * blend_layer_weights.x +
			                 col1 * blend_layer_weights.y +
			                 col2 * blend_layer_weights.z +
			                 col3 * blend_layer_weights.w);
		} else {
			ground_albedo = (col0 * norm_weights.x +
			                 col1 * norm_weights.y +
			                 col2 * norm_weights.z +
			                 col3 * norm_weights.w);
		}

		vec3 ground_normal = geom_normal;
		float ground_ao = 1.0;
		float ground_roughness = 0.85;

		float gw0 = blend_layer_weights.x;
		float gw1 = blend_layer_weights.y;
		float gw2 = blend_layer_weights.z;
		float gw3 = blend_layer_weights.w;

		vec2 t0 = unpack_normal_xy(gn0.rg) * swatch_height_params[idx0].w;
		vec2 t1 = unpack_normal_xy(gn1.rg) * swatch_height_params[idx1].w;
		vec2 t2 = unpack_normal_xy(gn2.rg) * swatch_height_params[idx2].w;
		vec2 t3 = unpack_normal_xy(gn3.rg) * swatch_height_params[idx3].w;

		float tz0 = sqrt(max(0.0, 1.0 - dot(t0, t0)));
		float tz1 = sqrt(max(0.0, 1.0 - dot(t1, t1)));
		float tz2 = sqrt(max(0.0, 1.0 - dot(t2, t2)));
		float tz3 = sqrt(max(0.0, 1.0 - dot(t3, t3)));

		float ao0 = clamp((0.35 + 0.65 * tz0) * (0.55 + 0.45 * gn0.b), 0.05, 1.0);
		float ao1 = clamp((0.35 + 0.65 * tz1) * (0.55 + 0.45 * gn1.b), 0.05, 1.0);
		float ao2 = clamp((0.35 + 0.65 * tz2) * (0.55 + 0.45 * gn2.b), 0.05, 1.0);
		float ao3 = clamp((0.35 + 0.65 * tz3) * (0.55 + 0.45 * gn3.b), 0.05, 1.0);

		if (enable_normal_mapping) {
			vec3 gn0_vec = unpack_triplanar_normal(gn0, vec3(0.0, 1.0, 0.0), geom_normal, swatch_height_params[idx0].w);
			vec3 gn1_vec = unpack_triplanar_normal(gn1, vec3(0.0, 1.0, 0.0), geom_normal, swatch_height_params[idx1].w);
			vec3 gn2_vec = unpack_triplanar_normal(gn2, vec3(0.0, 1.0, 0.0), geom_normal, swatch_height_params[idx2].w);
			vec3 gn3_vec = unpack_triplanar_normal(gn3, vec3(0.0, 1.0, 0.0), geom_normal, swatch_height_params[idx3].w);

			ground_normal = normalize(gn0_vec * gw0 + gn1_vec * gw1 + gn2_vec * gw2 + gn3_vec * gw3);
			ground_ao = (ao0 * gw0 + ao1 * gw1 + ao2 * gw2 + ao3 * gw3);
			ground_roughness = (gn0.a * swatch_albedo_params[idx0].w * gw0 +
			                    gn1.a * swatch_albedo_params[idx1].w * gw1 +
			                    gn2.a * swatch_albedo_params[idx2].w * gw2 +
			                    gn3.a * swatch_albedo_params[idx3].w * gw3);
		} else {
			float max_gw = max(max(gw0, gw1), max(gw2, gw3));
			float dom_layer = round(v_tex_indices.x);
			if (gw1 >= max_gw) dom_layer = round(v_tex_indices.y);
			else if (gw2 >= max_gw) dom_layer = round(v_tex_indices.z);
			else if (gw3 >= max_gw) dom_layer = round(v_tex_indices.w);

			int dom_idx = int(clamp(dom_layer, 0.0, 255.0));
			float uv_scale = swatch_params[dom_idx].y > 0.001 ? swatch_params[dom_idx].y : 1.0;
			vec2 dom_uv = uv_y * uv_scale;
			vec2 dom_dx = dx_y * uv_scale;
			vec2 dom_dy = dy_y * uv_scale;

			vec4 gn = textureGrad(terrain_normals_pbr, vec3(dom_uv, dom_layer), dom_dx, dom_dy);
			vec2 dom_t = unpack_normal_xy(gn.rg) * swatch_height_params[dom_idx].w;
			ground_normal = unpack_triplanar_normal(gn, vec3(0.0, 1.0, 0.0), geom_normal, swatch_height_params[dom_idx].w);
			float dom_tz = sqrt(max(0.0, 1.0 - dot(dom_t, dom_t)));
			ground_ao = clamp((0.35 + 0.65 * dom_tz) * (0.55 + 0.45 * gn.b), 0.05, 1.0);
			ground_roughness = gn.a * swatch_albedo_params[dom_idx].w;
		}

		if (enable_macro_noise && macro_normal_strength > 0.0) {
			float n_noise_y_x = (macro_fbm((v_world_pos.xz + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_y_z = (macro_fbm((v_world_pos.xz + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_y = vec3(n_noise_y_x, 0.0, n_noise_y_z);
			ground_normal = normalize(ground_normal + noise_norm_y * macro_normal_strength);
		}

		terrain_color = ground_albedo;
		blended_world_normal = ground_normal;
		blended_ao = ground_ao;
		blended_roughness = ground_roughness;
	} else {
		vec3 cliff_geom_normal = geom_normal;

		vec3 triplanar_normal = abs(cliff_geom_normal);
		vec3 blend_weights = pow(triplanar_normal, vec3(4.0));
		float bw_sum = blend_weights.x + blend_weights.y + blend_weights.z;
		blend_weights = bw_sum > 0.0001 ? blend_weights / bw_sum : vec3(0.0, 1.0, 0.0);

		vec2 uv_x = v_world_pos.zy * texture_scale;
		vec2 uv_y = v_world_pos.xz * texture_scale;
		vec2 uv_z = v_world_pos.xy * texture_scale;

		if (enable_macro_noise && uv_warp_strength > 0.0) {
			float warp_factor = uv_warp_strength * 0.05 / max(0.001, macro_scale);
			vec2 warp_zy = vec2(macro_fbm(v_world_pos.zy * macro_scale), macro_fbm((v_world_pos.zy + vec2(13.1, 29.7)) * macro_scale)) - 0.5;
			vec2 warp_xz = vec2(macro_fbm(v_world_pos.xz * macro_scale), macro_fbm((v_world_pos.xz + vec2(17.3, 31.7)) * macro_scale)) - 0.5;
			vec2 warp_xy = vec2(macro_fbm(v_world_pos.xy * macro_scale), macro_fbm((v_world_pos.xy + vec2(37.3, 11.9)) * macro_scale)) - 0.5;

			uv_x = (v_world_pos.zy + warp_zy * warp_factor) * texture_scale;
			uv_y = (v_world_pos.xz + warp_xz * warp_factor) * texture_scale;
			uv_z = (v_world_pos.xy + warp_xy * warp_factor) * texture_scale;
		}

		vec2 dx_x = dFdx(uv_x);
		vec2 dy_x = dFdy(uv_x);
		vec2 dx_y = dFdx(uv_y);
		vec2 dy_y = dFdy(uv_y);
		vec2 dx_z = dFdx(uv_z);
		vec2 dy_z = dFdy(uv_z);

		vec4 raw_c_weights = v_cliff_tex_weights;
		float total_c_weight = raw_c_weights.x + raw_c_weights.y + raw_c_weights.z + raw_c_weights.w;
		vec4 norm_c_weights = total_c_weight > 0.0001 ? raw_c_weights / total_c_weight : vec4(1.0, 0.0, 0.0, 0.0);

		if (blend_noise_strength > 0.001) {
			vec2 blend_noise_uv = (v_world_pos.zy * blend_weights.x + v_world_pos.xz * blend_weights.y + v_world_pos.xy * blend_weights.z) * blend_noise_scale;
			float b_noise = (macro_fbm(blend_noise_uv) - 0.5) * 2.0 * blend_noise_strength;
			vec4 perturbed_c_weights = max(vec4(0.0), norm_c_weights + vec4(b_noise, -b_noise, b_noise * 0.5, -b_noise * 0.5));
			float perturbed_c_sum = perturbed_c_weights.x + perturbed_c_weights.y + perturbed_c_weights.z + perturbed_c_weights.w;
			norm_c_weights = perturbed_c_sum > 0.0001 ? perturbed_c_weights / perturbed_c_sum : norm_c_weights;
		}

		int c_idx0 = int(clamp(round(v_cliff_tex_indices.x), 0.0, 255.0));
		int c_idx1 = int(clamp(round(v_cliff_tex_indices.y), 0.0, 255.0));
		int c_idx2 = int(clamp(round(v_cliff_tex_indices.z), 0.0, 255.0));
		int c_idx3 = int(clamp(round(v_cliff_tex_indices.w), 0.0, 255.0));

		TriplanarPbrData cp0 = raw_c_weights.x > 0.001 ? sample_triplanar_pbr(terrain_normals_pbr, round(v_cliff_tex_indices.x), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, cliff_geom_normal, swatch_height_params[c_idx0].w, swatch_albedo_params[c_idx0].w) : TriplanarPbrData(cliff_geom_normal, 1.0, 0.85, 0.5);
		TriplanarPbrData cp1 = raw_c_weights.y > 0.001 ? sample_triplanar_pbr(terrain_normals_pbr, round(v_cliff_tex_indices.y), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, cliff_geom_normal, swatch_height_params[c_idx1].w, swatch_albedo_params[c_idx1].w) : TriplanarPbrData(cliff_geom_normal, 1.0, 0.85, 0.5);
		TriplanarPbrData cp2 = raw_c_weights.z > 0.001 ? sample_triplanar_pbr(terrain_normals_pbr, round(v_cliff_tex_indices.z), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, cliff_geom_normal, swatch_height_params[c_idx2].w, swatch_albedo_params[c_idx2].w) : TriplanarPbrData(cliff_geom_normal, 1.0, 0.85, 0.5);
		TriplanarPbrData cp3 = raw_c_weights.w > 0.001 ? sample_triplanar_pbr(terrain_normals_pbr, round(v_cliff_tex_indices.w), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, cliff_geom_normal, swatch_height_params[c_idx3].w, swatch_albedo_params[c_idx3].w) : TriplanarPbrData(cliff_geom_normal, 1.0, 0.85, 0.5);

		vec4 cliff0 = raw_c_weights.x > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.x), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);
		vec4 cliff1 = raw_c_weights.y > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.y), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);
		vec4 cliff2 = raw_c_weights.z > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.z), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);
		vec4 cliff3 = raw_c_weights.w > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.w), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);

		vec3 c_col0 = (cliff0.rgb * cliff0.rgb) * swatch_albedo_params[c_idx0].rgb;
		vec3 c_col1 = (cliff1.rgb * cliff1.rgb) * swatch_albedo_params[c_idx1].rgb;
		vec3 c_col2 = (cliff2.rgb * cliff2.rgb) * swatch_albedo_params[c_idx2].rgb;
		vec3 c_col3 = (cliff3.rgb * cliff3.rgb) * swatch_albedo_params[c_idx3].rgb;

		vec4 cliff_blend_weights = norm_c_weights;
		vec3 cliff_albedo = vec3(0.0);

		if (enable_height_blend) {
			float cliff_edge_noise = 0.0;
			if (enable_macro_noise) {
				float max_w = max(max(norm_c_weights.x, norm_c_weights.y), max(norm_c_weights.z, norm_c_weights.w));
				float transition_factor = clamp((1.0 - max_w) * 2.0, 0.0, 1.0);
				cliff_edge_noise = (base_fbm_val - 0.5) * 0.12 * transition_factor;
			}

			vec4 c_heights = vec4(cp0.height, cp1.height, cp2.height, cp3.height);
			vec4 c_noise_offsets = vec4(cliff_edge_noise, -cliff_edge_noise, cliff_edge_noise * 0.75, -cliff_edge_noise * 0.75);

			vec4 c_hp0 = swatch_height_params[c_idx0];
			vec4 c_hp1 = swatch_height_params[c_idx1];
			vec4 c_hp2 = swatch_height_params[c_idx2];
			vec4 c_hp3 = swatch_height_params[c_idx3];

			vec4 c_h_scales = vec4(
				c_hp0.x > 0.001 ? c_hp0.x : 1.0,
				c_hp1.x > 0.001 ? c_hp1.x : 1.0,
				c_hp2.x > 0.001 ? c_hp2.x : 1.0,
				c_hp3.x > 0.001 ? c_hp3.x : 1.0
			);

			vec4 c_h_biases = vec4(c_hp0.y, c_hp1.y, c_hp2.y, c_hp3.y);

			vec4 c_h_powers = vec4(
				c_hp0.z > 0.001 ? c_hp0.z : 1.0,
				c_hp1.z > 0.001 ? c_hp1.z : 1.0,
				c_hp2.z > 0.001 ? c_hp2.z : 1.0,
				c_hp3.z > 0.001 ? c_hp3.z : 1.0
			);

			vec4 c_shaped_heights = vec4(
				pow(clamp(c_heights.x, 0.0, 1.0), c_h_powers.x),
				pow(clamp(c_heights.y, 0.0, 1.0), c_h_powers.y),
				pow(clamp(c_heights.z, 0.0, 1.0), c_h_powers.z),
				pow(clamp(c_heights.w, 0.0, 1.0), c_h_powers.w)
			);

			vec4 scaled_cliff_heights = vec4(
				c_shaped_heights.x * c_h_scales.x + c_h_biases.x,
				c_shaped_heights.y * c_h_scales.y + c_h_biases.y,
				c_shaped_heights.z * c_h_scales.z + c_h_biases.z,
				c_shaped_heights.w * c_h_scales.w + c_h_biases.w
			);

			vec4 c_height_scores = vec4(
				norm_c_weights.x > 0.001 ? (norm_c_weights.x + scaled_cliff_heights.x + c_noise_offsets.x) : -100.0,
				norm_c_weights.y > 0.001 ? (norm_c_weights.y + scaled_cliff_heights.y + c_noise_offsets.y) : -100.0,
				norm_c_weights.z > 0.001 ? (norm_c_weights.z + scaled_cliff_heights.z + c_noise_offsets.z) : -100.0,
				norm_c_weights.w > 0.001 ? (norm_c_weights.w + scaled_cliff_heights.w + c_noise_offsets.w) : -100.0
			);

			float max_c_score = max(max(c_height_scores.x, c_height_scores.y), max(c_height_scores.z, c_height_scores.w));
			float c_transition_depth = max(0.001, blend_softness);

			vec4 raw_c_blend = vec4(
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.x) * step(0.001, norm_c_weights.x),
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.y) * step(0.001, norm_c_weights.y),
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.z) * step(0.001, norm_c_weights.z),
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.w) * step(0.001, norm_c_weights.w)
			);

			float c_blend_sum = raw_c_blend.x + raw_c_blend.y + raw_c_blend.z + raw_c_blend.w;
			cliff_blend_weights = c_blend_sum > 0.0001 ? raw_c_blend / c_blend_sum : norm_c_weights;

			cliff_albedo = (c_col0 * cliff_blend_weights.x +
			                c_col1 * cliff_blend_weights.y +
			                c_col2 * cliff_blend_weights.z +
			                c_col3 * cliff_blend_weights.w);
		} else {
			cliff_albedo = (c_col0 * norm_c_weights.x +
			                c_col1 * norm_c_weights.y +
			                c_col2 * norm_c_weights.z +
			                c_col3 * norm_c_weights.w);
		}

		vec3 cliff_normal = geom_normal;
		float cliff_ao = 1.0;
		float cliff_roughness = 0.85;

		float cw0 = cliff_blend_weights.x;
		float cw1 = cliff_blend_weights.y;
		float cw2 = cliff_blend_weights.z;
		float cw3 = cliff_blend_weights.w;

		if (enable_normal_mapping) {
			cliff_normal = normalize(cp0.normal * cw0 + cp1.normal * cw1 + cp2.normal * cw2 + cp3.normal * cw3);
			cliff_ao = (cp0.ao * cw0 + cp1.ao * cw1 + cp2.ao * cw2 + cp3.ao * cw3);
			cliff_roughness = (cp0.roughness * cw0 + cp1.roughness * cw1 + cp2.roughness * cw2 + cp3.roughness * cw3);
		} else {
			cliff_normal = cliff_geom_normal;
			cliff_ao = (cp0.ao * cw0 + cp1.ao * cw1 + cp2.ao * cw2 + cp3.ao * cw3);
			cliff_roughness = (cp0.roughness * cw0 + cp1.roughness * cw1 + cp2.roughness * cw2 + cp3.roughness * cw3);
		}

		if (enable_macro_noise && macro_normal_strength > 0.0) {
			float n_noise_y_x = (macro_fbm((v_world_pos.xz + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_y_z = (macro_fbm((v_world_pos.xz + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_y = vec3(n_noise_y_x, 0.0, n_noise_y_z);

			float n_noise_x_y = (macro_fbm((v_world_pos.zy + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.zy - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_x_z = (macro_fbm((v_world_pos.zy + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.zy - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_x = vec3(0.0, n_noise_x_y, n_noise_x_z);

			float n_noise_z_x = (macro_fbm((v_world_pos.xy + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.xy - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_z_y = (macro_fbm((v_world_pos.xy + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.xy - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_z = vec3(n_noise_z_x, n_noise_z_y, 0.0);

			vec3 triplanar_cliff_noise = noise_norm_x * blend_weights.x + noise_norm_y * blend_weights.y + noise_norm_z * blend_weights.z;
			cliff_normal = normalize(cliff_normal + triplanar_cliff_noise * macro_normal_strength);
		}

		terrain_color = cliff_albedo;
		blended_world_normal = cliff_normal;
		blended_ao = cliff_ao;
		blended_roughness = cliff_roughness;
	}

	float macro_var = 1.0;
	if (enable_macro_noise) {
		macro_var = mix(1.0 - macro_albedo_contrast, 1.0 + macro_albedo_contrast, base_fbm_val);
	}

	vec3 final_albedo = terrain_color * macro_var * v_color.rgb;
	float terrain_lum = dot(final_albedo, vec3(0.2126, 0.7152, 0.0722));
	final_albedo = mix(vec3(terrain_lum), final_albedo, 0.82);
	vec3 emission_color = vec3(0.0);
	
	if (pathing_visible) {
		vec2 pathing_uv = (v_world_pos.xz + terrain_size / 2.0) / terrain_size;
		int code = int(round(texture(pathing_texture, pathing_uv).r * 255.0));

		vec2 cell_frac = fract(v_world_pos.xz / grid_spacing);
		int sx = int(floor(cell_frac.x * 3.0));
		int sz = int(floor(cell_frac.y * 3.0));
		int box_idx = sz * 3 + sx;

		vec4 pathing_color = vec4(0.0);
		if (box_idx == 0) {
			if (code == 0) {
				pathing_color = vec4(0.9, 0.1, 0.1, 0.75);
			}
		} else if (box_idx == 1) {
			if ((code & 1) != 0) {
				pathing_color = vec4(0.2, 0.6, 1.0, 0.75);
			}
		} else if (box_idx == 2) {
			if ((code & 2) != 0) {
				pathing_color = vec4(0.0, 0.15, 0.7, 0.75);
			}
		} else if (box_idx == 3) {
			if ((code & 4) != 0) {
				pathing_color = vec4(0.85, 0.85, 0.0, 0.75);
			}
		} else if (box_idx == 4) {
			if ((code & 8) != 0) {
				pathing_color = vec4(0.2, 0.85, 0.2, 0.75);
			}
		} else if (box_idx == 5) {
			if ((code & 32) != 0) {
				pathing_color = vec4(0.6, 0.2, 0.8, 0.75);
			}
		}

		if (pathing_color.a > 0.0) {
			final_albedo = mix(final_albedo, pathing_color.rgb, pathing_color.a);
		}
	}
	
	if (grid_visible) {
		vec2 grid_uv = v_world_pos.xz / grid_spacing;
		
		vec2 df = fwidth(grid_uv) * 3.0;
		vec2 grid_lines = smoothstep(vec2(1.0) - df, vec2(1.0), fract(grid_uv)) + 
						  (1.0 - smoothstep(vec2(0.0), df, fract(grid_uv)));
		
		float thin_line = max(grid_lines.x, grid_lines.y);
		
		vec2 thick_grid_uv = grid_uv / 10.0;
		vec2 df_thick = fwidth(thick_grid_uv) * 3.0;
		vec2 thick_grid_lines = smoothstep(vec2(1.0) - df_thick, vec2(1.0), fract(thick_grid_uv)) + 
								(1.0 - smoothstep(vec2(0.0), df_thick, fract(thick_grid_uv)));
		
		float thick_line = max(thick_grid_lines.x, thick_grid_lines.y);
		
		if (thick_line > 0.0) {
			final_albedo = mix(final_albedo, grid_color_thick.rgb, grid_color_thick.a * thick_line);
			emission_color = mix(emission_color, grid_color_thick.rgb, grid_color_thick.a * thick_line);
		} else if (thin_line > 0.0) {
			final_albedo = mix(final_albedo, grid_color_thin.rgb, grid_color_thin.a * thin_line);
			emission_color = mix(emission_color, grid_color_thin.rgb, grid_color_thin.a * thin_line);
		}
	}

	float final_roughness = 0.9;

	if (enable_macro_noise) {
		float macro_roughness_var = mix(1.0 - macro_roughness_contrast, 1.0 + macro_roughness_contrast, base_fbm_val);
		final_roughness = clamp(blended_roughness * macro_roughness_var, 0.05, 1.0);
	} else {
		final_roughness = clamp(blended_roughness, 0.05, 1.0);
	}

	vec2 shroud_uv = (v_world_pos.xz - shroud_world_min) / shroud_world_size;
	float shroud_factor = shroud_enabled ? texture(shroud_texture, clamp(shroud_uv, 0.0, 1.0)).r : 0.0;
	final_albedo *= (1.0 - shroud_factor * 0.98);
	emission_color *= (1.0 - shroud_factor * 0.98);

	ALBEDO = final_albedo;
	NORMAL = normalize((VIEW_MATRIX * vec4(blended_world_normal, 0.0)).xyz);
	AO = blended_ao * (1.0 - shroud_factor * 0.98);
	ROUGHNESS = mix(final_roughness, 1.0, shroud_factor);
	METALLIC = 0.0;                 
	SPECULAR = specular_amt * (1.0 - shroud_factor * 0.98);
	EMISSION = emission_color;
}
";
			_material = new ShaderMaterial();
			_material.Shader = shader;
		}

		ReloadTerrainTextures();

		var defaultShroudImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
		defaultShroudImage.Fill(new Color(0f, 0f, 0f, 1f));
		var defaultShroudTexture = ImageTexture.CreateFromImage(defaultShroudImage);
		_currentShroudTexture = defaultShroudTexture;
		_material.SetShaderParameter("shroud_texture", defaultShroudTexture);
		_material.SetShaderParameter("shroud_enabled", GameHost.Instance == null || !GameHost.Instance.IsMapEditorMode);

		float halfW = (Width * QuadSize) * 0.5f;
		float halfD = (Depth * QuadSize) * 0.5f;
		_material.SetShaderParameter("shroud_world_min", new Vector2(-halfW, -halfD));
		_material.SetShaderParameter("shroud_world_size", new Vector2(Width * QuadSize, Depth * QuadSize));

		_material.SetShaderParameter("grid_spacing", QuadSize);
		_material.SetShaderParameter("terrain_size", new Vector2(Width * QuadSize, Depth * QuadSize));
		_material.SetShaderParameter("texture_scale", 1.0f / QuadSize);
		_material.SetShaderParameter("cliff_jitter_strength", CliffJitterStrength);
		_material.SetShaderParameter("cliff_jitter_scale", CliffJitterScale);
		_material.SetShaderParameter("blend_softness", BlendSoftness);
		_material.SetShaderParameter("blend_noise_strength", BlendNoiseStrength);
		_material.SetShaderParameter("blend_noise_scale", BlendNoiseScale);

		ApplyQualitySettings(GameSettings.QualityIdx);

		CreateWater();
		CreateChunks();
		UpdateMeshAndPhysics();
	}

	public void ApplyQualitySettings(GraphicsQuality quality)
	{
		ApplyQualitySettings((int)quality);
	}

	public void ApplyQualitySettings(int qualityIdx)
	{
		if (_material == null) return;

		switch (qualityIdx)
		{
			case 0:
			case 1:
				_material.SetShaderParameter("enable_macro_noise", false);
				_material.SetShaderParameter("enable_height_blend", false);
				_material.SetShaderParameter("enable_normal_mapping", false);
				_material.SetShaderParameter("specular_amt", 0.0f);
				break;
			case 2:
			case 3:
			default:
				_material.SetShaderParameter("enable_macro_noise", true);
				_material.SetShaderParameter("enable_height_blend", true);
				_material.SetShaderParameter("enable_normal_mapping", true);
				_material.SetShaderParameter("specular_amt", 0.2f);
				break;
		}

		_material.SetShaderParameter("cliff_jitter_strength", CliffJitterStrength);
		_material.SetShaderParameter("blend_noise_strength", BlendNoiseStrength);
		_material.SetShaderParameter("cliff_jitter_scale", CliffJitterScale);
		_material.SetShaderParameter("blend_noise_scale", BlendNoiseScale);
	}

	protected static Texture2DArray? _cachedAlbedoTextureArray;
	protected static Texture2DArray? _cachedNormalRoughnessTextureArray;
	protected static string? _cachedMapDir;
	protected List<string> _loadedTextureList = new List<string>();
	public IReadOnlyList<string> LoadedTextureList => _loadedTextureList;
	protected Godot.Vector4[] _swatchParamsCache = new Godot.Vector4[TextureSwatchSlots.MaxSlots];
	protected Godot.Vector4[] _swatchHeightParamsCache = new Godot.Vector4[TextureSwatchSlots.MaxSlots];
	protected Godot.Vector4[] _swatchAlbedoParamsCache = new Godot.Vector4[TextureSwatchSlots.MaxSlots];

	public class SwatchLiveConfig
	{
		public float? TileMode { get; set; }
		public float? UvScale { get; set; }
		public float? StochasticTileSize { get; set; }
		public float? CrossFade { get; set; }
		public float? Brightness { get; set; }
		public Color? Tint { get; set; }
		public float? HeightScale { get; set; }
		public float? HeightOffset { get; set; }
		public float? CrevicePower { get; set; }
		public float? NormalScale { get; set; }
		public float? RoughnessScale { get; set; }
	}

	protected static readonly Dictionary<string, SwatchLiveConfig> _liveSwatchOverrides = new(StringComparer.OrdinalIgnoreCase);

	public virtual void UpdateTextureParamDirect(
		string swatchName,
		string tileMode,
		float uvScale,
		float stochasticTileSize,
		float crossFade = 0.0f,
		float? brightness = null,
		string? tintStr = null,
		float heightScale = 1.0f,
		float heightOffset = 0.0f,
		float crevicePower = 1.0f,
		float normalScale = 1.0f,
		float roughnessScale = 1.0f)
	{
		if (_material == null) return;
		string cleanName = System.IO.Path.GetFileNameWithoutExtension(swatchName);

		float tm = string.Equals(tileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0.0f : 1.0f;
		float uv = Math.Clamp(uvScale, 0.1f, 4.0f);
		float stoch = Math.Clamp(stochasticTileSize, 0.5f, 3.0f);
		float cf = Math.Clamp(crossFade, 0.0f, 10.0f) * 0.01f;

		float hs = Math.Clamp(heightScale, 0.1f, 3.0f);
		float ho = Math.Clamp(heightOffset, -1.0f, 1.0f);
		float cp = Math.Clamp(crevicePower, 0.5f, 4.0f);
		float ns = Math.Clamp(normalScale, 0.0f, 3.0f);
		float rs = Math.Clamp(roughnessScale, 0.1f, 3.0f);

		Color? parsedTint = null;
		if (!string.IsNullOrEmpty(tintStr) && Color.HtmlIsValid(tintStr))
		{
			parsedTint = Color.FromHtml(tintStr);
		}

		_liveSwatchOverrides[cleanName] = new SwatchLiveConfig
		{
			TileMode = tm,
			UvScale = uv,
			StochasticTileSize = stoch,
			CrossFade = cf,
			Brightness = brightness,
			Tint = parsedTint,
			HeightScale = hs,
			HeightOffset = ho,
			CrevicePower = cp,
			NormalScale = ns,
			RoughnessScale = rs
		};

		int targetIndex = -1;
		for (int i = 0; i < _loadedTextureList.Count && i < TextureSwatchSlots.MaxSlots; i++)
		{
			if (string.Equals(_loadedTextureList[i], cleanName, StringComparison.OrdinalIgnoreCase))
			{
				targetIndex = i;
				break;
			}
		}

		if (targetIndex >= 0)
		{
			_swatchParamsCache[targetIndex] = new Godot.Vector4(tm, uv, stoch, cf);
			_swatchHeightParamsCache[targetIndex] = new Godot.Vector4(hs, ho, cp, ns);

			float currentScaleFactor = 1.0f;
			float currentBrightness = brightness ?? 1.0f;
			Color currentTint = parsedTint ?? Colors.White;

			if (!brightness.HasValue && _liveSwatchOverrides.TryGetValue(cleanName, out var existingOver) && existingOver.Brightness.HasValue)
			{
				currentBrightness = existingOver.Brightness.Value;
			}
			if (!parsedTint.HasValue && _liveSwatchOverrides.TryGetValue(cleanName, out var existingOver2) && existingOver2.Tint.HasValue)
			{
				currentTint = existingOver2.Tint.Value;
			}

			string mapDir = MapWorkspaceService.GetActiveWorkspacePath();
			try
			{
				var unionedAssets = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(mapDir);
				var texturesObj = unionedAssets?["textures"] as System.Text.Json.Nodes.JsonObject;
					if (texturesObj != null)
					{
						foreach (var kvp in texturesObj)
						{
							string baseName = System.IO.Path.GetFileNameWithoutExtension(kvp.Key);
							if (string.Equals(baseName, cleanName, StringComparison.OrdinalIgnoreCase) && kvp.Value is System.Text.Json.Nodes.JsonObject sObj)
							{
								string scaleStr = sObj["Scale_Factor"]?.ToString() ?? sObj["scale_factor"]?.ToString() ?? sObj["ScaleFactor"]?.ToString();
								if (!string.IsNullOrEmpty(scaleStr) && float.TryParse(scaleStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedScale) && parsedScale > 0.0001f)
								{
									currentScaleFactor = Math.Clamp(parsedScale, 0.10f, 4.0f);
								}
								break;
							}
						}
					}
			}
			catch { }

			if (currentScaleFactor <= 0.0001f || MathF.Abs(currentScaleFactor - 1.0f) < 0.0001f)
			{
				string rtexPath = System.IO.Path.Combine(mapDir, "Assets", "textures", cleanName + ".rtex");
				if (!System.IO.File.Exists(rtexPath)) rtexPath = System.IO.Path.Combine(mapDir, cleanName + ".rtex");
				if (!System.IO.File.Exists(rtexPath)) rtexPath = PathUtils.FindPath($"Assets/textures/{cleanName}.rtex");
				if (!System.IO.File.Exists(rtexPath)) rtexPath = PathUtils.FindPath($"MapTemplate/Assets/textures/{cleanName}.rtex");

				float rtexSf = ExtractRtexScaleFactor(rtexPath);
				if (rtexSf > 0.0001f && MathF.Abs(rtexSf - 1.0f) > 0.001f)
				{
					currentScaleFactor = rtexSf;
				}
				else if (System.IO.File.Exists(rtexPath))
				{
					float calc = Realm.Shared.Textures.TextureConverter.CalculateLuminanceScaleFactor(rtexPath);
					currentScaleFactor = calc > 0.0001f ? Math.Clamp(calc, 0.10f, 4.0f) : 1.0f;
				}
				else
				{
					currentScaleFactor = 1.0f;
				}
			}

			if (currentScaleFactor <= 0.0001f) currentScaleFactor = 1.0f;
			if (currentBrightness <= 0.0001f) currentBrightness = 1.0f;

			float effectiveMultiplier = currentScaleFactor * currentBrightness;
			if (effectiveMultiplier <= 0.0001f) effectiveMultiplier = 1.0f;
			_swatchAlbedoParamsCache[targetIndex] = new Godot.Vector4(currentTint.R * effectiveMultiplier, currentTint.G * effectiveMultiplier, currentTint.B * effectiveMultiplier, rs);

			_material.SetShaderParameter("swatch_params", _swatchParamsCache);
			_material.SetShaderParameter("swatch_height_params", _swatchHeightParamsCache);
			_material.SetShaderParameter("swatch_albedo_params", _swatchAlbedoParamsCache);

			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Terrain.TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var ts = ref GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Terrain.TerrainState>(GameHost.Instance.WorldEntity);
				if (ts.SwatchConfigs == null || ts.SwatchConfigs.Length != TextureSwatchSlots.MaxSlots)
				{
					ts.SwatchConfigs = new Realm.Ecs.Components.Terrain.TerrainSwatchConfig[TextureSwatchSlots.MaxSlots];
				}
				ts.SwatchConfigs[targetIndex] = new Realm.Ecs.Components.Terrain.TerrainSwatchConfig(hs, ho, cp, ns);
			}
		}
	}

	public struct ActiveSwatchConfig
	{
		public string TileMode { get; set; }
		public float UvScale { get; set; }
		public float StochasticTileSize { get; set; }
		public float CrossFade { get; set; }
		public float HeightScale { get; set; }
		public float HeightOffset { get; set; }
		public float CrevicePower { get; set; }
		public float NormalScale { get; set; }
		public float RoughnessScale { get; set; }
		public float Brightness { get; set; }
		public Color Tint { get; set; }
	}

	public virtual ActiveSwatchConfig GetActiveSwatchConfig(string swatchName)
	{
		string cleanName = System.IO.Path.GetFileNameWithoutExtension(swatchName);
		int targetIndex = -1;
		for (int i = 0; i < _loadedTextureList.Count && i < TextureSwatchSlots.MaxSlots; i++)
		{
			if (string.Equals(_loadedTextureList[i], cleanName, StringComparison.OrdinalIgnoreCase))
			{
				targetIndex = i;
				break;
			}
		}

		var config = new ActiveSwatchConfig
		{
			TileMode = "Stochastic",
			UvScale = 1.0f,
			StochasticTileSize = 1.0f,
			CrossFade = 0.0f,
			HeightScale = 1.0f,
			HeightOffset = 0.0f,
			CrevicePower = 1.0f,
			NormalScale = 1.0f,
			RoughnessScale = 1.0f,
			Brightness = 1.0f,
			Tint = Colors.White
		};

		if (targetIndex >= 0 && _swatchParamsCache != null && targetIndex < _swatchParamsCache.Length)
		{
			var p = _swatchParamsCache[targetIndex];
			var hp = _swatchHeightParamsCache[targetIndex];
			config.TileMode = p.X < 0.5f ? "Grid" : "Stochastic";
			config.UvScale = p.Y > 0.001f ? p.Y : 1.0f;
			config.StochasticTileSize = p.Z > 0.001f ? p.Z : 1.0f;
			config.CrossFade = p.W * 100.0f;
			config.HeightScale = hp.X > 0.001f ? hp.X : 1.0f;
			config.HeightOffset = hp.Y;
			config.CrevicePower = hp.Z > 0.001f ? hp.Z : 1.0f;
			config.NormalScale = hp.W > 0.0001f ? hp.W : 1.0f;
			if (_swatchAlbedoParamsCache != null && targetIndex < _swatchAlbedoParamsCache.Length)
			{
				config.RoughnessScale = _swatchAlbedoParamsCache[targetIndex].W > 0.0001f ? _swatchAlbedoParamsCache[targetIndex].W : 1.0f;
			}
		}

		if (_liveSwatchOverrides.TryGetValue(cleanName, out var liveOver))
		{
			if (liveOver.TileMode.HasValue) config.TileMode = liveOver.TileMode.Value < 0.5f ? "Grid" : "Stochastic";
			if (liveOver.UvScale.HasValue) config.UvScale = liveOver.UvScale.Value;
			if (liveOver.StochasticTileSize.HasValue) config.StochasticTileSize = liveOver.StochasticTileSize.Value;
			if (liveOver.CrossFade.HasValue) config.CrossFade = liveOver.CrossFade.Value * 100.0f;
			if (liveOver.HeightScale.HasValue) config.HeightScale = liveOver.HeightScale.Value;
			if (liveOver.HeightOffset.HasValue) config.HeightOffset = liveOver.HeightOffset.Value;
			if (liveOver.CrevicePower.HasValue) config.CrevicePower = liveOver.CrevicePower.Value;
			if (liveOver.NormalScale.HasValue) config.NormalScale = liveOver.NormalScale.Value;
			if (liveOver.RoughnessScale.HasValue) config.RoughnessScale = liveOver.RoughnessScale.Value;
			if (liveOver.Brightness.HasValue) config.Brightness = liveOver.Brightness.Value;
			if (liveOver.Tint.HasValue) config.Tint = liveOver.Tint.Value;
		}

		return config;
	}

	private static float ExtractRtexScaleFactor(string rtexPath)
	{
		if (string.IsNullOrEmpty(rtexPath) || !System.IO.File.Exists(rtexPath)) return 1.0f;
		try
		{
			string? rtexMeta = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(rtexPath);
			if (!string.IsNullOrEmpty(rtexMeta))
			{
				var rNode = System.Text.Json.Nodes.JsonNode.Parse(rtexMeta);
				if (rNode is System.Text.Json.Nodes.JsonObject rObj)
				{
					if (rObj.TryGetPropertyValue("scale_factor", out var sfVal) ||
						rObj.TryGetPropertyValue("Scale_Factor", out sfVal) ||
						rObj.TryGetPropertyValue("scaleFactor", out sfVal) ||
						rObj.TryGetPropertyValue("ScaleFactor", out sfVal))
					{
						if (float.TryParse(sfVal?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedScale))
						{
							return Math.Clamp(parsedScale, 0.10f, 4.0f);
						}
					}
				}
			}
		}
		catch { }
		return 1.0f;
	}

	private (Image? AlbedoHeight, Image? NormalRoughness) LoadRtexLayers(string rtexPath)
	{
		string globalRtexPath = Godot.ProjectSettings.GlobalizePath(rtexPath);
		if (!System.IO.File.Exists(globalRtexPath)) return (null, null);

		try
		{
			byte[] bytes = System.IO.File.ReadAllBytes(globalRtexPath);
			var (_, layers, _) = Realm.Shared.Textures.RtexFile.Parse(bytes);
			if (layers.Count == 0) return (null, null);

			Image? img0 = null;
			if (layers.Count > 0 && layers[0].Length > 0)
			{
				img0 = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
				if (img0.LoadWebpFromBuffer(layers[0]) != Error.Ok)
				{
					img0.LoadPngFromBuffer(layers[0]);
				}
			}

			Image? img1 = null;
			if (layers.Count > 1 && layers[1].Length > 0)
			{
				img1 = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
				if (img1.LoadWebpFromBuffer(layers[1]) != Error.Ok)
				{
					img1.LoadPngFromBuffer(layers[1]);
				}
			}

			return (img0, img1);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to load RTEX layers from '{globalRtexPath}': {ex.Message}");
			return (null, null);
		}
	}

	public void ReloadTerrainTextures(bool forceReload = false)
	{
		if (_material == null) return;
		if (forceReload)
		{
			_liveSwatchOverrides.Clear();
			_cachedAlbedoTextureArray = null;
			_cachedNormalRoughnessTextureArray = null;
			_cachedMapDir = null;
		}
		string mapDir = MapWorkspaceService.GetActiveWorkspacePath();

		var textureList = new List<string>();
		System.Text.Json.Nodes.JsonObject? texturesObj = null;

		try
		{
			var unionedAssets = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(mapDir);
			texturesObj = unionedAssets?["textures"] as System.Text.Json.Nodes.JsonObject;
		}
		catch { }

		var swatchSlots = Realm.Godot.Utils.TextureSwatchSlots.ResolveSlots(texturesObj, mapDir);
		for (int i = 0; i < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots; i++)
		{
			textureList.Add(swatchSlots[i].BaseName ?? "");
		}

		var swatchParams = new Godot.Vector4[Realm.Godot.Utils.TextureSwatchSlots.MaxSlots];
		var swatchHeightParams = new Godot.Vector4[Realm.Godot.Utils.TextureSwatchSlots.MaxSlots];
		var swatchAlbedoParams = new Godot.Vector4[Realm.Godot.Utils.TextureSwatchSlots.MaxSlots];

		for (int i = 0; i < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots; i++)
		{
			var slot = swatchSlots[i];
			if (slot.IsFiller || string.IsNullOrEmpty(slot.BaseName))
			{
				swatchParams[i] = new Godot.Vector4(1.0f, 1.0f, 1.0f, 0.05f);
				swatchHeightParams[i] = new Godot.Vector4(1.0f, 0.0f, 1.0f, 0.0f);
				swatchAlbedoParams[i] = new Godot.Vector4(1.0f, 1.0f, 1.0f, 1.0f);
				continue;
			}

			string name = slot.BaseName;
			System.Text.Json.Nodes.JsonNode? swatchNode = slot.MetadataNode;

			float tileMode = 1.0f;
			float uvScale = 1.0f;
			float stochasticTileSize = 1.0f;
			float crossFade = 0.0f;
			float heightScale = 1.0f;
			float heightOffset = 0.0f;
			float crevicePower = 1.0f;
			float normalScale = 1.0f;
			float roughnessScale = 1.0f;
			float texScaleFactor = 1.0f;
			float texBrightness = 1.0f;
			Color texTint = new Color(1.0f, 1.0f, 1.0f);

			if (swatchNode is System.Text.Json.Nodes.JsonObject sObj)
			{
				string tm = sObj["Tile_Mode"]?.ToString() ?? sObj["tile_mode"]?.ToString() ?? "Stochastic";
				if (string.Equals(tm, "Grid", StringComparison.OrdinalIgnoreCase))
				{
					tileMode = 0.0f;
				}

				string uvStr = sObj["UV_Scale"]?.ToString() ?? sObj["uv_scale"]?.ToString();
				if (!string.IsNullOrEmpty(uvStr) && float.TryParse(uvStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedUv))
				{
					uvScale = Math.Clamp(parsedUv, 0.1f, 4.0f);
				}

				string stochStr = sObj["Stochastic_Tile_Size"]?.ToString() ?? sObj["stochastic_tile_size"]?.ToString();
				if (!string.IsNullOrEmpty(stochStr) && float.TryParse(stochStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedStoch))
				{
					stochasticTileSize = Math.Clamp(parsedStoch, 0.5f, 3.0f);
				}

				string cfStr = sObj["Cross_Fade"]?.ToString() ?? sObj["cross_fade"]?.ToString() ?? sObj["Grid_Cross_Fade"]?.ToString() ?? sObj["grid_cross_fade"]?.ToString();
				if (!string.IsNullOrEmpty(cfStr) && float.TryParse(cfStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCf))
				{
					crossFade = parsedCf > 0.10f ? Math.Clamp(parsedCf, 0.0f, 10.0f) * 0.01f : Math.Clamp(parsedCf, 0.0f, 0.10f);
				}

				string hsStr = sObj["Height_Scale"]?.ToString() ?? sObj["height_scale"]?.ToString() ?? sObj["heightScale"]?.ToString();
				if (!string.IsNullOrEmpty(hsStr) && float.TryParse(hsStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedHs))
				{
					heightScale = Math.Clamp(parsedHs, 0.1f, 3.0f);
				}

				string hoStr = sObj["Height_Offset"]?.ToString() ?? sObj["height_offset"]?.ToString() ?? sObj["heightOffset"]?.ToString();
				if (!string.IsNullOrEmpty(hoStr) && float.TryParse(hoStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedHo))
				{
					heightOffset = Math.Clamp(parsedHo, -1.0f, 1.0f);
				}

				string cpStr = sObj["Crevice_Power"]?.ToString() ?? sObj["crevice_power"]?.ToString() ?? sObj["crevicePower"]?.ToString();
				if (!string.IsNullOrEmpty(cpStr) && float.TryParse(cpStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCp))
				{
					crevicePower = Math.Clamp(parsedCp, 0.5f, 4.0f);
				}

				string normScaleStr = sObj["Normal_Scale"]?.ToString() ?? sObj["normal_scale"]?.ToString() ?? sObj["normalScale"]?.ToString();
				if (!string.IsNullOrEmpty(normScaleStr) && float.TryParse(normScaleStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedNormScale))
				{
					normalScale = Math.Clamp(parsedNormScale, 0.0f, 3.0f);
				}

				string roughScaleStr = sObj["Roughness_Scale"]?.ToString() ?? sObj["roughness_scale"]?.ToString() ?? sObj["roughnessScale"]?.ToString();
				if (!string.IsNullOrEmpty(roughScaleStr) && float.TryParse(roughScaleStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedRoughScale))
				{
					roughnessScale = Math.Clamp(parsedRoughScale, 0.10f, 3.0f);
				}

				string scaleStr = sObj["Scale_Factor"]?.ToString() ?? sObj["scale_factor"]?.ToString() ?? sObj["ScaleFactor"]?.ToString();
				if (!string.IsNullOrEmpty(scaleStr) && float.TryParse(scaleStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedScale) && parsedScale > 0.0001f)
				{
					texScaleFactor = Math.Clamp(parsedScale, 0.10f, 4.0f);
				}
				else
				{
					string rtexPath = System.IO.Path.Combine(mapDir, "Assets", "textures", name + ".rtex");
					if (!System.IO.File.Exists(rtexPath)) rtexPath = System.IO.Path.Combine(mapDir, name + ".rtex");
					if (!System.IO.File.Exists(rtexPath)) rtexPath = PathUtils.FindPath($"Assets/textures/{name}.rtex");
					if (!System.IO.File.Exists(rtexPath)) rtexPath = PathUtils.FindPath($"MapTemplate/Assets/textures/{name}.rtex");

					float rtexSf = ExtractRtexScaleFactor(rtexPath);
					if (rtexSf > 0.0001f && MathF.Abs(rtexSf - 1.0f) > 0.001f)
					{
						texScaleFactor = rtexSf;
					}
					else if (System.IO.File.Exists(rtexPath))
					{
						float calc = Realm.Shared.Textures.TextureConverter.CalculateLuminanceScaleFactor(rtexPath);
						texScaleFactor = calc > 0.0001f ? Math.Clamp(calc, 0.10f, 4.0f) : 1.0f;
					}
					else
					{
						texScaleFactor = 1.0f;
					}
				}

				if (texScaleFactor <= 0.0001f) texScaleFactor = 1.0f;

				string brightStr = sObj["Brightness"]?.ToString() ?? sObj["brightness"]?.ToString();
				if (!string.IsNullOrEmpty(brightStr) && float.TryParse(brightStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedBright))
				{
					texBrightness = Math.Clamp(parsedBright, 0.10f, 2.5f);
				}

				string tintStr = sObj["Tint"]?.ToString() ?? sObj["tint"]?.ToString();
				if (!string.IsNullOrEmpty(tintStr) && Color.HtmlIsValid(tintStr))
				{
					texTint = Color.FromHtml(tintStr);
				}
			}

			if (_liveSwatchOverrides.TryGetValue(name, out var liveOver))
			{
				if (liveOver.TileMode.HasValue) tileMode = liveOver.TileMode.Value;
				if (liveOver.UvScale.HasValue) uvScale = liveOver.UvScale.Value;
				if (liveOver.StochasticTileSize.HasValue) stochasticTileSize = liveOver.StochasticTileSize.Value;
				if (liveOver.CrossFade.HasValue) crossFade = liveOver.CrossFade.Value;
				if (liveOver.HeightScale.HasValue) heightScale = liveOver.HeightScale.Value;
				if (liveOver.HeightOffset.HasValue) heightOffset = liveOver.HeightOffset.Value;
				if (liveOver.CrevicePower.HasValue) crevicePower = liveOver.CrevicePower.Value;
				if (liveOver.NormalScale.HasValue) normalScale = liveOver.NormalScale.Value;
				if (liveOver.RoughnessScale.HasValue) roughnessScale = liveOver.RoughnessScale.Value;
				if (liveOver.Brightness.HasValue) texBrightness = liveOver.Brightness.Value;
				if (liveOver.Tint.HasValue) texTint = liveOver.Tint.Value;
			}

			if (texScaleFactor <= 0.0001f) texScaleFactor = 1.0f;
			if (texBrightness <= 0.0001f) texBrightness = 1.0f;

			float effectiveMultiplier = texScaleFactor * texBrightness;
			if (effectiveMultiplier <= 0.0001f) effectiveMultiplier = 1.0f;
			swatchParams[i] = new Godot.Vector4(tileMode, uvScale, stochasticTileSize, crossFade);
			swatchHeightParams[i] = new Godot.Vector4(heightScale, heightOffset, crevicePower, normalScale);
			swatchAlbedoParams[i] = new Godot.Vector4(texTint.R * effectiveMultiplier, texTint.G * effectiveMultiplier, texTint.B * effectiveMultiplier, roughnessScale);
		}

		_loadedTextureList = textureList;
		_swatchParamsCache = swatchParams;
		_swatchHeightParamsCache = swatchHeightParams;
		_swatchAlbedoParamsCache = swatchAlbedoParams;
		_material.SetShaderParameter("swatch_params", swatchParams);
		_material.SetShaderParameter("swatch_height_params", swatchHeightParams);
		_material.SetShaderParameter("swatch_albedo_params", swatchAlbedoParams);

		if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Terrain.TerrainState>(GameHost.Instance.WorldEntity))
		{
			ref var ts = ref GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Terrain.TerrainState>(GameHost.Instance.WorldEntity);
			ts.SwatchConfigs = new Realm.Ecs.Components.Terrain.TerrainSwatchConfig[Realm.Godot.Utils.TextureSwatchSlots.MaxSlots];
			for (int i = 0; i < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots; i++)
			{
				ts.SwatchConfigs[i] = new Realm.Ecs.Components.Terrain.TerrainSwatchConfig(
					swatchHeightParams[i].X,
					swatchHeightParams[i].Y,
					swatchHeightParams[i].Z,
					swatchHeightParams[i].W
				);
			}
		}

		if (!forceReload && _cachedAlbedoTextureArray != null && _cachedNormalRoughnessTextureArray != null && _cachedMapDir == mapDir)
		{
			_material.SetShaderParameter("swatch_params", swatchParams);
			_material.SetShaderParameter("swatch_height_params", swatchHeightParams);
			_material.SetShaderParameter("swatch_albedo_params", swatchAlbedoParams);
			_material.SetShaderParameter("terrain_textures", _cachedAlbedoTextureArray);
			_material.SetShaderParameter("terrain_normals_pbr", _cachedNormalRoughnessTextureArray);
			_material.SetShaderParameter("cliff_textures", _cachedAlbedoTextureArray);
			_material.SetShaderParameter("cliff_normals_pbr", _cachedNormalRoughnessTextureArray);
			return;
		}

		var albedoHeightImages = new Godot.Collections.Array<Image>();
		var normalRoughnessImages = new Godot.Collections.Array<Image>();
		for (int i = 0; i < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots; i++)
		{
			var slot = swatchSlots[i];
			Image? imgLayer0 = null;
			Image? imgLayer1 = null;

			if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
			{
				string name = slot.BaseName;
				string rtexPath = System.IO.Path.Combine(mapDir, "Assets", "textures", name + ".rtex");
				if (!System.IO.File.Exists(rtexPath))
				{
					rtexPath = System.IO.Path.Combine(mapDir, name + ".rtex");
				}
				if (!System.IO.File.Exists(rtexPath))
				{
					rtexPath = PathUtils.FindPath($"Assets/textures/{name}.rtex");
				}
				if (!System.IO.File.Exists(rtexPath))
				{
					rtexPath = PathUtils.FindPath($"MapTemplate/Assets/textures/{name}.rtex");
				}
				if (!System.IO.File.Exists(rtexPath))
				{
					rtexPath = ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{name}.rtex");
				}
				if (!System.IO.File.Exists(rtexPath))
				{
					string pngPath = ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{name}.png");
					if (System.IO.File.Exists(pngPath))
					{
						ProcessAndSaveRawTexture(pngPath, rtexPath);
					}
				}

				if (System.IO.File.Exists(rtexPath))
				{
					try
					{
						var layers = LoadRtexLayers(rtexPath);
						imgLayer0 = layers.AlbedoHeight;
						imgLayer1 = layers.NormalRoughness;
					}
					catch (Exception ex)
					{
						GD.PrintErr($"Failed to load dynamic RTEX layers for {name}: {ex.Message}");
					}
				}
			}

			if (imgLayer0 == null || imgLayer1 == null)
			{
				imgLayer0 = Godot.Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Godot.Image.Format.Rgba8);
				imgLayer0.Fill(new Color(0.5f, 0.5f, 0.5f, 1.0f));
				imgLayer1 = Godot.Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Godot.Image.Format.Rgba8);
				imgLayer1.Fill(new Color(0.5f, 0.5f, 1.0f, 0.8f));
			}

			if (imgLayer0.GetWidth() != TargetTextureResolution || imgLayer0.GetHeight() != TargetTextureResolution)
			{
				imgLayer0.Resize(TargetTextureResolution, TargetTextureResolution, Godot.Image.Interpolation.Bilinear);
			}
			if (imgLayer1.GetWidth() != TargetTextureResolution || imgLayer1.GetHeight() != TargetTextureResolution)
			{
				imgLayer1.Resize(TargetTextureResolution, TargetTextureResolution, Godot.Image.Interpolation.Bilinear);
			}

			if (imgLayer0.GetFormat() != Godot.Image.Format.Rgba8)
			{
				imgLayer0.Convert(Godot.Image.Format.Rgba8);
			}
			if (imgLayer1.GetFormat() != Godot.Image.Format.Rgba8)
			{
				imgLayer1.Convert(Godot.Image.Format.Rgba8);
			}

			imgLayer0.GenerateMipmaps();
			imgLayer1.GenerateMipmaps();
			albedoHeightImages.Add(imgLayer0);
			normalRoughnessImages.Add(imgLayer1);
		}

		var albedoTextureArray = new Texture2DArray();
		albedoTextureArray.CreateFromImages(albedoHeightImages);
		var normalTextureArray = new Texture2DArray();
		normalTextureArray.CreateFromImages(normalRoughnessImages);

		_cachedAlbedoTextureArray = albedoTextureArray;
		_cachedNormalRoughnessTextureArray = normalTextureArray;
		_cachedMapDir = mapDir;

		_material.SetShaderParameter("terrain_textures", albedoTextureArray);
		_material.SetShaderParameter("terrain_normals_pbr", normalTextureArray);
		_material.SetShaderParameter("cliff_textures", albedoTextureArray);
	}

	protected void CreateChunks()
	{
		foreach (var chunk in _chunks)
		{
			if (GodotObject.IsInstanceValid(chunk.MeshInstance)) chunk.MeshInstance.QueueFree();
			if (GodotObject.IsInstanceValid(chunk.CollisionShape)) chunk.CollisionShape.QueueFree();
			if (chunk.ProfileWaterMeshes != null)
			{
				foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
				{
					if (GodotObject.IsInstanceValid(pMesh)) pMesh.QueueFree();
				}
				chunk.ProfileWaterMeshes.Clear();
			}
		}
		_chunks.Clear();

		int w = Width;
		int d = Depth;
		_chunkedWidth = w;
		_chunkedDepth = d;

		for (int z = 0; z < d; z += CHUNK_SIZE)
		{
			for (int x = 0; x < w; x += CHUNK_SIZE)
			{
				int ex = Math.Min(x + CHUNK_SIZE, w);
				int ez = Math.Min(z + CHUNK_SIZE, d);
				
				var chunk = new TerrainChunk
				{
					StartX = x,
					StartZ = z,
					EndX = ex,
					EndZ = ez,
					ArrayMesh = new ArrayMesh()
				};

				chunk.MeshInstance = new MeshInstance3D();
				chunk.MeshInstance.Name = $"TerrainChunk_{x}_{z}";
				chunk.MeshInstance.Mesh = chunk.ArrayMesh;
				chunk.MeshInstance.MaterialOverride = _material;
				chunk.MeshInstance.Layers = TerrainVisualLayer;
				AddChild(chunk.MeshInstance);

				chunk.CollisionShape = new CollisionShape3D();
				chunk.CollisionShape.Name = $"TerrainCollision_{x}_{z}";
				
				float lx = (x + (ex - x) / 2.0f - w / 2.0f) * QuadSize;
				float lz = (z + (ez - z) / 2.0f - d / 2.0f) * QuadSize;
				chunk.CollisionShape.Position = new Vector3(lx, 0.0f, lz);
				
				AddChild(chunk.CollisionShape);
				
				_chunks.Add(chunk);
			}
		}
	}

	protected static readonly StringName ShroudTextureParam = "shroud_texture";
	protected static readonly StringName ShroudEnabledParam = "shroud_enabled";
	protected ImageTexture _currentShroudTexture = null;
	protected static ImageTexture _clearShroudTexture = null;

	public void SetShroudEnabled(bool enabled)
	{
		if (_material != null)
		{
			_material.SetShaderParameter(ShroudEnabledParam, enabled);
		}
		foreach (var wMat in _waterMaterials.Values)
		{
			if (wMat != null)
			{
				wMat.SetShaderParameter(ShroudEnabledParam, enabled);
			}
		}
	}

	public static ImageTexture GetClearShroudTexture()
	{
		if (_clearShroudTexture == null)
		{
			var img = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
			img.Fill(new Color(0f, 0f, 0f, 1f));
			_clearShroudTexture = ImageTexture.CreateFromImage(img);
		}
		return _clearShroudTexture;
	}

	public void BeginMinimapCapture()
	{
		var clearTex = GetClearShroudTexture();
		if (_material != null)
		{
			_material.SetShaderParameter(ShroudTextureParam, clearTex);
		}
		foreach (var wMat in _waterMaterials.Values)
		{
			if (wMat != null)
			{
				wMat.SetShaderParameter(ShroudTextureParam, clearTex);
			}
		}
		SetAllChunksVisible(true);
	}

	public void EndMinimapCapture()
	{
		var restoreTex = _currentShroudTexture ?? GetClearShroudTexture();
		if (_material != null)
		{
			_material.SetShaderParameter(ShroudTextureParam, restoreTex);
		}
		foreach (var wMat in _waterMaterials.Values)
		{
			if (wMat != null)
			{
				wMat.SetShaderParameter(ShroudTextureParam, restoreTex);
			}
		}
	}

	public void SetShroudTexture(ImageTexture shroudTexture)
	{
		if (shroudTexture == null) return;
		_currentShroudTexture = shroudTexture;
		if (IsMinimapRendering) return;

		if (_material != null)
		{
			_material.SetShaderParameter(ShroudTextureParam, shroudTexture);
		}
		foreach (var wMat in _waterMaterials.Values)
		{
			if (wMat != null)
			{
				wMat.SetShaderParameter(ShroudTextureParam, shroudTexture);
			}
		}
	}

	public void SetFogTexture(ImageTexture fogTexture) => SetShroudTexture(fogTexture);

	public void UpdateMeshAndPhysics(bool rebuildPhysics = true, bool rebuildNavMesh = true, Rect2I? affectedRegion = null, bool rebuildWater = true)
	{
		UpdateMeshAndPhysics(rebuildPhysics, rebuildNavMesh, affectedRegion.HasValue ? new[] { affectedRegion.Value } : (IEnumerable<Rect2I>?)null, rebuildWater);
	}

	public void UpdateMeshAndPhysics(bool rebuildPhysics, bool rebuildNavMesh, IEnumerable<Rect2I>? affectedRegions, bool rebuildWater = true)
	{
		int w = Width;
		int d = Depth;

		if (SplatMap == null || SplatMap.GetLength(0) < w + 1 || SplatMap.GetLength(1) < d + 1)
		{
			var newSplatMap = new TerrainSplatWeights[w + 1, d + 1];
			for (int z = 0; z <= d; z++)
			{
				for (int x = 0; x <= w; x++)
				{
					if (SplatMap != null && x < SplatMap.GetLength(0) && z < SplatMap.GetLength(1))
						newSplatMap[x, z] = SplatMap[x, z];
					else
						newSplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				}
			}
			SplatMap = newSplatMap;
		}

		if (CliffSplatMap == null || CliffSplatMap.GetLength(0) < w + 1 || CliffSplatMap.GetLength(1) < d + 1)
		{
			var newCliffSplatMap = new TerrainSplatWeights[w + 1, d + 1];
			for (int z = 0; z <= d; z++)
			{
				for (int x = 0; x <= w; x++)
				{
					if (CliffSplatMap != null && x < CliffSplatMap.GetLength(0) && z < CliffSplatMap.GetLength(1))
						newCliffSplatMap[x, z] = CliffSplatMap[x, z];
					else
						newCliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
				}
			}
			CliffSplatMap = newCliffSplatMap;
		}

		if (_chunks.Count == 0 || _chunkedWidth != w || _chunkedDepth != d)
		{
			CreateChunks();
		}

		foreach (var chunk in _chunks)
		{
			if (affectedRegions != null)
			{
				bool intersectsAny = false;
				foreach (var region in affectedRegions)
				{
					if (!(chunk.EndX < region.Position.X || chunk.StartX > region.Position.X + region.Size.X ||
						  chunk.EndZ < region.Position.Y || chunk.StartZ > region.Position.Y + region.Size.Y))
					{
						intersectsAny = true;
						break;
					}
				}
				if (!intersectsAny)
				{
					continue;
				}
			}

			UpdateChunkMesh(chunk, rebuildPhysics);
		}

		if (rebuildWater)
		{
			RegenerateWaterMesh();
		}
	}

	public void SanitizeCornerHeights()
	{
		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;

		for (int gz = 0; gz <= d; gz++)
		{
			for (int gx = 0; gx <= w; gx++)
			{
				float h = GetGridNodeHeight(gx, gz, cells, w, d);

				if (gx > 0 && gz > 0 && gx - 1 < w && gz - 1 < d) cells[gx - 1, gz - 1].Y_SE = h;
				if (gx < w && gz > 0 && gz - 1 < d) cells[gx, gz - 1].Y_SW = h;
				if (gx > 0 && gz < d && gx - 1 < w) cells[gx - 1, gz].Y_NE = h;
				if (gx < w && gz < d) cells[gx, gz].Y_NW = h;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float GetGridNodeHeight(int gx, int gz)
	{
		return GetGridNodeHeight(gx, gz, Cells, Width, Depth);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float GetGridNodeHeight(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		if (cells == null || w <= 0 || d <= 0) return 0f;
		if ((uint)gx < (uint)w && (uint)gz < (uint)d) return cells[gx, gz].Y_NW;
		int cellX = Math.Clamp(gx, 0, w - 1);
		int cellZ = Math.Clamp(gz, 0, d - 1);
		var cell = cells[cellX, cellZ];
		if (gx >= w && gz >= d) return cell.Y_SE;
		if (gx >= w) return cell.Y_NE;
		if (gz >= d) return cell.Y_SW;
		return cell.Y_NW;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float GetGridNodeHeight(int gx, int gz, float[,] heights, int w, int d)
	{
		if (heights == null || w <= 0 || d <= 0) return 0f;
		int hW = heights.GetLength(0);
		int hD = heights.GetLength(1);
		int cellX = Math.Clamp(gx, 0, hW - 1);
		int cellZ = Math.Clamp(gz, 0, hD - 1);
		return heights[cellX, cellZ];
	}

	public virtual void SetPathingVisible(bool visible) { }
	public virtual void SetGridVisible(bool visible) { }
	public virtual void SetWireframeMode(bool enabled) { }
	public virtual void ToggleWireframeMode() { }
	public virtual void UpdatePathingTexture() { }
	public virtual void ResizeTerrain(int newWidth, int newDepth) { }
	public virtual void ScaleTerrainData(int newWidth, int newDepth) { }
	public virtual void RemapSplatIndices(IReadOnlyDictionary<int, int> remap) { }
	public virtual void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, TerrainCell[,] cells, int[,] pathingCodes, TerrainSplatWeights[,] splatMap) { }
	public virtual void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, float[,] heights, int[,] pathingCodes, TerrainSplatWeights[,] splatMap) { }
	public virtual void ProcessAndSaveRawTexture(string rawPngPath, string outputRtexPath) { }

	public void UpdatePhysics(Rect2I? affectedRegion = null)
	{
		foreach (var chunk in _chunks)
		{
			if (affectedRegion.HasValue)
			{
				var region = affectedRegion.Value;
				if (chunk.EndX < region.Position.X || chunk.StartX > region.Position.X + region.Size.X ||
					chunk.EndZ < region.Position.Y || chunk.StartZ > region.Position.Y + region.Size.Y)
				{
					continue;
				}
			}
			UpdateChunkPhysics(chunk);
		}
	}

	protected void UpdateChunkPhysics(TerrainChunk chunk)
	{
		int cellWidth = chunk.EndX - chunk.StartX;
		int cellDepth = chunk.EndZ - chunk.StartZ;

		int mapDataCount = (cellWidth + 1) * (cellDepth + 1);
		if (chunk.MapDataCache == null || chunk.MapDataCache.Length != mapDataCount)
		{
			chunk.MapDataCache = new float[mapDataCount];
		}

		bool mapDataChanged = false;
		int idx = 0;

		for (int z = 0; z <= cellDepth; z++)
		{
			for (int x = 0; x <= cellWidth; x++)
			{
				int vx = chunk.StartX + x;
				int vz = chunk.StartZ + z;
				float h = GetGridNodeHeight(vx, vz);

				if (Math.Abs(chunk.MapDataCache[idx] - h) > 0.0001f)
				{
					chunk.MapDataCache[idx] = h;
					mapDataChanged = true;
				}
				idx++;
			}
		}

		if (mapDataChanged || chunk.CollisionShape.Shape == null)
		{
			var heightMapShape = new HeightMapShape3D();
			heightMapShape.MapWidth = cellWidth + 1;
			heightMapShape.MapDepth = cellDepth + 1;
			heightMapShape.MapData = chunk.MapDataCache;

			chunk.CollisionShape.Shape = heightMapShape;

			float offsetX = (chunk.StartX + cellWidth / 2.0f - Width / 2.0f) * QuadSize;
			float offsetZ = (chunk.StartZ + cellDepth / 2.0f - Depth / 2.0f) * QuadSize;
			chunk.CollisionShape.Position = new Vector3(offsetX, 0, offsetZ);
			chunk.CollisionShape.Scale = new Vector3(QuadSize, 1.0f, QuadSize);
		}
	}

	protected void UpdateChunkMesh(TerrainChunk chunk, bool rebuildPhysics)
	{
		var cells = Cells;
		if (cells == null) return;
		int w = Width;
		int d = Depth;
		float quadSize = QuadSize;
		float halfWQuadSize = (w / 2.0f) * quadSize;
		float halfDQuadSize = (d / 2.0f) * quadSize;
		var splatMap = SplatMap;
		var cliffSplatMap = CliffSplatMap ?? splatMap;

		int maxVertices = 0;
		int maxIndices = 0;

		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				CountQuadElements(x, z, cells, w, d, ref maxVertices, ref maxIndices);
			}
		}

		if (chunk.VerticesCache == null || chunk.VerticesCache.Length < maxVertices)
		{
			chunk.VerticesCache = new Vector3[maxVertices];
			chunk.NormalsCache = new Vector3[maxVertices];
			chunk.UvsCache = new Vector2[maxVertices];
			chunk.ColorsCache = new Color[maxVertices];
			chunk.TexIndicesCache = new float[maxVertices * 4];
			chunk.TexWeightsCache01 = new float[maxVertices * 4];
			chunk.CliffIndicesCache = new float[maxVertices * 4];
			chunk.CliffWeightsCache = new float[maxVertices * 4];
		}

		if (chunk.IndicesCache == null || chunk.IndicesCache.Length < maxIndices)
		{
			chunk.IndicesCache = new int[maxIndices];
		}

		int vertexIndex = 0;
		int indexIndex = 0;

		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				ProcessCellQuad(chunk, x, z, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, splatMap, cliffSplatMap, ref vertexIndex, ref indexIndex);
			}
		}

		WeldChunkVertices(chunk, ref vertexIndex, indexIndex);

		Vector3[] finalVertices = chunk.VerticesCache;
		Color[] finalColors = chunk.ColorsCache;
		Vector3[] finalNormals = chunk.NormalsCache;
		Vector2[] finalUvs = chunk.UvsCache;
		float[] finalTexIndices = chunk.TexIndicesCache;
		float[] finalTexWeights = chunk.TexWeightsCache01;
		float[] finalCliffIndices = chunk.CliffIndicesCache;
		float[] finalCliffWeights = chunk.CliffWeightsCache;
		int[] finalIndices = chunk.IndicesCache;

		if (vertexIndex < chunk.VerticesCache.Length)
		{
			finalVertices = new Vector3[vertexIndex];
			Array.Copy(chunk.VerticesCache, finalVertices, vertexIndex);

			finalColors = new Color[vertexIndex];
			Array.Copy(chunk.ColorsCache, finalColors, vertexIndex);

			finalNormals = new Vector3[vertexIndex];
			Array.Copy(chunk.NormalsCache, finalNormals, vertexIndex);

			finalUvs = new Vector2[vertexIndex];
			Array.Copy(chunk.UvsCache, finalUvs, vertexIndex);

			finalTexIndices = new float[vertexIndex * 4];
			Array.Copy(chunk.TexIndicesCache, finalTexIndices, vertexIndex * 4);

			finalTexWeights = new float[vertexIndex * 4];
			Array.Copy(chunk.TexWeightsCache01, finalTexWeights, vertexIndex * 4);

			finalCliffIndices = new float[vertexIndex * 4];
			Array.Copy(chunk.CliffIndicesCache, finalCliffIndices, vertexIndex * 4);

			finalCliffWeights = new float[vertexIndex * 4];
			Array.Copy(chunk.CliffWeightsCache, finalCliffWeights, vertexIndex * 4);
		}

		if (indexIndex < chunk.IndicesCache.Length)
		{
			finalIndices = new int[indexIndex];
			Array.Copy(chunk.IndicesCache, finalIndices, indexIndex);
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = finalVertices;
		arrays[(int)Mesh.ArrayType.Color] = finalColors;
		arrays[(int)Mesh.ArrayType.Normal] = finalNormals;
		arrays[(int)Mesh.ArrayType.TexUV] = finalUvs;
		arrays[(int)Mesh.ArrayType.Custom0] = finalTexIndices;
		arrays[(int)Mesh.ArrayType.Custom1] = finalTexWeights;
		arrays[(int)Mesh.ArrayType.Custom2] = finalCliffIndices;
		arrays[(int)Mesh.ArrayType.Custom3] = finalCliffWeights;
		arrays[(int)Mesh.ArrayType.Index] = finalIndices;

		chunk.ArrayMesh.ClearSurfaces();
		if (vertexIndex > 0)
		{
			int custom0Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift;
			int custom1Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift;
			int custom2Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift;
			int custom3Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom3Shift;
			chunk.ArrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
				new Godot.Collections.Array<Godot.Collections.Array>(),
				null,
				(Mesh.ArrayFormat)((int)(Mesh.ArrayFormat.FormatCustom0 | Mesh.ArrayFormat.FormatCustom1 | Mesh.ArrayFormat.FormatCustom2 | Mesh.ArrayFormat.FormatCustom3) | custom0Format | custom1Format | custom2Format | custom3Format));
		}

		float minY = float.MaxValue;
		float maxY = float.MinValue;
		for (int i = 0; i < vertexIndex; i++)
		{
			float y = finalVertices[i].Y;
			if (y < minY) minY = y;
			if (y > maxY) maxY = y;
		}
		if (vertexIndex == 0) { minY = -2f; maxY = 2f; }
		chunk.MinY = minY;
		chunk.MaxY = maxY;

		float halfW = (w / 2.0f) * quadSize;
		float halfD = (d / 2.0f) * quadSize;
		float minX = chunk.StartX * quadSize - halfW;
		float maxX = chunk.EndX * quadSize - halfW;
		float minZ = chunk.StartZ * quadSize - halfD;
		float maxZ = chunk.EndZ * quadSize - halfD;
		chunk.WorldAabb = new Aabb(new Vector3(minX, minY - 2f, minZ), new Vector3(maxX - minX, Math.Max(0.5f, maxY - minY + 10f), maxZ - minZ));
		chunk.MeshInstance.CustomAabb = chunk.WorldAabb;
		if (chunk.ProfileWaterMeshes != null)
		{
			foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
			{
				if (GodotObject.IsInstanceValid(pMesh)) pMesh.CustomAabb = chunk.WorldAabb;
			}
		}

		if (rebuildPhysics || chunk.CollisionShape.Shape == null)
		{
			UpdateChunkPhysics(chunk);
		}

		if (IsRuntimeOnly)
		{
			chunk.VerticesCache = null;
			chunk.NormalsCache = null;
			chunk.ColorsCache = null;
			chunk.TexIndicesCache = null;
			chunk.TexWeightsCache01 = null;
			chunk.CliffIndicesCache = null;
			chunk.CliffWeightsCache = null;
			chunk.UvsCache = null;
			chunk.IndicesCache = null;
			chunk.MapDataCache = null;
			chunk.WeldVertexMap = null;
			chunk.WeldRemapTable = null;
		}
	}

	public override void _Process(double delta)
	{
		UpdateFrustumCulling();
	}

	private Vector3 _lastFrustumCamPos;
	private Vector3 _lastFrustumCamRot;
	private readonly Plane[] _cachedFrustumPlanes = new Plane[6];

	protected void UpdateFrustumCulling()
	{
		if (IsMinimapRendering)
		{
			SetAllChunksVisible(true);
			return;
		}

		var viewport = GetViewport();
		if (viewport == null) return;
		var camera = viewport.GetCamera3D();
		if (camera == null || !GodotObject.IsInstanceValid(camera)) return;

		if (camera.Projection == Camera3D.ProjectionType.Orthogonal)
		{
			SetAllChunksVisible(true);
			return;
		}

		Vector3 camPos = camera.GlobalPosition;
		Vector3 camRot = camera.GlobalRotation;
		if ((camPos - _lastFrustumCamPos).LengthSquared() < 0.0001f && (camRot - _lastFrustumCamRot).LengthSquared() < 0.0001f)
		{
			return;
		}
		_lastFrustumCamPos = camPos;
		_lastFrustumCamRot = camRot;

		var frustum = camera.GetFrustum();
		if (frustum == null || frustum.Count < 6) return;

		for (int i = 0; i < 6; i++)
		{
			_cachedFrustumPlanes[i] = frustum[i];
		}

		ReadOnlySpan<Plane> planesSpan = _cachedFrustumPlanes;
		foreach (var chunk in _chunks)
		{
			bool visible = IntersectsFrustum(planesSpan, chunk.WorldAabb);
			if (GodotObject.IsInstanceValid(chunk.MeshInstance) && chunk.MeshInstance.Visible != visible)
			{
				chunk.MeshInstance.Visible = visible;
			}
			if (chunk.ProfileWaterMeshes != null)
			{
				foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
				{
					if (GodotObject.IsInstanceValid(pMesh) && pMesh.Visible != visible)
					{
						pMesh.Visible = visible;
					}
				}
			}
		}
	}

	public void SetAllChunksVisible(bool visible)
	{
		foreach (var chunk in _chunks)
		{
			if (GodotObject.IsInstanceValid(chunk.MeshInstance) && chunk.MeshInstance.Visible != visible) chunk.MeshInstance.Visible = visible;
			if (chunk.ProfileWaterMeshes != null)
			{
				foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
				{
					if (GodotObject.IsInstanceValid(pMesh) && pMesh.Visible != visible) pMesh.Visible = visible;
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IntersectsFrustum(ReadOnlySpan<Plane> frustumPlanes, Aabb aabb)
	{
		aabb = aabb.Grow(8.0f);
		Vector3 min = aabb.Position;
		Vector3 max = aabb.End;

		for (int i = 0; i < frustumPlanes.Length; i++)
		{
			ref readonly Plane plane = ref frustumPlanes[i];
			Vector3 n = new Vector3(
				plane.Normal.X >= 0 ? min.X : max.X,
				plane.Normal.Y >= 0 ? min.Y : max.Y,
				plane.Normal.Z >= 0 ? min.Z : max.Z
			);

			if (plane.DistanceTo(n) > 0)
			{
				return false;
			}
		}
		return true;
	}

	public static bool IntersectsFrustum(Godot.Collections.Array<Plane> frustumPlanes, Aabb aabb)
	{
		if (frustumPlanes == null || frustumPlanes.Count == 0) return true;
		Span<Plane> span = stackalloc Plane[Math.Min(frustumPlanes.Count, 32)];
		for (int i = 0; i < span.Length; i++) span[i] = frustumPlanes[i];
		return IntersectsFrustum((ReadOnlySpan<Plane>)span, aabb);
	}

	private Vector3 GetWorldPosition(float gridX, float gridZ, float height, float halfWQuadSize, float halfDQuadSize, float quadSize)
	{
		float lx = gridX * quadSize - halfWQuadSize;
		float lz = gridZ * quadSize - halfDQuadSize;
		return new Vector3(lx, height, lz);
	}

	private Vector3 GetWorldPosition(float gridX, float gridZ, float height)
	{
		return GetWorldPosition(gridX, gridZ, height, (Width / 2.0f) * QuadSize, (Depth / 2.0f) * QuadSize, QuadSize);
	}

	private void CountQuadElements(
		int x, int z,
		TerrainCell[,] cells,
		int w, int d,
		ref int totalVertices,
		ref int totalIndices)
	{
		if (cells == null) return;
		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (x < 0 || x >= cellW || z < 0 || z >= cellD) return;

		totalVertices += 12;
		totalIndices += 12;
	}

	private (int tex0, int tex1, int tex2, int tex3) GetQuadDominantTextures(ReadOnlySpan<TerrainSplatWeights> splats)
	{
		Span<int> indices = stackalloc int[16];
		Span<float> weights = stackalloc float[16];
		int count = 0;

		for (int sIdx = 0; sIdx < splats.Length; sIdx++)
		{
			TerrainSplatWeights s = splats[sIdx];
			for (int k = 0; k < 4; k++)
			{
				int idx = k switch { 0 => s.Index0, 1 => s.Index1, 2 => s.Index2, _ => s.Index3 };
				float w = k switch { 0 => s.Weight0, 1 => s.Weight1, 2 => s.Weight2, _ => s.Weight3 };
				if (w <= 0.0001f) continue;

				bool found = false;
				for (int i = 0; i < count; i++)
				{
					if (indices[i] == idx)
					{
						weights[i] += w;
						found = true;
						break;
					}
				}
				if (!found && count < 16)
				{
					indices[count] = idx;
					weights[count] = w;
					count++;
				}
			}
		}

		if (count == 0)
		{
			int defaultTex = splats.Length > 0 ? splats[0].Index0 : 0;
			return (defaultTex, defaultTex, defaultTex, defaultTex);
		}

		for (int i = 0; i < count - 1; i++)
		{
			for (int j = i + 1; j < count; j++)
			{
				if (weights[j] > weights[i])
				{
					float tempW = weights[i];
					weights[i] = weights[j];
					weights[j] = tempW;

					int tempIdx = indices[i];
					indices[i] = indices[j];
					indices[j] = tempIdx;
				}
			}
		}

		int res0 = indices[0];
		int res1 = count > 1 ? indices[1] : res0;
		int res2 = count > 2 ? indices[2] : res0;
		int res3 = count > 3 ? indices[3] : res0;

		return (res0, res1, res2, res3);
	}

	private (int tex0, int tex1, int tex2, int tex3) GetQuadDominantTextures(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2, TerrainSplatWeights s3)
	{
		Span<TerrainSplatWeights> splats = stackalloc TerrainSplatWeights[4] { s0, s1, s2, s3 };
		return GetQuadDominantTextures(splats);
	}

	private (float w0, float w1, float w2, float w3) GetSplatWeightsForQuad(TerrainSplatWeights s, int tex0, int tex1, int tex2, int tex3)
	{
		float GetWeightForTexture(in TerrainSplatWeights splat, int targetTexIndex)
		{
			float w = 0.0f;
			if (splat.Index0 == targetTexIndex) w += splat.Weight0;
			if (splat.Index1 == targetTexIndex) w += splat.Weight1;
			if (splat.Index2 == targetTexIndex) w += splat.Weight2;
			if (splat.Index3 == targetTexIndex) w += splat.Weight3;
			return w;
		}

		float w0 = GetWeightForTexture(s, tex0);
		float w1 = (tex1 != tex0) ? GetWeightForTexture(s, tex1) : 0.0f;
		float w2 = (tex2 != tex0 && tex2 != tex1) ? GetWeightForTexture(s, tex2) : 0.0f;
		float w3 = (tex3 != tex0 && tex3 != tex1 && tex3 != tex2) ? GetWeightForTexture(s, tex3) : 0.0f;

		float sumW = w0 + w1 + w2 + w3;
		if (sumW > 0.0001f)
		{
			float invSum = 1.0f / sumW;
			w0 *= invSum;
			w1 *= invSum;
			w2 *= invSum;
			w3 *= invSum;
		}
		else
		{
			w0 = 1.0f;
			w1 = 0.0f;
			w2 = 0.0f;
			w3 = 0.0f;
		}

		return (w0, w1, w2, w3);
	}

	private TerrainSplatWeights BlendSplatWeights(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2, TerrainSplatWeights s3)
	{
		var (tex0, tex1, tex2, tex3) = GetQuadDominantTextures(s0, s1, s2, s3);
		var (w00, w01, w02, w03) = GetSplatWeightsForQuad(s0, tex0, tex1, tex2, tex3);
		var (w10, w11, w12, w13) = GetSplatWeightsForQuad(s1, tex0, tex1, tex2, tex3);
		var (w20, w21, w22, w23) = GetSplatWeightsForQuad(s2, tex0, tex1, tex2, tex3);
		var (w30, w31, w32, w33) = GetSplatWeightsForQuad(s3, tex0, tex1, tex2, tex3);

		float avg0 = (w00 + w10 + w20 + w30) * 0.25f;
		float avg1 = (w01 + w11 + w21 + w31) * 0.25f;
		float avg2 = (w02 + w12 + w22 + w32) * 0.25f;
		float avg3 = (w03 + w13 + w23 + w33) * 0.25f;

		float sum = avg0 + avg1 + avg2 + avg3;
		if (sum > 0.0001f)
		{
			float invSum = 1.0f / sum;
			avg0 *= invSum;
			avg1 *= invSum;
			avg2 *= invSum;
			avg3 *= invSum;
		}
		else
		{
			avg0 = 1.0f; avg1 = 0f; avg2 = 0f; avg3 = 0f;
		}

		return new TerrainSplatWeights
		{
			Index0 = tex0,
			Index1 = tex1,
			Index2 = tex2,
			Index3 = tex3,
			Weight0 = avg0,
			Weight1 = avg1,
			Weight2 = avg2,
			Weight3 = avg3
		};
	}

	private TerrainSplatWeights BlendSplatWeights(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2)
	{
		var (tex0, tex1, tex2, tex3) = GetQuadDominantTextures(s0, s1, s2, s0);
		var (w00, w01, w02, w03) = GetSplatWeightsForQuad(s0, tex0, tex1, tex2, tex3);
		var (w10, w11, w12, w13) = GetSplatWeightsForQuad(s1, tex0, tex1, tex2, tex3);
		var (w20, w21, w22, w23) = GetSplatWeightsForQuad(s2, tex0, tex1, tex2, tex3);

		float avg0 = (w00 + w10 + w20) / 3.0f;
		float avg1 = (w01 + w11 + w21) / 3.0f;
		float avg2 = (w02 + w12 + w22) / 3.0f;
		float avg3 = (w03 + w13 + w23) / 3.0f;

		float sum = avg0 + avg1 + avg2 + avg3;
		if (sum > 0.0001f)
		{
			float invSum = 1.0f / sum;
			avg0 *= invSum;
			avg1 *= invSum;
			avg2 *= invSum;
			avg3 *= invSum;
		}
		else
		{
			avg0 = 1.0f; avg1 = 0f; avg2 = 0f; avg3 = 0f;
		}

		return new TerrainSplatWeights
		{
			Index0 = tex0,
			Index1 = tex1,
			Index2 = tex2,
			Index3 = tex3,
			Weight0 = avg0,
			Weight1 = avg1,
			Weight2 = avg2,
			Weight3 = avg3
		};
	}

	public static float Smoothstep(float edge0, float edge1, float x)
	{
		float t = Mathf.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
		return t * t * (3.0f - 2.0f * t);
	}

	public static float GetCliffFactor(Vector3 normal)
	{
		float ny = Mathf.Clamp(normal.Y, 0.0f, 1.0f);
		return 1.0f - Smoothstep(0.70f, 0.90f, ny);
	}

	public static float GetVertexCliffWeight(int gx, int gz, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (cells == null || w <= 0 || d <= 0) return 0f;
		float h = GetGridNodeHeight(gx, gz, cells, w, d);
		float maxDelta = 0f;
		if (gx > 0) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx - 1, gz, cells, w, d)));
		if (gx < w) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx + 1, gz, cells, w, d)));
		if (gz > 0) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx, gz - 1, cells, w, d)));
		if (gz < d) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx, gz + 1, cells, w, d)));

		sbyte minTier = sbyte.MaxValue;
		sbyte maxTier = sbyte.MinValue;
		int cellCount = 0;

		void CheckCell(int cx, int cz)
		{
			if (cx >= 0 && cx < w && cz >= 0 && cz < d)
			{
				sbyte t = cells[cx, cz].MacroTier;
				if (t < minTier) minTier = t;
				if (t > maxTier) maxTier = t;
				cellCount++;
			}
		}

		CheckCell(gx - 1, gz - 1);
		CheckCell(gx, gz - 1);
		CheckCell(gx - 1, gz);
		CheckCell(gx, gz);

		float tierCliffDeltaThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		bool hasTierDifference = cellCount > 1 && (maxTier - minTier) >= 1;
		bool hasStepHeightDelta = maxDelta >= tierCliffDeltaThreshold;

		if (!hasTierDifference && !hasStepHeightDelta)
		{
			return 0.0f;
		}

		float deltaCliff = Smoothstep(TerrainCell.TIER_HEIGHT * 0.50f, TerrainCell.TIER_HEIGHT * 0.85f, maxDelta);
		if (hasTierDifference)
		{
			deltaCliff = Math.Max(deltaCliff, 1.0f);
		}

		return Math.Clamp(deltaCliff, 0.0f, 1.0f);
	}

	private void ProcessCellQuad(
		TerrainChunk chunk,
		int x, int z,
		TerrainCell[,] cells,
		int w, int d,
		float quadSize,
		float halfWQuadSize,
		float halfDQuadSize,
		TerrainSplatWeights[,] splatMap,
		TerrainSplatWeights[,] cliffSplatMap,
		ref int vertexIndex,
		ref int indexIndex)
	{
		var cell = cells[x, z];

		float hNW = cell.Y_NW;
		float hNE = cell.Y_NE;
		float hSE = cell.Y_SE;
		float hSW = cell.Y_SW;
		float hC = cell.CenterHeight;

		Vector3 gPNW = GetWorldPosition(x, z, hNW, halfWQuadSize, halfDQuadSize, quadSize);
		Vector3 gPNE = GetWorldPosition(x + 1, z, hNE, halfWQuadSize, halfDQuadSize, quadSize);
		Vector3 gPSE = GetWorldPosition(x + 1, z + 1, hSE, halfWQuadSize, halfDQuadSize, quadSize);
		Vector3 gPSW = GetWorldPosition(x, z + 1, hSW, halfWQuadSize, halfDQuadSize, quadSize);
		Vector3 gPC = GetWorldPosition(x + 0.5f, z + 0.5f, hC, halfWQuadSize, halfDQuadSize, quadSize);

		Vector3 normNW = GetVertexNormal(x, z, cells, w, d, quadSize);
		Vector3 normNE = GetVertexNormal(x + 1, z, cells, w, d, quadSize);
		Vector3 normSE = GetVertexNormal(x + 1, z + 1, cells, w, d, quadSize);
		Vector3 normSW = GetVertexNormal(x, z + 1, cells, w, d, quadSize);
		Vector3 normC = (normNW + normNE + normSE + normSW).Normalized();

		sbyte currentTier = cell.MacroTier;
		bool bordersDifferentTier = false;
		if (x > 0 && Math.Abs(cells[x - 1, z].MacroTier - currentTier) >= 1) bordersDifferentTier = true;
		if (x < w - 1 && Math.Abs(cells[x + 1, z].MacroTier - currentTier) >= 1) bordersDifferentTier = true;
		if (z > 0 && Math.Abs(cells[x, z - 1].MacroTier - currentTier) >= 1) bordersDifferentTier = true;
		if (z < d - 1 && Math.Abs(cells[x, z + 1].MacroTier - currentTier) >= 1) bordersDifferentTier = true;

		float cliffNW = GetVertexCliffWeight(x, z, cells, w, d, quadSize);
		float cliffNE = GetVertexCliffWeight(x + 1, z, cells, w, d, quadSize);
		float cliffSE = GetVertexCliffWeight(x + 1, z + 1, cells, w, d, quadSize);
		float cliffSW = GetVertexCliffWeight(x, z + 1, cells, w, d, quadSize);
		float cliffC = (cliffNW + cliffNE + cliffSE + cliffSW) * 0.25f;

		float maxDelta = Math.Max(
			Math.Max(Math.Abs(hNW - hNE), Math.Abs(hNE - hSE)),
			Math.Max(Math.Abs(hSE - hSW), Math.Abs(hSW - hNW))
		);
		float maxCenterDelta = Math.Max(
			Math.Max(Math.Abs(hC - hNW), Math.Abs(hC - hNE)),
			Math.Max(Math.Abs(hC - hSE), Math.Abs(hC - hSW))
		);
		float totalQuadDelta = Math.Max(maxDelta, maxCenterDelta);

		float tierStepThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		bool isCliffQuad = (bordersDifferentTier && totalQuadDelta >= tierStepThreshold * 0.5f) || totalQuadDelta >= tierStepThreshold;
		float quadCliffWeight = isCliffQuad ? 1.0f : 0.0f;

		int mapSplatW = splatMap != null ? splatMap.GetLength(0) : 0;
		int mapSplatD = splatMap != null ? splatMap.GetLength(1) : 0;

		TerrainSplatWeights floorS00 = splatMap != null ? splatMap[Math.Clamp(x, 0, mapSplatW - 1), Math.Clamp(z, 0, mapSplatD - 1)] : default;
		TerrainSplatWeights floorS10 = splatMap != null ? splatMap[Math.Clamp(x + 1, 0, mapSplatW - 1), Math.Clamp(z, 0, mapSplatD - 1)] : floorS00;
		TerrainSplatWeights floorS11 = splatMap != null ? splatMap[Math.Clamp(x + 1, 0, mapSplatW - 1), Math.Clamp(z + 1, 0, mapSplatD - 1)] : floorS00;
		TerrainSplatWeights floorS01 = splatMap != null ? splatMap[Math.Clamp(x, 0, mapSplatW - 1), Math.Clamp(z + 1, 0, mapSplatD - 1)] : floorS00;
		TerrainSplatWeights floorSC = BlendSplatWeights(floorS00, floorS10, floorS11, floorS01);

		int mapCliffW = cliffSplatMap != null ? cliffSplatMap.GetLength(0) : 0;
		int mapCliffD = cliffSplatMap != null ? cliffSplatMap.GetLength(1) : 0;

		TerrainSplatWeights cliffS00 = (cliffSplatMap != null && mapCliffW > 0 && mapCliffD > 0)
			? cliffSplatMap[Math.Clamp(x, 0, mapCliffW - 1), Math.Clamp(z, 0, mapCliffD - 1)]
			: default;
		TerrainSplatWeights cliffS10 = (cliffSplatMap != null && mapCliffW > 0 && mapCliffD > 0)
			? cliffSplatMap[Math.Clamp(x + 1, 0, mapCliffW - 1), Math.Clamp(z, 0, mapCliffD - 1)]
			: cliffS00;
		TerrainSplatWeights cliffS11 = (cliffSplatMap != null && mapCliffW > 0 && mapCliffD > 0)
			? cliffSplatMap[Math.Clamp(x + 1, 0, mapCliffW - 1), Math.Clamp(z + 1, 0, mapCliffD - 1)]
			: cliffS00;
		TerrainSplatWeights cliffS01 = (cliffSplatMap != null && mapCliffW > 0 && mapCliffD > 0)
			? cliffSplatMap[Math.Clamp(x, 0, mapCliffW - 1), Math.Clamp(z + 1, 0, mapCliffD - 1)]
			: cliffS00;
		TerrainSplatWeights cliffSC = BlendSplatWeights(cliffS00, cliffS10, cliffS11, cliffS01);

		Vector2 uvNW = new Vector2(gPNW.X, gPNW.Z);
		Vector2 uvNE = new Vector2(gPNE.X, gPNE.Z);
		Vector2 uvSE = new Vector2(gPSE.X, gPSE.Z);
		Vector2 uvSW = new Vector2(gPSW.X, gPSW.Z);
		Vector2 uvC = new Vector2(gPC.X, gPC.Z);

		var (gTex0, gTex1, gTex2, gTex3) = GetQuadDominantTextures(floorS00, floorS10, floorS11, floorS01);
		var (cTex0, cTex1, cTex2, cTex3) = GetQuadDominantTextures(cliffS00, cliffS10, cliffS11, cliffS01);

		ProcessSubTriangleGround(chunk, gPNW, gPNE, gPC, normNW, normNE, normC, uvNW, uvNE, uvC, cliffNW, cliffNE, cliffC, quadCliffWeight, floorS00, floorS10, floorSC, gTex0, gTex1, gTex2, gTex3, cliffS00, cliffS10, cliffSC, cTex0, cTex1, cTex2, cTex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, gPNE, gPSE, gPC, normNE, normSE, normC, uvNE, uvSE, uvC, cliffNE, cliffSE, cliffC, quadCliffWeight, floorS10, floorS11, floorSC, gTex0, gTex1, gTex2, gTex3, cliffS10, cliffS11, cliffSC, cTex0, cTex1, cTex2, cTex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, gPSE, gPSW, gPC, normSE, normSW, normC, uvSE, uvSW, uvC, cliffSE, cliffSW, cliffC, quadCliffWeight, floorS11, floorS01, floorSC, gTex0, gTex1, gTex2, gTex3, cliffS11, cliffS01, cliffSC, cTex0, cTex1, cTex2, cTex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, gPSW, gPNW, gPC, normSW, normNW, normC, uvSW, uvNW, uvC, cliffSW, cliffNW, cliffC, quadCliffWeight, floorS01, floorS00, floorSC, gTex0, gTex1, gTex2, gTex3, cliffS01, cliffS00, cliffSC, cTex0, cTex1, cTex2, cTex3, ref vertexIndex, ref indexIndex);
	}

	private void ProcessSubTriangleGround(
		TerrainChunk chunk,
		Vector3 pos0, Vector3 pos1, Vector3 pos2,
		Vector3 norm0, Vector3 norm1, Vector3 norm2,
		Vector2 uv0, Vector2 uv1, Vector2 uv2,
		float cliff0, float cliff1, float cliff2,
		float quadCliff,
		TerrainSplatWeights gS0, TerrainSplatWeights gS1, TerrainSplatWeights gS2,
		int gTex0, int gTex1, int gTex2, int gTex3,
		TerrainSplatWeights cS0, TerrainSplatWeights cS1, TerrainSplatWeights cS2,
		int cTex0, int cTex1, int cTex2, int cTex3,
		ref int vertexIndex,
		ref int indexIndex)
	{
		int baseIndex = vertexIndex;

		var (gw00, gw01, gw02, gw03) = GetSplatWeightsForQuad(gS0, gTex0, gTex1, gTex2, gTex3);
		var (gw10, gw11, gw12, gw13) = GetSplatWeightsForQuad(gS1, gTex0, gTex1, gTex2, gTex3);
		var (gw20, gw21, gw22, gw23) = GetSplatWeightsForQuad(gS2, gTex0, gTex1, gTex2, gTex3);

		var (cw00, cw01, cw02, cw03) = GetSplatWeightsForQuad(cS0, cTex0, cTex1, cTex2, cTex3);
		var (cw10, cw11, cw12, cw13) = GetSplatWeightsForQuad(cS1, cTex0, cTex1, cTex2, cTex3);
		var (cw20, cw21, cw22, cw23) = GetSplatWeightsForQuad(cS2, cTex0, cTex1, cTex2, cTex3);

		PopulateExplicitVertex(chunk, pos0, uv0, norm0, cliff0, quadCliff, gTex0, gTex1, gTex2, gTex3, gw00, gw01, gw02, gw03, cTex0, cTex1, cTex2, cTex3, cw00, cw01, cw02, cw03, ref vertexIndex);
		PopulateExplicitVertex(chunk, pos1, uv1, norm1, cliff1, quadCliff, gTex0, gTex1, gTex2, gTex3, gw10, gw11, gw12, gw13, cTex0, cTex1, cTex2, cTex3, cw10, cw11, cw12, cw13, ref vertexIndex);
		PopulateExplicitVertex(chunk, pos2, uv2, norm2, cliff2, quadCliff, gTex0, gTex1, gTex2, gTex3, gw20, gw21, gw22, gw23, cTex0, cTex1, cTex2, cTex3, cw20, cw21, cw22, cw23, ref vertexIndex);

		chunk.IndicesCache[indexIndex++] = baseIndex;
		chunk.IndicesCache[indexIndex++] = baseIndex + 1;
		chunk.IndicesCache[indexIndex++] = baseIndex + 2;
	}

	private void PopulateExplicitVertex(
		TerrainChunk chunk,
		Vector3 position,
		Vector2 uv,
		Vector3 faceNormal,
		float cliffWeight,
		float quadCliffWeight,
		int gTex0, int gTex1, int gTex2, int gTex3,
		float gw0, float gw1, float gw2, float gw3,
		int cTex0, int cTex1, int cTex2, int cTex3,
		float cw0, float cw1, float cw2, float cw3,
		ref int vertexIndex)
	{
		chunk.VerticesCache[vertexIndex] = position;
		chunk.NormalsCache[vertexIndex] = faceNormal;
		chunk.UvsCache[vertexIndex] = uv;
		chunk.ColorsCache[vertexIndex] = new Color(quadCliffWeight, 1.0f, 1.0f, cliffWeight);

		int sIdx = vertexIndex * 4;
		chunk.TexIndicesCache[sIdx + 0] = gTex0;
		chunk.TexIndicesCache[sIdx + 1] = gTex1;
		chunk.TexIndicesCache[sIdx + 2] = gTex2;
		chunk.TexIndicesCache[sIdx + 3] = gTex3;

		chunk.TexWeightsCache01[sIdx + 0] = gw0 > 0.0001f ? gw0 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 1] = gw1 > 0.0001f ? gw1 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 2] = gw2 > 0.0001f ? gw2 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 3] = gw3 > 0.0001f ? gw3 : 0.0f;

		chunk.CliffIndicesCache[sIdx + 0] = cTex0;
		chunk.CliffIndicesCache[sIdx + 1] = cTex1;
		chunk.CliffIndicesCache[sIdx + 2] = cTex2;
		chunk.CliffIndicesCache[sIdx + 3] = cTex3;

		chunk.CliffWeightsCache[sIdx + 0] = cw0 > 0.0001f ? cw0 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 1] = cw1 > 0.0001f ? cw1 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 2] = cw2 > 0.0001f ? cw2 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 3] = cw3 > 0.0001f ? cw3 : 0.0f;

		vertexIndex++;
	}

	private void WeldChunkVertices(TerrainChunk chunk, ref int vertexIndex, int indexIndex)
	{
		if (vertexIndex == 0)
		{
			return;
		}

		if (chunk.WeldVertexMap == null)
		{
			chunk.WeldVertexMap = new Dictionary<TerrainVertexKey, int>(vertexIndex);
		}
		else
		{
			chunk.WeldVertexMap.Clear();
		}

		if (chunk.WeldRemapTable == null || chunk.WeldRemapTable.Length < vertexIndex)
		{
			chunk.WeldRemapTable = new int[Math.Max(vertexIndex, chunk.WeldRemapTable != null ? chunk.WeldRemapTable.Length * 2 : 1024)];
		}

		var vertexMap = chunk.WeldVertexMap;
		var remapTable = chunk.WeldRemapTable;
		int uniqueVertexCount = 0;

		for (int v = 0; v < vertexIndex; v++)
		{
			int texIdx = v * 4;
			TerrainVertexKey key = new TerrainVertexKey(
				chunk.VerticesCache[v],
				chunk.NormalsCache[v],
				chunk.UvsCache[v],
				chunk.ColorsCache[v],
				chunk.TexIndicesCache[texIdx],
				chunk.TexIndicesCache[texIdx + 1],
				chunk.TexIndicesCache[texIdx + 2],
				chunk.TexIndicesCache[texIdx + 3],
				chunk.TexWeightsCache01[texIdx],
				chunk.TexWeightsCache01[texIdx + 1],
				chunk.TexWeightsCache01[texIdx + 2],
				chunk.TexWeightsCache01[texIdx + 3],
				chunk.CliffIndicesCache[texIdx],
				chunk.CliffIndicesCache[texIdx + 1],
				chunk.CliffIndicesCache[texIdx + 2],
				chunk.CliffIndicesCache[texIdx + 3],
				chunk.CliffWeightsCache[texIdx],
				chunk.CliffWeightsCache[texIdx + 1],
				chunk.CliffWeightsCache[texIdx + 2],
				chunk.CliffWeightsCache[texIdx + 3]
			);

			if (vertexMap.TryGetValue(key, out int existingIndex))
			{
				remapTable[v] = existingIndex;
			}
			else
			{
				int newIndex = uniqueVertexCount++;
				vertexMap[key] = newIndex;
				remapTable[v] = newIndex;

				if (newIndex != v)
				{
					chunk.VerticesCache[newIndex] = chunk.VerticesCache[v];
					chunk.NormalsCache[newIndex] = chunk.NormalsCache[v];
					chunk.UvsCache[newIndex] = chunk.UvsCache[v];
					chunk.ColorsCache[newIndex] = chunk.ColorsCache[v];

					int newTexIdx = newIndex * 4;
					chunk.TexIndicesCache[newTexIdx] = chunk.TexIndicesCache[texIdx];
					chunk.TexIndicesCache[newTexIdx + 1] = chunk.TexIndicesCache[texIdx + 1];
					chunk.TexIndicesCache[newTexIdx + 2] = chunk.TexIndicesCache[texIdx + 2];
					chunk.TexIndicesCache[newTexIdx + 3] = chunk.TexIndicesCache[texIdx + 3];

					chunk.TexWeightsCache01[newTexIdx] = chunk.TexWeightsCache01[texIdx];
					chunk.TexWeightsCache01[newTexIdx + 1] = chunk.TexWeightsCache01[texIdx + 1];
					chunk.TexWeightsCache01[newTexIdx + 2] = chunk.TexWeightsCache01[texIdx + 2];
					chunk.TexWeightsCache01[newTexIdx + 3] = chunk.TexWeightsCache01[texIdx + 3];

					chunk.CliffIndicesCache[newTexIdx] = chunk.CliffIndicesCache[texIdx];
					chunk.CliffIndicesCache[newTexIdx + 1] = chunk.CliffIndicesCache[texIdx + 1];
					chunk.CliffIndicesCache[newTexIdx + 2] = chunk.CliffIndicesCache[texIdx + 2];
					chunk.CliffIndicesCache[newTexIdx + 3] = chunk.CliffIndicesCache[texIdx + 3];

					chunk.CliffWeightsCache[newTexIdx] = chunk.CliffWeightsCache[texIdx];
					chunk.CliffWeightsCache[newTexIdx + 1] = chunk.CliffWeightsCache[texIdx + 1];
					chunk.CliffWeightsCache[newTexIdx + 2] = chunk.CliffWeightsCache[texIdx + 2];
					chunk.CliffWeightsCache[newTexIdx + 3] = chunk.CliffWeightsCache[texIdx + 3];
				}
			}
		}

		for (int i = 0; i < indexIndex; i++)
		{
			chunk.IndicesCache[i] = remapTable[chunk.IndicesCache[i]];
		}

		vertexIndex = uniqueVertexCount;
	}

	public void BakeNavMesh()
	{
		var world = GameHost.Instance?.EcsWorld;
		var worldEntity = GameHost.Instance != null ? GameHost.Instance.WorldEntity : default;
		if (world != null && world.IsAlive(worldEntity) && world.Has<TerrainState>(worldEntity))
		{
			ref var state = ref world.Get<TerrainState>(worldEntity);
			var terrainNavMeshService = ServiceLocator.Get<Realm.Ecs.Services.TerrainNavMeshService>();
			terrainNavMeshService.BakeNavMesh(ref state);
			world.Set(worldEntity, state);
		}
	}

	public void GetHeightAndNormal(float worldX, float worldZ, out float height, out Vector3 normal)
	{
		var cells = Cells;
		if (cells == null)
		{
			height = 0.0f;
			normal = Vector3.Up;
			return;
		}

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0)
		{
			height = 0.0f;
			normal = Vector3.Up;
			return;
		}

		float halfW = (cellW / 2.0f) * QuadSize;
		float halfD = (cellD / 2.0f) * QuadSize;

		float localX = (worldX + halfW) / QuadSize;
		float localZ = (worldZ + halfD) / QuadSize;

		int x0 = Math.Clamp((int)MathF.Floor(localX), 0, cellW - 1);
		int z0 = Math.Clamp((int)MathF.Floor(localZ), 0, cellD - 1);

		float tx = Math.Clamp(localX - x0, 0.0f, 1.0f);
		float tz = Math.Clamp(localZ - z0, 0.0f, 1.0f);

		var cell = cells[x0, z0];
		float h00 = cell.Y_NW;
		float h10 = cell.Y_NE;
		float h01 = cell.Y_SW;
		float h11 = cell.Y_SE;
		height = (1 - tx) * (1 - tz) * h00 + tx * (1 - tz) * h10 + (1 - tx) * tz * h01 + tx * tz * h11;
		Vector3 n00 = GetVertexNormal(x0, z0);
		Vector3 n10 = GetVertexNormal(x0 + 1, z0);
		Vector3 n01 = GetVertexNormal(x0, z0 + 1);
		Vector3 n11 = GetVertexNormal(x0 + 1, z0 + 1);
		normal = ((1 - tx) * (1 - tz) * n00 + tx * (1 - tz) * n10 + (1 - tx) * tz * n01 + tx * tz * n11).Normalized();
	}

	public static Vector3 GetVertexNormal(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (cells == null || w <= 0 || d <= 0) return Vector3.Up;

		float dx;
		if (x > 0 && x < w)
		{
			dx = (GetGridNodeHeight(x + 1, z, cells, w, d) - GetGridNodeHeight(x - 1, z, cells, w, d)) / (2.0f * quadSize);
		}
		else if (x < w)
		{
			dx = (GetGridNodeHeight(x + 1, z, cells, w, d) - GetGridNodeHeight(x, z, cells, w, d)) / quadSize;
		}
		else if (x > 0)
		{
			dx = (GetGridNodeHeight(x, z, cells, w, d) - GetGridNodeHeight(x - 1, z, cells, w, d)) / quadSize;
		}
		else
		{
			dx = 0.0f;
		}

		float dz;
		if (z > 0 && z < d)
		{
			dz = (GetGridNodeHeight(x, z + 1, cells, w, d) - GetGridNodeHeight(x, z - 1, cells, w, d)) / (2.0f * quadSize);
		}
		else if (z < d)
		{
			dz = (GetGridNodeHeight(x, z + 1, cells, w, d) - GetGridNodeHeight(x, z, cells, w, d)) / quadSize;
		}
		else if (z > 0)
		{
			dz = (GetGridNodeHeight(x, z, cells, w, d) - GetGridNodeHeight(x, z - 1, cells, w, d)) / quadSize;
		}
		else
		{
			dz = 0.0f;
		}

		if (Math.Abs(dx) < 0.0001f && Math.Abs(dz) < 0.0001f)
		{
			return Vector3.Up;
		}

		Vector3 tangentX = new Vector3(quadSize, dx * quadSize, 0.0f).Normalized();
		Vector3 tangentZ = new Vector3(0.0f, dz * quadSize, quadSize).Normalized();
		return tangentZ.Cross(tangentX).Normalized();
	}

	public Vector3 GetVertexNormal(int x, int z)
	{
		return GetVertexNormal(x, z, Cells, Width, Depth, QuadSize);
	}

	public readonly struct TerrainVertexKey : IEquatable<TerrainVertexKey>
	{
		private readonly int _positionX;
		private readonly int _positionY;
		private readonly int _positionZ;
		private readonly int _normalX;
		private readonly int _normalY;
		private readonly int _normalZ;
		private readonly int _textureU;
		private readonly int _textureV;
		private readonly int _colorAlpha;
		private readonly int _quadCliff;
		private readonly int _gTex0;
		private readonly int _gTex1;
		private readonly int _gTex2;
		private readonly int _gTex3;
		private readonly int _gw0;
		private readonly int _gw1;
		private readonly int _gw2;
		private readonly int _gw3;
		private readonly int _cTex0;
		private readonly int _cTex1;
		private readonly int _cTex2;
		private readonly int _cTex3;
		private readonly int _cw0;
		private readonly int _cw1;
		private readonly int _cw2;
		private readonly int _cw3;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int FastRound(float v, float scale) => (int)(v * scale + (v >= 0f ? 0.5f : -0.5f));

		public TerrainVertexKey(
			Vector3 position,
			Vector3 normal,
			Vector2 uv,
			Color color,
			float gTex0, float gTex1, float gTex2, float gTex3,
			float gw0, float gw1, float gw2, float gw3,
			float cTex0, float cTex1, float cTex2, float cTex3,
			float cw0, float cw1, float cw2, float cw3)
		{
			_positionX = FastRound(position.X, 2000.0f);
			_positionY = FastRound(position.Y, 2000.0f);
			_positionZ = FastRound(position.Z, 2000.0f);
			_normalX = FastRound(normal.X, 1000.0f);
			_normalY = FastRound(normal.Y, 1000.0f);
			_normalZ = FastRound(normal.Z, 1000.0f);
			_textureU = FastRound(uv.X, 1000.0f);
			_textureV = FastRound(uv.Y, 1000.0f);
			_colorAlpha = FastRound(color.A, 1000.0f);
			_quadCliff = (int)(color.R + (color.R >= 0f ? 0.5f : -0.5f));
			_gTex0 = (int)(gTex0 + (gTex0 >= 0f ? 0.5f : -0.5f));
			_gTex1 = (int)(gTex1 + (gTex1 >= 0f ? 0.5f : -0.5f));
			_gTex2 = (int)(gTex2 + (gTex2 >= 0f ? 0.5f : -0.5f));
			_gTex3 = (int)(gTex3 + (gTex3 >= 0f ? 0.5f : -0.5f));
			_gw0 = FastRound(gw0, 1000.0f);
			_gw1 = FastRound(gw1, 1000.0f);
			_gw2 = FastRound(gw2, 1000.0f);
			_gw3 = FastRound(gw3, 1000.0f);
			_cTex0 = (int)(cTex0 + (cTex0 >= 0f ? 0.5f : -0.5f));
			_cTex1 = (int)(cTex1 + (cTex1 >= 0f ? 0.5f : -0.5f));
			_cTex2 = (int)(cTex2 + (cTex2 >= 0f ? 0.5f : -0.5f));
			_cTex3 = (int)(cTex3 + (cTex3 >= 0f ? 0.5f : -0.5f));
			_cw0 = FastRound(cw0, 1000.0f);
			_cw1 = FastRound(cw1, 1000.0f);
			_cw2 = FastRound(cw2, 1000.0f);
			_cw3 = FastRound(cw3, 1000.0f);
		}

		public bool Equals(TerrainVertexKey other)
		{
			return _positionX == other._positionX &&
				   _positionY == other._positionY &&
				   _positionZ == other._positionZ &&
				   _normalX == other._normalX &&
				   _normalY == other._normalY &&
				   _normalZ == other._normalZ &&
				   _textureU == other._textureU &&
				   _textureV == other._textureV &&
				   _colorAlpha == other._colorAlpha &&
				   _quadCliff == other._quadCliff &&
				   _gTex0 == other._gTex0 &&
				   _gTex1 == other._gTex1 &&
				   _gTex2 == other._gTex2 &&
				   _gTex3 == other._gTex3 &&
				   _gw0 == other._gw0 &&
				   _gw1 == other._gw1 &&
				   _gw2 == other._gw2 &&
				   _gw3 == other._gw3 &&
				   _cTex0 == other._cTex0 &&
				   _cTex1 == other._cTex1 &&
				   _cTex2 == other._cTex2 &&
				   _cTex3 == other._cTex3 &&
				   _cw0 == other._cw0 &&
				   _cw1 == other._cw1 &&
				   _cw2 == other._cw2 &&
				   _cw3 == other._cw3;
		}

		public override bool Equals(object? obj)
		{
			return obj is TerrainVertexKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + _positionX;
				hash = hash * 31 + _positionY;
				hash = hash * 31 + _positionZ;
				hash = hash * 31 + _normalX;
				hash = hash * 31 + _normalY;
				hash = hash * 31 + _normalZ;
				hash = hash * 31 + _textureU;
				hash = hash * 31 + _textureV;
				hash = hash * 31 + _colorAlpha;
				hash = hash * 31 + _quadCliff;
				hash = hash * 31 + _gTex0;
				hash = hash * 31 + _gw0;
				hash = hash * 31 + _cTex0;
				hash = hash * 31 + _cw0;
				return hash;
			}
		}
	}
}
