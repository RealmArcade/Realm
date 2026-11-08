using System;
using Godot;
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

	public EditorPasteWriter(EditorTerrainSurface surface)
	{
		_surface = surface;
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
}
