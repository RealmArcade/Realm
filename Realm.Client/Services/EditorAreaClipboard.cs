using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

/// <summary>
/// What the editor has copied: the area template (cells, splat and cliff splat, pathing, entities and the
/// ellipse mask) plus the single copied prop or decal, together with the reads the clipboard UI and the
/// paste path need. It reads the terrain through the editor service and the live splat maps through
/// <see cref="EditorTerrainSurface"/>, so nothing else has to hold this state.
/// </summary>
public sealed class EditorAreaClipboard
{
	private readonly EditorTerrainSurface _surface;

	private CopiedAreaTemplate? _copiedArea;
	private CopiedObjectTemplate? _copiedObject;
	private int _pasteAnchorIndex;

	public EditorAreaClipboard(EditorTerrainSurface surface)
	{
		_surface = surface;
	}

	public void ClearArea() => _copiedArea = null;

	public CopiedAreaTemplate? CopiedArea
	{
		get => _copiedArea;
		set => _copiedArea = value;
	}

	public bool HasCopiedArea => _copiedArea != null;
	public int CopiedAreaWidth => _copiedArea?.Width ?? 0;
	public int CopiedAreaDepth => _copiedArea?.Depth ?? 0;
	public int CopiedAreaAnchorX => _copiedArea?.AnchorTileX ?? 0;
	public int CopiedAreaAnchorZ => _copiedArea?.AnchorTileZ ?? 0;
	public int CopiedAreaSourceMinX => _copiedArea?.SourceMinX ?? 0;
	public int CopiedAreaSourceMinZ => _copiedArea?.SourceMinZ ?? 0;
	public int CopiedAreaSourceMaxX => _copiedArea?.SourceMaxX ?? 0;
	public int CopiedAreaSourceMaxZ => _copiedArea?.SourceMaxZ ?? 0;
	public bool HasCopiedAreaMask => _copiedArea?.Mask != null;
	public bool IsCopiedCellMasked(int srcX, int srcZ) => _copiedArea == null || _copiedArea.Mask == null || (srcX >= 0 && srcX < _copiedArea.Width && srcZ >= 0 && srcZ < _copiedArea.Depth && _copiedArea.Mask[srcX, srcZ]);

	public int GetPasteAnchorX(int index, int width)
	{
		return index switch
		{
			0 => width / 2,
			1 => 0,
			2 => Math.Max(0, width - 1),
			3 => Math.Max(0, width - 1),
			4 => 0,
			_ => width / 2
		};
	}

	public int GetPasteAnchorZ(int index, int depth)
	{
		return index switch
		{
			0 => depth / 2,
			1 => 0,
			2 => 0,
			3 => Math.Max(0, depth - 1),
			4 => Math.Max(0, depth - 1),
			_ => depth / 2
		};
	}

	public int PasteAnchorIndex
	{
		get => _pasteAnchorIndex;
		set => _pasteAnchorIndex = value;
	}

	public void CyclePasteAnchor()
	{
		if (!HasCopiedArea) return;
		_pasteAnchorIndex = (_pasteAnchorIndex + 1) % EditorToolCatalog.PasteAnchorNames.Length;
		SetCopiedAreaAnchor(
			GetPasteAnchorX(_pasteAnchorIndex, CopiedAreaWidth),
			GetPasteAnchorZ(_pasteAnchorIndex, CopiedAreaDepth));
	}

	public void SetCopiedAreaAnchor(int anchorTileX, int anchorTileZ)
	{
		if (_copiedArea != null)
		{
			_copiedArea.AnchorTileX = Mathf.Clamp(anchorTileX, 0, Math.Max(0, _copiedArea.Width - 1));
			_copiedArea.AnchorTileZ = Mathf.Clamp(anchorTileZ, 0, Math.Max(0, _copiedArea.Depth - 1));
		}
	}

	public CopiedObjectTemplate? GetCopiedObject() => _copiedObject;

	public void SetCopiedObject(CopiedObjectTemplate? template)
	{
		_copiedObject = template;
	}

	public void CopyProp(string propId, float rotation, float scale) =>
		_copiedObject = new CopiedObjectTemplate
		{
			Type = "prop",
			Id = propId,
			Rotation = rotation,
			Scale = scale,
			Player = 0
		};

	public void CopyDecal(string decalId, float rotation, float scale) =>
		_copiedObject = new CopiedObjectTemplate
		{
			Type = "decal",
			Id = decalId,
			Rotation = rotation,
			Scale = scale,
			Player = 0
		};

	public void CopyArea(int minX, int minZ, int maxX, int maxZ, List<CopiedEntityInfo> entities, bool isSquare = true)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return;

		if (_surface.SplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.SplatMap = GameContext.Host.GroundTerrain.SplatMap;
		}

		if (_surface.CliffSplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.CliffSplatMap = GameContext.Host.GroundTerrain.CliffSplatMap;
		}

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		var copiedCells = new TerrainCell[selWidth, selDepth];
		var splatMap = _surface.SplatMap != null ? new TerrainSplatWeights[selWidth + 1, selDepth + 1] : null;
		var cliffSplatMap = _surface.CliffSplatMap != null ? new TerrainSplatWeights[selWidth + 1, selDepth + 1] : null;
		var pathing = new int[selWidth, selDepth];
		var mask = isSquare ? null : new bool[selWidth, selDepth];

