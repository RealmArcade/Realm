using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Client.UI.MapEditor;

public static class VirtualGridHelper
{
	public static void UpdateVirtualGridSize(Control scrollContainer, Control virtualGridContent, int itemCount, float gridPadding, float cellWidth, float cellHeight, float spacingX, float spacingY)
	{
		float availableWidth = Mathf.Max(100.0f, scrollContainer.Size.X - gridPadding * 2.0f);
		int columns = Math.Max(1, (int)((availableWidth + spacingX) / (cellWidth + spacingX)));
		int totalRows = (itemCount + columns - 1) / columns;
		UpdateVirtualGridHeight(virtualGridContent, itemCount, totalRows, gridPadding, cellHeight, spacingY);
	}

	public static void UpdateVirtualGridHeight(Control virtualGridContent, int itemCount, int totalRows, float gridPadding, float cellHeight, float spacingY)
	{
		float totalHeight = itemCount > 0
			? (gridPadding * 2.0f + totalRows * cellHeight + Math.Max(0, totalRows - 1) * spacingY)
			: 0;

		if (Math.Abs(virtualGridContent.CustomMinimumSize.Y - totalHeight) > 0.5f)
		{
			virtualGridContent.CustomMinimumSize = new Vector2(0, totalHeight);
		}
	}

	public static void HideAllGridCells(List<AssetGridCell> cellPool)
	{
		foreach (var cell in cellPool)
		{
			cell.Visible = false;
		}
	}

	public static void EnsureCellPoolCapacity(List<AssetGridCell> cellPool, Control virtualGridContent, int requiredCapacity)
	{
		while (cellPool.Count < requiredCapacity)
		{
			var newCell = new AssetGridCell();
			cellPool.Add(newCell);
			virtualGridContent.AddChild(newCell);
		}
	}

	public static void HideUnusedGridCells(List<AssetGridCell> cellPool, int visibleCount)
	{
		for (int i = visibleCount; i < cellPool.Count; i++)
		{
			cellPool[i].Visible = false;
		}
	}
}
