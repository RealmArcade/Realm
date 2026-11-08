using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

/// <summary>
/// Writes a copied area back into the terrain grid: one rotated, reflected, folded and mirrored cell at a
/// time, with the height, splat, cliff-splat and pathing writes that each copied cell carries. It writes
/// through <see cref="EditorTerrainSurface"/>, so the editor only decides the layout and hands it over.
/// </summary>
internal sealed class EditorPasteWriter
{
	private readonly EditorTerrainSurface _surface;
	private readonly EditorAreaClipboard _clipboard;

	public EditorPasteWriter(EditorTerrainSurface surface, EditorAreaClipboard clipboard)
	{
		_surface = surface;
		_clipboard = clipboard;
	}

	private void ProcessCell(
		CopiedAreaTemplate copiedArea,
		int sx, int sz, int pasteWidth, int pasteDepth, int targetWidth, int targetDepth,
		int blockStartX, int blockStartZ,
		int rotSteps, PasteReflection pasteReflection, MirrorMode symmetryMirror, int foldRotSteps,
		int width, int depth, bool pasteHeights, bool pasteTextures, bool pastePathing,
		TerrainCell[,]? srcCells, TerrainCell[,]? cells,
		ref TerrainState terrain, ref bool modified, ref bool pathingModified)
	{
		int srcX = pasteReflection == PasteReflection.Horizontal ? pasteWidth - 1 - sx : sx;
		int srcZ = pasteReflection == PasteReflection.Vertical ? pasteDepth - 1 - sz : sz;

		if (copiedArea.Mask != null && !copiedArea.Mask[srcX, srcZ]) return;

		int rotX = sx;
		int rotZ = sz;
		if (rotSteps == 1)
		{
			rotX = pasteDepth - 1 - sz;
			rotZ = sx;
		}
		else if (rotSteps == 2)
		{
			rotX = pasteWidth - 1 - sx;
			rotZ = pasteDepth - 1 - sz;
		}
		else if (rotSteps == 3)
		{
			rotX = sz;
			rotZ = pasteWidth - 1 - sx;
		}

		TerrainCell curCell = default;
		if (pasteHeights && srcCells != null && srcX < pasteWidth && srcZ < pasteDepth)
		{
			curCell = srcCells[srcX, srcZ];
			if (pasteReflection == PasteReflection.Horizontal) curCell = EditorTerrainGeometry.MirrorCell(in curCell, MirrorMode.Horizontal);
			else if (pasteReflection == PasteReflection.Vertical) curCell = EditorTerrainGeometry.MirrorCell(in curCell, MirrorMode.Vertical);
			curCell = EditorTerrainGeometry.RotateCell(in curCell, rotSteps);
		}

		TerrainSplatWeights sNW = default, sNE = default, sSE = default, sSW = default;
		TerrainSplatWeights cS_NW = default, cS_NE = default, cS_SE = default, cS_SW = default;
		if (pasteTextures)
		{
			if (copiedArea.SplatMap != null && srcX + 1 < copiedArea.SplatMap.GetLength(0) && srcZ + 1 < copiedArea.SplatMap.GetLength(1))
			{
				sNW = copiedArea.SplatMap[srcX, srcZ];
				sNE = copiedArea.SplatMap[srcX + 1, srcZ];
				sSE = copiedArea.SplatMap[srcX + 1, srcZ + 1];
				sSW = copiedArea.SplatMap[srcX, srcZ + 1];
				if (pasteReflection == PasteReflection.Horizontal) (sNW, sNE, sSE, sSW) = EditorTerrainGeometry.MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Horizontal);
				else if (pasteReflection == PasteReflection.Vertical) (sNW, sNE, sSE, sSW) = EditorTerrainGeometry.MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Vertical);
				(sNW, sNE, sSE, sSW) = EditorTerrainGeometry.RotateSplatQuad(in sNW, in sNE, in sSE, in sSW, rotSteps);
			}

