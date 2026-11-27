using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client;

public partial class RuntimeTerrain
{
	private readonly float[] _grassHeights = new float[TextureSwatchSlots.MaxSlots];
	private readonly List<Vector4> _grassInstanceBuffer = new();
	private ShaderMaterial? _grassMaterial;
	private ArrayMesh? _grassTuft;
	private bool _hasGrass;

	private void UpdateGrassPalette(SwatchSlotInfo[] slots)
	{
		_hasGrass = false;
		for (int i = 0; i < slots.Length; i++)
		{
			_grassHeights[i] = Mathf.Clamp(slots[i].MetadataNode?.GrassBladeHeight ?? 0f, 0f, 1.5f);
			_hasGrass |= _grassHeights[i] > 0f;
		}

		_material.SetShaderParameter("swatch_grass_height", _grassHeights);

		if (_hasGrass)
		{
			_grassMaterial ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://Assets/shaders/terrain_grass.gdshader") };
			_grassMaterial.SetShaderParameter("terrain_textures", ToVariantOrNil(_cachedAlbedoTextureArray));
			_grassMaterial.SetShaderParameter("swatch_albedo_params", _swatchAlbedoParamsCache);
			_grassTuft ??= CreateGrassTuft();
		}

		foreach (var chunk in _chunks) UpdateGrassChunk(chunk);
	}

	private static ArrayMesh CreateGrassTuft()
	{
		const int bladeCount = 7;
		var vertices = new Vector3[bladeCount * 5];
		var normals = new Vector3[vertices.Length];
		var indices = new int[bladeCount * 9];
		var distantBlades = new int[bladeCount * 3];
		for (int blade = 0; blade < bladeCount; blade++)
		{
			float angle = blade * 2.399963f;
			var outward = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
			var width = new Vector3(-outward.Z, 0f, outward.X) * (0.077f + 0.012f * (blade % 3));
			var root = outward * (0.035f + 0.025f * blade);
			float height = 1f - blade * 0.053f;
			var middle = root + Vector3.Up * height * 0.58f + outward * 0.12f;
			int first = blade * 5;
			vertices[first] = root - width;
			vertices[first + 1] = root + width;
			vertices[first + 2] = middle - width * 0.72f;
			vertices[first + 3] = middle + width * 0.66f + outward * 0.052f;
			vertices[first + 4] = root + outward * (0.26f + 0.052f * blade) + Vector3.Up * height;
			for (int point = 0; point < 5; point++)
				normals[first + point] = (Vector3.Up * (0.35f + point * 0.14f) + outward + width.Normalized() * (point % 2 == 0 ? -0.36f : 0.36f)).Normalized();
			int start = blade * 9;
			indices[start] = first;
			indices[start + 1] = first + 1;
			indices[start + 2] = first + 2;
			indices[start + 3] = first + 1;
			indices[start + 4] = first + 3;
			indices[start + 5] = first + 2;
			indices[start + 6] = first + 2;
			indices[start + 7] = first + 3;
			indices[start + 8] = first + 4;
			distantBlades[blade * 3] = first;
			distantBlades[blade * 3 + 1] = first + 1;
			distantBlades[blade * 3 + 2] = first + 4;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		var mesh = new ArrayMesh();
		var levels = new Godot.Collections.Dictionary { [0.08f] = distantBlades, [0.22f] = distantBlades[..12] };
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, lods: levels);
		return mesh;
	}

	private static float NextGrassSample(ref uint seed)
	{
		seed = unchecked(seed * 1664525u + 1013904223u);
		return (seed >> 8) * (1f / 16777216f);
	}

