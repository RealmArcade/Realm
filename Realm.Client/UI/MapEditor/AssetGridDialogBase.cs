using System;
using System.Collections.Generic;
using Godot;
using Realm.Shared.Textures;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public partial class AssetGridDialogBase : FloatingDialogBase
{
		public AssetGridDialogBase() { }
		public AssetGridDialogBase(MapEditorHUD hud, string title, Vector2 size) : base(hud, title, size) { }

	protected float CellWidth = 100.0f;
	protected float CellHeight = 100.0f;
	protected float SpacingX = 10.0f;
	protected float SpacingY = 10.0f;
	protected float GridPadding = 15.0f;

	protected readonly List<AssetGridCell> _cellPool = new();
	protected Container _virtualGridContent;
	protected ScrollContainer _scrollContainer;

	protected void EnsureCellPoolCapacity(int requiredCapacity)
	{
		while (_cellPool.Count < requiredCapacity)
		{
			var newCell = new AssetGridCell();
			_cellPool.Add(newCell);
			_virtualGridContent.AddChild(newCell);
		}
	}

	protected void BindVisibleGridCells(int startIndex, int visibleCount, int columns, List<IndexedAsset> matchingAssets, IndexedAsset selectedAsset, Action<IndexedAsset, bool> onCellClicked)
	{
		for (int i = 0; i < visibleCount; i++)
		{
			int assetIndex = startIndex + i;
			var asset = matchingAssets[assetIndex];
			int row = assetIndex / columns;
			int col = assetIndex % columns;

			float posX = GridPadding + col * (CellWidth + SpacingX);
			float posY = GridPadding + row * (CellHeight + SpacingY);

			var cell = _cellPool[i];
			cell.Position = new Vector2(posX, posY);
			cell.Size = new Vector2(CellWidth, CellHeight);
			cell.Visible = true;

			bool isSelected = selectedAsset != null && string.Equals(selectedAsset.FilePath, asset.FilePath, StringComparison.OrdinalIgnoreCase);
			cell.Bind(asset, isSelected, onCellClicked);
		}
	}

	protected void HideUnusedGridCells(int visibleCount)
	{
		for (int i = visibleCount; i < _cellPool.Count; i++)
		{
			_cellPool[i].Visible = false;
		}
	}

	protected void UpdateVirtualGridSize(int matchCount)
	{
		float availableWidth = Mathf.Max(100.0f, _scrollContainer.Size.X - GridPadding * 2.0f);
		int columns = Math.Max(1, (int)((availableWidth + SpacingX) / (CellWidth + SpacingX)));
		int totalRows = (matchCount + columns - 1) / columns;
		float totalHeight = matchCount > 0
			? (GridPadding * 2.0f + totalRows * CellHeight + Math.Max(0, totalRows - 1) * SpacingY)
			: 0;

		_virtualGridContent.CustomMinimumSize = new Vector2(0, totalHeight);
	}

	protected void UpdateVisibleCellsForScroll(ScrollContainer scrollContainer, int matchCount, out int startIndex, out int visibleCount, out int columns)
	{
		float availableWidth = Mathf.Max(100.0f, scrollContainer.Size.X - GridPadding * 2.0f);
		columns = Math.Max(1, (int)((availableWidth + SpacingX) / (CellWidth + SpacingX)));
		int totalRows = (matchCount + columns - 1) / columns;

		float scrollY = (float)scrollContainer.GetVScrollBar().Value;
		float viewHeight = scrollContainer.Size.Y;
		if (viewHeight <= 0)
		{
			viewHeight = 320.0f;
		}

		int startRow = Math.Max(0, (int)((scrollY - GridPadding) / (CellHeight + SpacingY)) - 1);
		int endRow = Math.Min(totalRows - 1, (int)((scrollY + viewHeight - GridPadding) / (CellHeight + SpacingY)) + 1);

		startIndex = Math.Max(0, startRow * columns);
		int endIndex = Math.Min(matchCount - 1, (endRow + 1) * columns - 1);
		visibleCount = endIndex >= startIndex ? (endIndex - startIndex + 1) : 0;
	}
}
