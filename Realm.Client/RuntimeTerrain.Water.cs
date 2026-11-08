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
public partial class RuntimeTerrain
{
	public const int CHUNK_SIZE = 16;
	protected int _chunkedWidth = 0;
	protected int _chunkedDepth = 0;


	protected List<TerrainChunk> _chunks = new List<TerrainChunk>();
	public IReadOnlyList<TerrainChunk> Chunks => _chunks;
	protected ShaderMaterial _material = null!;
	public ShaderMaterial Material => _material;
	protected ShaderMaterial _shallowWaterMaterial = null!;
	protected ShaderMaterial _deepWaterMaterial = null!;
	protected System.Collections.Generic.Dictionary<byte, WaterProfileSaveData> _waterProfiles = new();
	protected System.Collections.Generic.Dictionary<byte, ShaderMaterial> _waterMaterials = new();
	protected Shader _waterUberShader = null!;

	public System.Collections.Generic.Dictionary<byte, WaterProfileSaveData> WaterProfiles => _waterProfiles;
	public System.Collections.Generic.Dictionary<byte, ShaderMaterial> WaterMaterials => _waterMaterials;

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

	public void EnsureWaterUberShader()
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
	}

	public ShaderMaterial GetWaterMaterial(byte profileIndex)
	{
		EnsureWaterUberShader();
		if (_waterProfiles.Count == 0)
		{
			ReloadWaterProfiles();
		}

		if (_waterMaterials.TryGetValue(profileIndex, out var mat) && mat != null) return mat;
		var newMat = new ShaderMaterial { Shader = _waterUberShader };
		var prof = GetWaterProfile(profileIndex);
		ApplyWaterProfileToMaterial(newMat, prof);
		_waterMaterials[profileIndex] = newMat;
		return newMat;
	}

	public void SetWaterProfiles(List<WaterProfileSaveData> profiles)
	{
		EnsureWaterUberShader();
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

		_shallowWaterMaterial = GetWaterMaterial(0);
		_deepWaterMaterial = GetWaterMaterial(1);
		UpdateWaterTransform();
		RegenerateWaterMesh();
	}

	public void ApplyWaterProfileToMaterial(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		if (mat == null || profile == null) return;

		ApplyWaterColors(mat, profile);
		ApplyWaterBaseParameters(mat, profile);
		ApplyWaterTextures(mat, profile);
		ApplyWaterFlowAndCaustics(mat, profile);
		ApplyWaterSubsurface(mat, profile);
		ApplyWaterDetail(mat, profile);
	}

	private void ApplyWaterColors(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		Color shallow = Color.HtmlIsValid(profile.ShallowColorHex) ? Color.FromHtml(profile.ShallowColorHex) : new Color(0.05f, 0.30f, 0.38f, 0.55f);
		Color deep = Color.HtmlIsValid(profile.DeepColorHex) ? Color.FromHtml(profile.DeepColorHex) : new Color(0.01f, 0.06f, 0.14f, 0.98f);
		Color foam = Color.HtmlIsValid(profile.FoamColorHex) ? Color.FromHtml(profile.FoamColorHex) : new Color(0.85f, 0.95f, 1.0f, 0.85f);

		mat.SetShaderParameter("shallow_color", shallow);
		mat.SetShaderParameter("deep_color", deep);
		mat.SetShaderParameter("foam_color", foam);
	}

	private void ApplyWaterBaseParameters(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		mat.SetShaderParameter("max_depth", profile.MaxDepth);
		mat.SetShaderParameter("foam_depth", profile.FoamDepth);
		mat.SetShaderParameter("wave_speed", profile.WaveSpeed);
		mat.SetShaderParameter("wave_strength", profile.WaveStrength);
	}

	private void ApplyWaterTextures(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		mat.SetShaderParameter("use_normal_texture", profile.UseNormalTexture && !string.IsNullOrEmpty(profile.NormalTexturePath));
		if (profile.UseNormalTexture && !string.IsNullOrEmpty(profile.NormalTexturePath))
		{
			var tex = LoadTextureFromActiveWorkspace(profile.NormalTexturePath, preferNormal: true);
			if (tex != null) mat.SetShaderParameter("normal_texture", tex);
		}

		mat.SetShaderParameter("normal_scale", profile.NormalScale);
	}

	private void ApplyWaterFlowAndCaustics(ShaderMaterial mat, WaterProfileSaveData profile)
	{
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
	}

	private void ApplyWaterSubsurface(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		Color emission = Color.HtmlIsValid(profile.EmissionColorHex) ? Color.FromHtml(profile.EmissionColorHex) : Colors.Black;
		Color core = Color.HtmlIsValid(profile.CoreColorHex) ? Color.FromHtml(profile.CoreColorHex) : Colors.White;
		Color sss = Color.HtmlIsValid(profile.SubsurfaceColorHex) ? Color.FromHtml(profile.SubsurfaceColorHex) : Colors.Black;

		mat.SetShaderParameter("emission_color", emission);
		mat.SetShaderParameter("emission_boost", profile.EmissionBoost);
		mat.SetShaderParameter("core_color", core);
		mat.SetShaderParameter("core_threshold", profile.CoreThreshold);
		mat.SetShaderParameter("subsurface_color", sss);
		mat.SetShaderParameter("subsurface_strength", profile.SubsurfaceStrength);
	}

	private void ApplyWaterDetail(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		mat.SetShaderParameter("use_detail_texture", profile.UseDetailTexture && !string.IsNullOrEmpty(profile.DetailTexturePath));
		if (profile.UseDetailTexture && !string.IsNullOrEmpty(profile.DetailTexturePath))
		{
			var dtex = LoadTextureFromActiveWorkspace(profile.DetailTexturePath, preferNormal: false);
			if (dtex != null) mat.SetShaderParameter("detail_texture", dtex);
		}

		mat.SetShaderParameter("detail_tile_mode", string.Equals(profile.DetailTileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
		mat.SetShaderParameter("detail_uv_scale", new Vector2(profile.DetailUvScaleX, profile.DetailUvScaleY));
		mat.SetShaderParameter("detail_uv_scroll", new Vector2(profile.DetailUvScrollX, profile.DetailUvScrollY));
		mat.SetShaderParameter("detail_stochastic_tile_size", profile.DetailStochasticTileSize > 0.001f ? profile.DetailStochasticTileSize : 1.0f);
		mat.SetShaderParameter("detail_cross_fade", Math.Clamp(profile.DetailCrossFade * 0.01f, 0.0f, 0.10f));
		mat.SetShaderParameter("detail_alpha", profile.DetailAlpha);
		mat.SetShaderParameter("detail_blend_mode", profile.DetailBlendMode);
	}

	private Texture2D? LoadTextureFromActiveWorkspace(string texturePath, bool preferNormal = false)
	{
		if (string.IsNullOrWhiteSpace(texturePath)) return null;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string? fullPath = FindTexturePath(wsPath, texturePath);

		if (fullPath == null || !File.Exists(fullPath)) return null;

		if (fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			var (alb, norm) = LoadRtexLayers(fullPath);
			var targetImg = (preferNormal && norm != null) ? norm : alb;
			return targetImg != null ? ImageTexture.CreateFromImage(targetImg) : null;
		}

		var img = Image.LoadFromFile(fullPath);
		return img != null ? ImageTexture.CreateFromImage(img) : null;
	}

	private string? FindTexturePath(string wsPath, string texturePath)
	{
		string fullPath = Path.Combine(wsPath, "Assets", "textures", texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "noise", texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "decals", texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "ribbons", texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "vfx", texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = PathUtils.FindPath($"Assets/textures/{texturePath}");
		if (File.Exists(fullPath)) return fullPath;

		fullPath = PathUtils.FindPath($"Assets/noise/{texturePath}");
		if (File.Exists(fullPath)) return fullPath;

		fullPath = PathUtils.FindPath(texturePath);
		if (File.Exists(fullPath)) return fullPath;

		return null;
	}

	public void ReloadWaterProfiles()
	{
		EnsureWaterUberShader();

		_waterProfiles.Clear();
		foreach (var def in WaterProfileSaveData.CreateDefaultProfiles())
		{
			_waterProfiles[def.ProfileIndex] = def;
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!string.IsNullOrEmpty(wsPath) && GameContext.MetadataService.TryLoadMetadata(wsPath, out var metadataRoot) && metadataRoot?.CustomWaterProfiles != null)
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
			float delta = cell.WaterHeight > 0.001f ? cell.WaterHeight : WATER_DELTA;
			return ((cell.MacroTier * TerrainCell.TIER_HEIGHT) + delta, cell.WaterMode, cell.WaterProfileIndex);
		}

		float maxCornerH = Math.Max(Math.Max(cell.Y_NW, cell.Y_NE), Math.Max(cell.Y_SE, cell.Y_SW));
		return FindAdjacentWaterInfo(x, z, cell, maxCornerH, cells, w, d);
	}

	private (float waterY, WaterType waterMode, byte waterProfileIndex) FindAdjacentWaterInfo(int x, int z, TerrainCell cell, float maxCornerH, TerrainCell[,] cells, int w, int d)
	{
		float bestWaterY = float.MaxValue;
		WaterType bestWaterMode = WaterType.None;
		byte bestProfileIndex = 0;

		for (int dz = -1; dz <= 1; dz++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				EvaluateAdjacentWaterCell(x, z, dx, dz, cell, maxCornerH, cells, w, d, ref bestWaterY, ref bestWaterMode, ref bestProfileIndex);
			}
		}

		return bestWaterMode != WaterType.None ? (bestWaterY, bestWaterMode, bestProfileIndex) : (0f, WaterType.None, (byte)0);
	}

	private void EvaluateAdjacentWaterCell(int x, int z, int dx, int dz, TerrainCell cell, float maxCornerH, TerrainCell[,] cells, int w, int d, ref float bestWaterY, ref WaterType bestWaterMode, ref byte bestProfileIndex)
	{
		if (dx == 0 && dz == 0) return;

		int nx = x + dx;
		int nz = z + dz;
		if (nx < 0 || nx >= w || nz < 0 || nz >= d) return;

		var nCell = cells[nx, nz];
		if (nCell.WaterMode == WaterType.None) return;

		float nWaterY = CalculateNeighborWaterY(nCell);
		float sharedMinH = GetSharedMinHeight(cell, dx, dz);

		if (ShouldSkipWaterEvaluation(sharedMinH, maxCornerH, nWaterY, bestWaterY)) return;

		bestWaterY = nWaterY;
		bestWaterMode = nCell.WaterMode;
		bestProfileIndex = nCell.WaterProfileIndex;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float CalculateNeighborWaterY(TerrainCell nCell)
	{
		float nDelta = nCell.WaterHeight > 0.001f ? nCell.WaterHeight : WATER_DELTA;
		return (nCell.MacroTier * TerrainCell.TIER_HEIGHT) + nDelta;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool ShouldSkipWaterEvaluation(float sharedMinH, float maxCornerH, float nWaterY, float bestWaterY)
	{
		return sharedMinH > nWaterY || maxCornerH <= nWaterY || nWaterY >= bestWaterY;
	}

	private float GetSharedMinHeight(TerrainCell cell, int dx, int dz)
	{
		return (dx, dz) switch
		{
			(-1, 0) => Math.Min(cell.Y_NW, cell.Y_SW),
			(1, 0) => Math.Min(cell.Y_NE, cell.Y_SE),
			(0, -1) => Math.Min(cell.Y_NW, cell.Y_NE),
			(0, 1) => Math.Min(cell.Y_SW, cell.Y_SE),
			(-1, -1) => cell.Y_NW,
			(1, -1) => cell.Y_NE,
			(1, 1) => cell.Y_SE,
			_ => cell.Y_SW,
		};
	}

	public void RegenerateWaterMesh()
	{
		EnsureWaterUberShader();
		UpdateWaterTransform();

		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;
		float quadSize = QuadSize;
		float halfWQuadSize = (w / 2.0f) * quadSize;
		float halfDQuadSize = (d / 2.0f) * quadSize;

		var profileVerts = new System.Collections.Generic.Dictionary<byte, List<Vector3>>();
		var profileNorms = new System.Collections.Generic.Dictionary<byte, List<Vector3>>();
		var profileUvs = new System.Collections.Generic.Dictionary<byte, List<Vector2>>();
		var profileIndices = new System.Collections.Generic.Dictionary<byte, List<int>>();

		foreach (var chunk in _chunks)
		{
			profileVerts.Clear();
			profileNorms.Clear();
			profileUvs.Clear();
			profileIndices.Clear();

			GenerateWaterGeometryForChunk(chunk, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, profileVerts, profileNorms, profileUvs, profileIndices);
			UpdateWaterMeshesForChunk(chunk, profileVerts, profileNorms, profileUvs, profileIndices);
		}
	}

	private void GenerateWaterGeometryForChunk(
		TerrainChunk chunk, TerrainCell[,] cells, int w, int d, float quadSize, float halfWQuadSize, float halfDQuadSize, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				ProcessWaterCell(x, z, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, profileVerts, profileNorms, profileUvs, profileIndices);
			}
		}
	}

	private void ProcessWaterCell(
		int x, int z, TerrainCell[,] cells, int w, int d, float quadSize, float halfWQuadSize, float halfDQuadSize, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
		var (waterY1, activeWaterMode1, activeWaterProfile1) = GetCellWaterInfo(x, z, cells, w, d);
		if (activeWaterMode1 == WaterType.None) return;

		AddWaterQuad(x, z, waterY1, activeWaterProfile1, quadSize, halfWQuadSize, halfDQuadSize, profileVerts, profileNorms, profileUvs, profileIndices);

		if (x + 1 < w)
		{
			AddWaterWall(x, z, waterY1, activeWaterProfile1, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, profileVerts, profileNorms, profileUvs, profileIndices, true);
		}

		if (z + 1 < d)
		{
			AddWaterWall(x, z, waterY1, activeWaterProfile1, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, profileVerts, profileNorms, profileUvs, profileIndices, false);
		}
	}

	private void AddWaterQuad(
		int x, int z, float waterY1, byte activeWaterProfile1, float quadSize, float halfWQuadSize, float halfDQuadSize, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
		Vector3 nw = new Vector3(x * quadSize - halfWQuadSize, waterY1, z * quadSize - halfDQuadSize);
		Vector3 ne = new Vector3((x + 1) * quadSize - halfWQuadSize, waterY1, z * quadSize - halfDQuadSize);
		Vector3 se = new Vector3((x + 1) * quadSize - halfWQuadSize, waterY1, (z + 1) * quadSize - halfDQuadSize);
		Vector3 sw = new Vector3(x * quadSize - halfWQuadSize, waterY1, (z + 1) * quadSize - halfDQuadSize);

		var geom = GetGeomLists(activeWaterProfile1, profileVerts, profileNorms, profileUvs, profileIndices);
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
	}

	private void AddWallGeometry(
		System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<Vector3> norms, 
		System.Collections.Generic.List<Vector2> uvs, System.Collections.Generic.List<int> indices,
		int wBase, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 wallNorm)
	{
		verts.Add(v0);
		verts.Add(v1);
		verts.Add(v2);
		verts.Add(v3);

		norms.Add(wallNorm);
		norms.Add(wallNorm);
		norms.Add(wallNorm);
		norms.Add(wallNorm);

		uvs.Add(new Vector2(0, 0));
		uvs.Add(new Vector2(1, 0));
		uvs.Add(new Vector2(1, 1));
		uvs.Add(new Vector2(0, 1));

		indices.Add(wBase + 0);
		indices.Add(wBase + 1);
		indices.Add(wBase + 2);
		indices.Add(wBase + 0);
		indices.Add(wBase + 2);
		indices.Add(wBase + 3);
		indices.Add(wBase + 0);
		indices.Add(wBase + 2);
		indices.Add(wBase + 1);
		indices.Add(wBase + 0);
		indices.Add(wBase + 3);
		indices.Add(wBase + 2);
	}

	private bool TryGetWaterWallBounds(
		float waterY1, float waterY2, WaterType activeWaterMode2, byte activeWaterProfile1, byte activeWaterProfile2,
		out float yMin, out float yMax, out byte wallProfile, out Vector3 wallNorm, bool isXAxis)
	{
		yMin = 0; yMax = 0; wallProfile = 0; wallNorm = Vector3.Zero;
		if (activeWaterMode2 == WaterType.None || MathF.Abs(waterY1 - waterY2) <= 0.01f) return false;

		yMin = MathF.Min(waterY1, waterY2);
		yMax = MathF.Max(waterY1, waterY2);
		wallProfile = (waterY1 >= waterY2) ? activeWaterProfile1 : activeWaterProfile2;
		wallNorm = isXAxis ? ((waterY1 > waterY2) ? Vector3.Right : Vector3.Left) : ((waterY1 > waterY2) ? Vector3.Back : Vector3.Forward);
		
		return true;
	}

	private void AddWaterWallGeometry(
		byte wallProfile, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices,
		Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 wallNorm)
	{
		var wGeom = GetGeomLists(wallProfile, profileVerts, profileNorms, profileUvs, profileIndices);
		int wBase = wGeom.verts.Count;
		AddWallGeometry(wGeom.verts, wGeom.norms, wGeom.uvs, wGeom.indices, wBase, p0, p1, p2, p3, wallNorm);
	}

	private void AddWaterWall(
		int x, int z, float waterY1, byte activeWaterProfile1, TerrainCell[,] cells, int w, int d, float quadSize, float halfWQuadSize, float halfDQuadSize, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices, bool isX)
	{
		var (waterY2, activeWaterMode2, activeWaterProfile2) = GetCellWaterInfo(isX ? x + 1 : x, isX ? z : z + 1, cells, w, d);
		
		if (!TryGetWaterWallBounds(waterY1, waterY2, activeWaterMode2, activeWaterProfile1, activeWaterProfile2, out float yMin, out float yMax, out byte wallProfile, out Vector3 wallNorm, isX)) return;

		if (isX)
		{
			float wallX = (x + 1) * quadSize - halfWQuadSize;
			float z0 = z * quadSize - halfDQuadSize;
			float z1 = (z + 1) * quadSize - halfDQuadSize;
			AddWaterWallGeometry(wallProfile, profileVerts, profileNorms, profileUvs, profileIndices,
				new Vector3(wallX, yMin, z0), new Vector3(wallX, yMin, z1), new Vector3(wallX, yMax, z1), new Vector3(wallX, yMax, z0), wallNorm);
		}
		else
		{
			float wallZ = (z + 1) * quadSize - halfDQuadSize;
			float x0 = x * quadSize - halfWQuadSize;
			float x1 = (x + 1) * quadSize - halfWQuadSize;
			AddWaterWallGeometry(wallProfile, profileVerts, profileNorms, profileUvs, profileIndices,
				new Vector3(x0, yMin, wallZ), new Vector3(x1, yMin, wallZ), new Vector3(x1, yMax, wallZ), new Vector3(x0, yMax, wallZ), wallNorm);
		}
	}

	private void UpdateWaterMeshesForChunk(
		TerrainChunk chunk, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
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

	private (List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> indices) GetGeomLists(
		byte pIdx, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
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

		var surfaceArray = new Array();
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
		var worldSize = new Vector2(Width * QuadSize, Depth * QuadSize);

		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", worldSize);
			_material.SetShaderParameter("grid_spacing", QuadSize);
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
			_terrainUberShader ??= GD.Load<Shader>("res://Assets/shaders/terrain_uber.gdshader");
			_material = new ShaderMaterial();
			_material.Shader = _terrainUberShader;
		}

		ReloadTerrainTextures();
		CreatePolarOverlayMaterial();

		_material.SetShaderParameter("grid_spacing", QuadSize);
		_material.SetShaderParameter("terrain_size", new Vector2(Width * QuadSize, Depth * QuadSize));
		_material.SetShaderParameter("texture_scale", 1.0f / QuadSize);
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

		_material.SetShaderParameter("blend_noise_strength", BlendNoiseStrength);
		_material.SetShaderParameter("blend_noise_scale", BlendNoiseScale);
	}

}
