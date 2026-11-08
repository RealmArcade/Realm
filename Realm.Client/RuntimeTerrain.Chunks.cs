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
	protected void CreateChunks()
	{
		foreach (var chunk in _chunks)
		{
			if (GodotObject.IsInstanceValid(chunk.MeshInstance)) chunk.MeshInstance.QueueFree();
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

				_chunks.Add(chunk);
			}
		}

		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(w * QuadSize, d * QuadSize));
			_material.SetShaderParameter("grid_spacing", QuadSize);
		}
	}

	protected ShaderMaterial _polarOverlayMaterial = null!;
	private Shader _terrainUberShader = null!;
	private Shader _polarOverlayShader = null!;

	private void CreatePolarOverlayMaterial()
	{
		_polarOverlayShader ??= GD.Load<Shader>("res://Assets/shaders/terrain_polar_overlay.gdshader");
		_polarOverlayMaterial = new ShaderMaterial();
		_polarOverlayMaterial.Shader = _polarOverlayShader;
		_polarOverlayMaterial.RenderPriority = 100;
		_polarOverlayMaterial.SetShaderParameter("grid_spacing", QuadSize);
		ApplyOverlayPass();
	}

	private bool _gridWanted;
	private bool _polarWanted;
	private bool _overlaySuppressed;
	private Vector2 _polarCenterWanted;
	private float _polarRingSpacingWanted = 8.0f;
	private float _polarRadialStepWanted = 45.0f;

	private void ApplyOverlayPass()
	{
		if (_material == null || _polarOverlayMaterial == null) return;
		_polarOverlayMaterial.SetShaderParameter("grid_visible", _gridWanted);
		_polarOverlayMaterial.SetShaderParameter("polar_overlay_visible", _polarWanted);
		_polarOverlayMaterial.SetShaderParameter("polar_center", _polarCenterWanted);
		_polarOverlayMaterial.SetShaderParameter("polar_ring_spacing", _polarRingSpacingWanted);
		_polarOverlayMaterial.SetShaderParameter("polar_radial_step_deg", _polarRadialStepWanted);
		_polarOverlayMaterial.SetShaderParameter("terrain_size", _material.GetShaderParameter("terrain_size"));
		_polarOverlayMaterial.SetShaderParameter("grid_spacing", _material.GetShaderParameter("grid_spacing"));
		bool active = !_overlaySuppressed && (_gridWanted || _polarWanted);
		_material.NextPass = active ? _polarOverlayMaterial : null;
	}

	public virtual void SetGridVisible(bool visible)
	{
		_gridWanted = visible;
		ApplyOverlayPass();
	}

	public virtual void SetPolarOverlayVisible(bool visible)
	{
		_polarWanted = visible;
		ApplyOverlayPass();
	}

	public virtual void SetPolarCenter(Vector2 center)
	{
		_polarCenterWanted = center;
		ApplyOverlayPass();
	}

	public virtual void SetPolarRingSpacing(float spacing)
	{
		_polarRingSpacingWanted = spacing;
		ApplyOverlayPass();
	}

	public virtual void SetPolarRadialStep(float angleDegrees)
	{
		_polarRadialStepWanted = angleDegrees;
		ApplyOverlayPass();
	}

	private bool _wasPathingVisibleBeforeMinimap;

	public static void ClearCache()
	{
		_cachedAlbedoTextureArray = null;
		_cachedNormalRoughnessTextureArray = null;
		_cachedMapDir = null;
		_liveSwatchOverrides.Clear();
	}

	public void BeginMinimapCapture()
	{
		if (_material != null)
		{
			_overlaySuppressed = true;
			ApplyOverlayPass();

			var pathingParam = _material.GetShaderParameter("pathing_visible");
			_wasPathingVisibleBeforeMinimap = pathingParam.VariantType == Variant.Type.Bool && pathingParam.AsBool();
			if (_wasPathingVisibleBeforeMinimap)
			{
				_material.SetShaderParameter("pathing_visible", false);
			}
		}

		SetAllChunksVisible(true);
	}

	public void EndMinimapCapture()
	{
		if (_material != null)
		{
			_overlaySuppressed = false;
			ApplyOverlayPass();
			if (_wasPathingVisibleBeforeMinimap)
			{
				_material.SetShaderParameter("pathing_visible", true);
			}
		}
	}

}