			if (copiedArea.CliffSplatMap != null && srcX + 1 < copiedArea.CliffSplatMap.GetLength(0) && srcZ + 1 < copiedArea.CliffSplatMap.GetLength(1))
			{
				cS_NW = copiedArea.CliffSplatMap[srcX, srcZ];
				cS_NE = copiedArea.CliffSplatMap[srcX + 1, srcZ];
				cS_SE = copiedArea.CliffSplatMap[srcX + 1, srcZ + 1];
				cS_SW = copiedArea.CliffSplatMap[srcX, srcZ + 1];
				if (pasteReflection == PasteReflection.Horizontal) (cS_NW, cS_NE, cS_SE, cS_SW) = EditorTerrainGeometry.MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Horizontal);
				else if (pasteReflection == PasteReflection.Vertical) (cS_NW, cS_NE, cS_SE, cS_SW) = EditorTerrainGeometry.MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Vertical);
				(cS_NW, cS_NE, cS_SE, cS_SW) = EditorTerrainGeometry.RotateSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, rotSteps);
			}
			else if (copiedArea.SplatMap != null)
			{
				cS_NW = sNW;
				cS_NE = sNE;
				cS_SE = sSE;
				cS_SW = sSW;
			}
		}

		int finalX = rotX;
		int finalZ = rotZ;
		if (symmetryMirror == MirrorMode.Horizontal)
		{
			finalX = targetWidth - 1 - rotX;
			curCell = EditorTerrainGeometry.MirrorCell(in curCell, MirrorMode.Horizontal);
			(sNW, sNE, sSE, sSW) = EditorTerrainGeometry.MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Horizontal);
			(cS_NW, cS_NE, cS_SE, cS_SW) = EditorTerrainGeometry.MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Horizontal);
		}
		else if (symmetryMirror == MirrorMode.Vertical)
		{
			finalZ = targetDepth - 1 - rotZ;
			curCell = EditorTerrainGeometry.MirrorCell(in curCell, MirrorMode.Vertical);
			(sNW, sNE, sSE, sSW) = EditorTerrainGeometry.MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Vertical);
			(cS_NW, cS_NE, cS_SE, cS_SW) = EditorTerrainGeometry.MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Vertical);
		}
		else if (symmetryMirror == MirrorMode.Both)
		{
			finalX = targetWidth - 1 - rotX;
			finalZ = targetDepth - 1 - rotZ;
			curCell = EditorTerrainGeometry.MirrorCell(in curCell, MirrorMode.Both);
			(sNW, sNE, sSE, sSW) = EditorTerrainGeometry.MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Both);
			(cS_NW, cS_NE, cS_SE, cS_SW) = EditorTerrainGeometry.MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Both);
		}
		else if (foldRotSteps != 0)
		{
			if (foldRotSteps == 1)
			{
				finalX = targetDepth - 1 - rotZ;
				finalZ = rotX;
			}
			else if (foldRotSteps == 2)
			{
				finalX = targetWidth - 1 - rotX;
				finalZ = targetDepth - 1 - rotZ;
			}
			else if (foldRotSteps == 3)
			{
				finalX = rotZ;
				finalZ = targetWidth - 1 - rotX;
			}

			curCell = EditorTerrainGeometry.RotateCell(in curCell, foldRotSteps);
			(sNW, sNE, sSE, sSW) = EditorTerrainGeometry.RotateSplatQuad(in sNW, in sNE, in sSE, in sSW, foldRotSteps);
			(cS_NW, cS_NE, cS_SE, cS_SW) = EditorTerrainGeometry.RotateSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, foldRotSteps);
		}

		int targetX = blockStartX + finalX;
		int targetZ = blockStartZ + finalZ;

		if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
		{
			if (pasteHeights && srcCells != null && cells != null)
			{
				EditorTerrainGeometry.SetGridNodeHeight(ref terrain, targetX, targetZ, curCell.Y_NW);
				EditorTerrainGeometry.SetGridNodeHeight(ref terrain, targetX + 1, targetZ, curCell.Y_NE);
				EditorTerrainGeometry.SetGridNodeHeight(ref terrain, targetX + 1, targetZ + 1, curCell.Y_SE);
				EditorTerrainGeometry.SetGridNodeHeight(ref terrain, targetX, targetZ + 1, curCell.Y_SW);
				cells[targetX, targetZ].WaterMode = curCell.WaterMode;
				cells[targetX, targetZ].WaterProfileIndex = curCell.WaterProfileIndex;
				cells[targetX, targetZ].WaterHeight = curCell.WaterHeight;
			}

			if (pasteTextures)
			{
				_surface.SetGridNodeSplat(targetX, targetZ, in sNW, in sNE, in sSE, in sSW);
				_surface.SetGridNodeCliffSplat(targetX, targetZ, in cS_NW, in cS_NE, in cS_SE, in cS_SW);
			}

			if (pastePathing && copiedArea.Pathing != null && terrain.PathingCodes != null && srcX < pasteWidth && srcZ < pasteDepth)
			{
				terrain.PathingCodes[targetX, targetZ] = copiedArea.Pathing[srcX, srcZ];
				pathingModified = true;
			}

			modified = true;
		}
	}

	public void PasteBlock(
		CopiedAreaTemplate copiedArea,
		int blockStartX, int blockStartZ,
		int rotSteps,
		PasteReflection pasteReflection,
		MirrorMode symmetryMirror,
		int foldRotSteps,
		int width, int depth,
		bool pasteHeights, bool pasteTextures, bool pastePathing,
		ref TerrainState terrain,
		ref bool modified,
		ref bool pathingModified)
	{
		int pasteWidth = copiedArea.Width;
		int pasteDepth = copiedArea.Depth;
		int targetWidth = (rotSteps == 1 || rotSteps == 3) ? pasteDepth : pasteWidth;
		int targetDepth = (rotSteps == 1 || rotSteps == 3) ? pasteWidth : pasteDepth;

		var cells = terrain.Cells;
		var srcCells = copiedArea.Cells;

		for (int sz = 0; sz < pasteDepth; sz++)
		{
			for (int sx = 0; sx < pasteWidth; sx++)
			{
				ProcessCell(copiedArea, sx, sz, pasteWidth, pasteDepth, targetWidth, targetDepth, blockStartX, blockStartZ, rotSteps, pasteReflection, symmetryMirror, foldRotSteps, width, depth, pasteHeights, pasteTextures, pastePathing, srcCells, cells, ref terrain, ref modified, ref pathingModified);
			}
		}
	}

	public PasteAreaResult Build(
		int startX,
		int startZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		bool pastePathing,
		MirrorMode mirrorMode,
		float rotationDegrees,
		PasteReflection pasteReflection,
		Vector2 symmetryPivot,
		int symmetryFolds)
	{
		var result = new PasteAreaResult();
		result.SpawnRequests = new List<EntitySpawnRequest>();

		if (_clipboard.CopiedArea == null) return result;
		var copiedArea = _clipboard.CopiedArea;

		ref var terrain = ref EditorService.GetTerrainState();
		if (terrain.Cells == null) return result;

		if (_surface.SplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.SplatMap = GameContext.Host.GroundTerrain.SplatMap;
		}

		if (_surface.CliffSplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.CliffSplatMap = GameContext.Host.GroundTerrain.CliffSplatMap;
		}

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int pasteWidth = copiedArea.Width;
		int pasteDepth = copiedArea.Depth;
		bool modified = false;
		bool pathingModified = false;

		float r = rotationDegrees % 360.0f;
		if (r < 0) r += 360.0f;
		int rotSteps = (int)Math.Round(r / 90.0f) % 4;
		int targetWidth = (rotSteps == 1 || rotSteps == 3) ? pasteDepth : pasteWidth;
		int targetDepth = (rotSteps == 1 || rotSteps == 3) ? pasteWidth : pasteDepth;

		PasteBlock(copiedArea, startX, startZ, rotSteps, pasteReflection, MirrorMode.None, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

		if (mirrorMode != MirrorMode.None)
		{
			Godot.Vector3 centerPos = new Godot.Vector3((startX + targetWidth / 2.0f - width / 2.0f) * quadSize, 0, (startZ + targetDepth / 2.0f - depth / 2.0f) * quadSize);
			var transforms = EditorGridSnapping.GetMirroredTransforms(centerPos, 0.0f, mirrorMode, symmetryPivot, symmetryFolds);
			if (mirrorMode == MirrorMode.Horizontal)
			{
				if (transforms.Count > 0)
				{
					var (rcx, rcz) = EditorGridSnapping.WorldPosToCellCoords(transforms[0].Position);
					int rStartX = rcx - targetWidth / 2;
					int rStartZ = rcz - targetDepth / 2;
					PasteBlock(copiedArea, rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.Horizontal, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Vertical)
			{
				if (transforms.Count > 0)
				{
					var (rcx, rcz) = EditorGridSnapping.WorldPosToCellCoords(transforms[0].Position);
					int rStartX = rcx - targetWidth / 2;
					int rStartZ = rcz - targetDepth / 2;
					PasteBlock(copiedArea, rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.Vertical, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Both)
			{
				if (transforms.Count >= 3)
				{
					var (rcx0, rcz0) = EditorGridSnapping.WorldPosToCellCoords(transforms[0].Position);
					int rStartX0 = rcx0 - targetWidth / 2;
					int rStartZ0 = rcz0 - targetDepth / 2;
					PasteBlock(copiedArea, rStartX0, rStartZ0, rotSteps, pasteReflection, MirrorMode.Horizontal, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

					var (rcx1, rcz1) = EditorGridSnapping.WorldPosToCellCoords(transforms[1].Position);
					int rStartX1 = rcx1 - targetWidth / 2;
					int rStartZ1 = rcz1 - targetDepth / 2;
					PasteBlock(copiedArea, rStartX1, rStartZ1, rotSteps, pasteReflection, MirrorMode.Vertical, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

					var (rcx2, rcz2) = EditorGridSnapping.WorldPosToCellCoords(transforms[2].Position);
					int rStartX2 = rcx2 - targetWidth / 2;
					int rStartZ2 = rcz2 - targetDepth / 2;
					PasteBlock(copiedArea, rStartX2, rStartZ2, rotSteps, pasteReflection, MirrorMode.Both, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Rotational)
			{
				foreach (var t in transforms)
				{
					int rotKSteps = ((int)Math.Round(t.Rotation / 90.0f) % 4 + 4) % 4;
					int curTargetWidth = (rotKSteps == 1 || rotKSteps == 3) ? targetDepth : targetWidth;
					int curTargetDepth = (rotKSteps == 1 || rotKSteps == 3) ? targetWidth : targetDepth;
					var (rcx, rcz) = EditorGridSnapping.WorldPosToCellCoords(t.Position);
					int rStartX = rcx - curTargetWidth / 2;
					int rStartZ = rcz - curTargetDepth / 2;
					PasteBlock(copiedArea, rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.None, rotKSteps, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
		}

		if (modified && pasteHeights)
		{
			EditorGridSnapping.SanitizeCornerHeights(ref terrain);
		}

		if (modified && pasteTextures)
		{
			_surface.AlignSplatMapSlots(0, 0, width, depth);
		}

		result.TerrainModified = modified;
		result.HeightsModified = pasteHeights && modified;
		result.PathingModified = pathingModified;

		if (pasteEntities)
		{
			Godot.Vector3 pasteCenter = new Godot.Vector3((startX + (targetWidth - 1) / 2.0f - width / 2.0f) * quadSize, 0, (startZ + (targetDepth - 1) / 2.0f - depth / 2.0f) * quadSize);

			float rad = rotationDegrees * Mathf.Pi / 180.0f;
			float cosR = Mathf.Cos(rad);
			float sinR = Mathf.Sin(rad);

			Godot.Vector3 originalCenterOffset = new Godot.Vector3((pasteWidth - 1) / 2.0f * quadSize, 0, (pasteDepth - 1) / 2.0f * quadSize);

			foreach (var ent in copiedArea.Entities)
			{
				Godot.Vector3 relPos = ent.RelativePos;
				float entRot = ent.Rotation;

				if (pasteReflection == PasteReflection.Horizontal)
				{
					relPos = new Godot.Vector3((pasteWidth - 1) * quadSize - relPos.X, relPos.Y, relPos.Z);
					entRot = -entRot;
				}
				else if (pasteReflection == PasteReflection.Vertical)
				{
					relPos = new Godot.Vector3(relPos.X, relPos.Y, (pasteDepth - 1) * quadSize - relPos.Z);
					entRot = 180.0f - entRot;
				}

				Godot.Vector3 relativeToCenter = relPos - originalCenterOffset;

				float rx = relativeToCenter.X * cosR - relativeToCenter.Z * sinR;
				float rz = relativeToCenter.X * sinR + relativeToCenter.Z * cosR;

				Godot.Vector3 rotatedRelative = new Godot.Vector3(rx, 0, rz);
				Godot.Vector3 destPos = pasteCenter + rotatedRelative;

				destPos.Y = EditorTerrainQueries.GetHeightAt(destPos);

				float finalRot = entRot - rotationDegrees;

				result.SpawnRequests.Add(new EntitySpawnRequest
				{
					Type = ent.Type,
					Id = ent.Id,
					Position = destPos,
					Rotation = finalRot,
					Scale = ent.Scale,
					Player = ent.Player
				});

				AddMirroredRequests(result.SpawnRequests, ent.Type, ent.Id, destPos, finalRot, ent.Scale, ent.Player, mirrorMode, symmetryPivot, symmetryFolds);
			}
		}

		return result;
	}

	public void AddMirroredRequests(
		List<EntitySpawnRequest> requests,
		string type, string id,
		Godot.Vector3 pos, float rotation, float scale,
		int player,
		MirrorMode mirrorMode,
		Vector2 symmetryPivot,
		int symmetryFolds)
	{
		var mirrored = EditorGridSnapping.GetMirroredTransforms(pos, rotation, mirrorMode, symmetryPivot, symmetryFolds);
		foreach (var m in mirrored)
		{
			Godot.Vector3 mPos = m.Position;
			mPos.Y = EditorTerrainQueries.GetHeightAt(mPos);
			requests.Add(new EntitySpawnRequest { Type = type, Id = id, Position = mPos, Rotation = m.Rotation, Scale = scale, Player = player });
		}
	}

}