	private void UpdateGrassChunk(TerrainChunk chunk)
	{
		if (!_hasGrass && chunk.Grass == null) return;
		_grassInstanceBuffer.Clear();
		var splats = SplatMap;
		var cells = Cells;
		int width = cells.GetLength(0);
		int depth = cells.GetLength(1);
		float spacing = QuadSize;
		if (_hasGrass && splats != null && splats.GetLength(0) > chunk.EndX && splats.GetLength(1) > chunk.EndZ &&
		    width >= chunk.EndX && depth >= chunk.EndZ)
		{
			for (int z = chunk.StartZ; z < chunk.EndZ; z++)
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				var cell = cells[x, z];
				if (CalculateQuadCliffWeight(x, z, cell, cells, width, depth, ExtractQuadHeights(cell)) > 0f) continue;
				var (waterY, waterMode, _) = GetCellWaterInfo(x, z, cells, width, depth);
				uint seed = unchecked((uint)x * 2246822519u ^ (uint)z * 3266489917u);
				for (int patch = 0; patch < 4; patch++)
				{
					float u = ((patch & 1) + NextGrassSample(ref seed)) * 0.5f;
					float v = ((patch >> 1) + NextGrassSample(ref seed)) * 0.5f;
					int cornerX = NextGrassSample(ref seed) < u ? 1 : 0;
					int cornerZ = NextGrassSample(ref seed) < v ? 1 : 0;
					var paint = splats[x + cornerX, z + cornerZ];
					float pick = NextGrassSample(ref seed);
					int slot = pick < paint.Weight0 ? paint.Index0 :
						pick < paint.Weight0 + paint.Weight1 ? paint.Index1 :
						pick < paint.Weight0 + paint.Weight1 + paint.Weight2 ? paint.Index2 : paint.Index3;
					if (paint.Weight0 + paint.Weight1 + paint.Weight2 + paint.Weight3 <= pick ||
					    (uint)slot >= _grassHeights.Length || _grassHeights[slot] <= 0f) continue;

					float north = Mathf.Lerp(cell.Y_NW, cell.Y_NE, u);
					float south = Mathf.Lerp(cell.Y_SW, cell.Y_SE, u);
					float twist = cell.Y_NW - cell.Y_NE - cell.Y_SW + cell.Y_SE;
					float dx = u - 0.5f;
					float dz = v - 0.5f;
					float centerFanCorrection = 0.5f * MathF.CopySign(MathF.Min(MathF.Abs(dx), MathF.Abs(dz)), dx * dz) - dx * dz;
					float surface = Mathf.Lerp(north, south, v) + twist * centerFanCorrection;
					if (waterMode != WaterType.None && surface <= waterY) continue;
					float height = _grassHeights[slot] * (0.55f + 0.45f * NextGrassSample(ref seed));
					float angle = NextGrassSample(ref seed) * Mathf.Tau;
					AppendGrassInstance((x + u - width * 0.5f) * spacing, surface - 0.025f,
						(z + v - depth * 0.5f) * spacing, height, angle, slot, u, v);
				}
			}
		}

		int count = _grassInstanceBuffer.Count / 4;
		if (count == 0)
		{
			if (chunk.Grass != null)
			{
				chunk.Grass.QueueFree();
				chunk.Grass = null;
			}
			return;
		}

		if (chunk.Grass == null)
		{
			chunk.Grass = new MultiMeshInstance3D
			{
				Name = "GrassTufts", MaterialOverride = _grassMaterial, Layers = 1,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				VisibilityRangeEnd = 320f, ExtraCullMargin = 0.3f,
				Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
					UseCustomData = true, Mesh = _grassTuft }
			};
			chunk.MeshInstance.AddChild(chunk.Grass);
		}

		var instances = chunk.Grass.Multimesh;
		instances.InstanceCount = count;
		instances.Buffer = MemoryMarshal.Cast<Vector4, float>(CollectionsMarshal.AsSpan(_grassInstanceBuffer)).ToArray();
	}

	private void AppendGrassInstance(float x, float y, float z, float height, float angle, int slot, float u, float v)
	{
		float cosine = MathF.Cos(angle) * height;
		float sine = MathF.Sin(angle) * height;
		_grassInstanceBuffer.Add(new Vector4(cosine, 0f, sine, x));
		_grassInstanceBuffer.Add(new Vector4(0f, height, 0f, y));
		_grassInstanceBuffer.Add(new Vector4(-sine, 0f, cosine, z));
		_grassInstanceBuffer.Add(new Vector4(slot, u, v, 0f));
	}
}