		float selCenterX = (minX + maxX) * 0.5f;
		float selCenterZ = (minZ + maxZ) * 0.5f;
		float rx = Math.Max(0.5f, (maxX - minX) * 0.5f);
		float rz = Math.Max(0.5f, (maxZ - minZ) * 0.5f);

		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				int sourceX = Math.Clamp(minX + sx, 0, terrain.Width - 1);
				int sourceZ = Math.Clamp(minZ + sz, 0, terrain.Depth - 1);
				bool inBounds = true;
				if (!isSquare)
				{
					float cellCenterX = minX + sx + 0.5f;
					float cellCenterZ = minZ + sz + 0.5f;
					float ndx = (cellCenterX - selCenterX) / rx;
					float ndz = (cellCenterZ - selCenterZ) / rz;
					inBounds = (ndx * ndx + ndz * ndz <= 1.05f);
				}

				if (mask != null)
				{
					mask[sx, sz] = inBounds;
				}

				if (inBounds)
				{
					copiedCells[sx, sz] = cells[sourceX, sourceZ];
					if (terrain.PathingCodes != null && sourceX < terrain.PathingCodes.GetLength(0) && sourceZ < terrain.PathingCodes.GetLength(1))
					{
						pathing[sx, sz] = terrain.PathingCodes[sourceX, sourceZ];
					}
				}
			}
		}

		if (_surface.SplatMap != null && splatMap != null)
		{
			int mapW = _surface.SplatMap.GetLength(0);
			int mapD = _surface.SplatMap.GetLength(1);
			for (int vz = 0; vz <= selDepth; vz++)
			{
				for (int vx = 0; vx <= selWidth; vx++)
				{
					int sourceVx = Math.Clamp(minX + vx, 0, mapW - 1);
					int sourceVz = Math.Clamp(minZ + vz, 0, mapD - 1);
					splatMap[vx, vz] = _surface.SplatMap[sourceVx, sourceVz];
				}
			}
		}

		if (_surface.CliffSplatMap != null && cliffSplatMap != null)
		{
			int mapCW = _surface.CliffSplatMap.GetLength(0);
			int mapCD = _surface.CliffSplatMap.GetLength(1);
			for (int vz = 0; vz <= selDepth; vz++)
			{
				for (int vx = 0; vx <= selWidth; vx++)
				{
					int sourceVx = Math.Clamp(minX + vx, 0, mapCW - 1);
					int sourceVz = Math.Clamp(minZ + vz, 0, mapCD - 1);
					cliffSplatMap[vx, vz] = _surface.CliffSplatMap[sourceVx, sourceVz];
				}
			}
		}

		_copiedArea = new CopiedAreaTemplate
		{
			Width = selWidth,
			Depth = selDepth,
			AnchorTileX = selWidth / 2,
			AnchorTileZ = selDepth / 2,
			SourceMinX = minX,
			SourceMinZ = minZ,
			SourceMaxX = maxX,
			SourceMaxZ = maxZ,
			Cells = copiedCells,
			SplatMap = splatMap,
			CliffSplatMap = cliffSplatMap,
			Pathing = pathing,
			Entities = entities,
			Mask = mask
		};
	}

	public List<CopiedEntityInfo> BuildCopiedEntityList(
		int minX, int minZ, int maxX, int maxZ,
		IReadOnlyList<CopiedEntityCandidate> candidates,
		bool isSquare = true)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		if (terrain.Cells == null) return new List<CopiedEntityInfo>();

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		EditorTerrainSurface.CalculateWorldBounds(minX, minZ, maxX, maxZ, width, depth, quadSize, out float minWorldX, out float maxWorldX, out float minWorldZ, out float maxWorldZ, out float centerWorldX, out float centerWorldZ, out float rxWorld, out float rzWorld);
		Godot.Vector3 origin = new Godot.Vector3((minX - width / 2.0f) * quadSize, 0.0f, (minZ - depth / 2.0f) * quadSize);

		var entities = new List<CopiedEntityInfo>();
		for (int index = 0; index < candidates.Count; index++)
		{
			CopiedEntityCandidate candidate = candidates[index];
			Godot.Vector3 pos = candidate.Position;
			if (pos.X < minWorldX || pos.X > maxWorldX || pos.Z < minWorldZ || pos.Z > maxWorldZ) continue;

			if (!isSquare)
			{
				float ndx = (pos.X - centerWorldX) / rxWorld;
				float ndz = (pos.Z - centerWorldZ) / rzWorld;
				if (ndx * ndx + ndz * ndz > 1.0f) continue;
			}

			entities.Add(new CopiedEntityInfo
			{
				Type = candidate.Type,
				Id = candidate.Id,
				RelativePos = pos - origin,
				Rotation = candidate.RotationY,
				Scale = candidate.Scale,
				Player = candidate.Player
			});
		}

		if (GameContext.World != null)
		{
			var propQuery = QueryCache.AllPropIdentityAndPositionQuery;
			var world = GameContext.World;
			world.Query(in propQuery, (Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
			{
				if (GameHost.EntityToProp3D.ContainsKey(entity)) return;

				Godot.Vector3 worldPos = new Godot.Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);
				if (worldPos.X >= minWorldX && worldPos.X <= maxWorldX && worldPos.Z >= minWorldZ && worldPos.Z <= maxWorldZ)
				{
					if (!isSquare)
					{
						float ndx = (worldPos.X - centerWorldX) / rxWorld;
						float ndz = (worldPos.Z - centerWorldZ) / rzWorld;
						if (ndx * ndx + ndz * ndz > 1.0f) return;
					}

					float rotY = world.Has<RotationY>(entity) ? world.Get<RotationY>(entity).Value : 0f;
					float scale = world.Has<ModelScale>(entity) ? world.Get<ModelScale>(entity).Value : 1f;

					entities.Add(new CopiedEntityInfo
					{
						Type = "prop",
						Id = propIdComp.PropId,
						RelativePos = worldPos - origin,
						Rotation = rotY,
						Scale = scale,
						Player = 0
					});
				}
			});
		}

		return entities;
	}
}
