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

	public static void UpdateVisibleGridCells(Godot.ScrollContainer scrollContainer, Godot.Control virtualGridContent, System.Collections.Generic.List<Realm.Client.Services.IndexedAsset> matchingAssets, System.Collections.Generic.List<AssetGridCell> cellPool, float gridPadding, float cellWidth, float cellHeight, float spacingX, float spacingY, float defaultViewHeight, System.Action<int, int, int> bindVisibleGridCells)
	{
		float availableWidth = Godot.Mathf.Max(100.0f, scrollContainer.Size.X - gridPadding * 2.0f);
		int columns = System.Math.Max(1, (int)((availableWidth + spacingX) / (cellWidth + spacingX)));
		int totalRows = (matchingAssets.Count + columns - 1) / columns;

		UpdateVirtualGridHeight(virtualGridContent, matchingAssets.Count, totalRows, gridPadding, cellHeight, spacingY);

		if (matchingAssets.Count == 0)
		{
			HideAllGridCells(cellPool);
			return;
		}

		UpdateVisibleCellsForScroll(scrollContainer, virtualGridContent, matchingAssets.Count, cellPool, columns, totalRows, gridPadding, cellHeight, spacingY, defaultViewHeight, bindVisibleGridCells);
	}

	public static void UpdateVisibleCellsForScroll(Godot.ScrollContainer scrollContainer, Godot.Control virtualGridContent, int assetCount, System.Collections.Generic.List<AssetGridCell> cellPool, int columns, int totalRows, float gridPadding, float cellHeight, float spacingY, float defaultViewHeight, System.Action<int, int, int> bindVisibleGridCells)
	{
		float scrollY = (float)scrollContainer.GetVScrollBar().Value;
		float viewHeight = scrollContainer.Size.Y > 0 ? scrollContainer.Size.Y : defaultViewHeight;

		int startRow = System.Math.Max(0, (int)((scrollY - gridPadding) / (cellHeight + spacingY)) - 1);
		int endRow = System.Math.Min(totalRows - 1, (int)((scrollY + viewHeight - gridPadding) / (cellHeight + spacingY)) + 1);

		int startIndex = System.Math.Max(0, startRow * columns);
		int endIndex = System.Math.Min(assetCount - 1, (endRow + 1) * columns - 1);
		int visibleCount = endIndex >= startIndex ? (endIndex - startIndex + 1) : 0;

		EnsureCellPoolCapacity(cellPool, virtualGridContent, visibleCount);
		bindVisibleGridCells(startIndex, visibleCount, columns);
		HideUnusedGridCells(cellPool, visibleCount);
	}

	public static void BindVisibleGridCells(int startIndex, int visibleCount, int columns, System.Collections.Generic.List<Realm.Client.Services.IndexedAsset> matchingAssets, System.Collections.Generic.List<AssetGridCell> cellPool, float gridPadding, float cellWidth, float cellHeight, float spacingX, float spacingY, Realm.Client.Services.IndexedAsset selectedAsset, System.Action<Realm.Client.Services.IndexedAsset, bool> onAssetCellClicked)
	{
		for (int i = 0; i < visibleCount; i++)
		{
			int assetIndex = startIndex + i;
			var asset = matchingAssets[assetIndex];
			int row = assetIndex / columns;
			int col = assetIndex % columns;

			float posX = gridPadding + col * (cellWidth + spacingX);
			float posY = gridPadding + row * (cellHeight + spacingY);

			var cell = cellPool[i];
			cell.Position = new Godot.Vector2(posX, posY);
			cell.Size = new Godot.Vector2(cellWidth, cellHeight);
			cell.Visible = true;

			bool isSelected = selectedAsset != null && string.Equals(selectedAsset.FilePath, asset.FilePath, System.StringComparison.OrdinalIgnoreCase);
			cell.Bind(asset, isSelected, onAssetCellClicked);
		}
	}
}
