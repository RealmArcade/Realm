using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Terrain;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class MapRefactorDialog : FloatingDialogBase
{
	public MapRefactorDialog() { }

	private HSlider _sldHeightRange = null!;
	private Label _lblHeightRange = null!;
	private CheckBox _chkResetTextures = null!;
	private HSlider _sldQuantize = null!;

	private TerrainCell[,]? _snapshotCells;
	private TerrainSplatWeights[,]? _snapshotGround;
	private TerrainSplatWeights[,]? _snapshotCliff;
	private bool _suppressLiveUpdate;

	public MapRefactorDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("MAP REFACTOR"), new Vector2(460, 320))
	{
		BuildUI();
	}

	private void BuildUI()
	{
		var contentVBox = CreateContentColumn();
		BodyContainer.AddChild(contentVBox);

		AddSectionHeader(contentVBox, "🪄 " + TranslationServer.Translate("MAP FILTERS"), new Color(0.35f, 0.75f, 0.9f));

		var lblInfo = new Label();
		lblInfo.Name = "LblInfo_Label";
		lblInfo.Text = TranslationServer.Translate("Every filter previews on the terrain as you change it. Cancel puts the map back, Apply keeps the preview as one undo step.");
		lblInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblInfo.CustomMinimumSize = new Vector2(400, 0);
		lblInfo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblInfo.AddThemeFontSizeOverride("font_size", 11);
		lblInfo.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		contentVBox.AddChild(lblInfo);

		(_sldHeightRange, _lblHeightRange) = AddSlider(contentVBox, TranslationServer.Translate("Scale height"), 1f, 16f, 0.25f, 1f, _ => ApplyLiveFilters(), "0.0#", 120f);
		_sldHeightRange.TooltipText = TranslationServer.Translate("Rescale the height difference between the lowest and the highest terrain point, leaving the lowest point where it is");

		_chkResetTextures = AddCheckBox(
			contentVBox,
			TranslationServer.Translate("Reset textures by height"),
			false,
			_ => ApplyLiveFilters(),
			TranslationServer.Translate("Paint every distinct height step with the next ground texture and the next cliff texture in turn"));
		_chkResetTextures.TooltipText = TranslationServer.Translate("Give each distinct height step its own pair of ground and cliff textures, wrapping around once the swatches run out");

		_sldQuantize = AddSlider(contentVBox, TranslationServer.Translate("Quantize"), 0f, 16f, 1f, 0f, _ => ApplyLiveFilters(), "0", 120f).Slider;
		_sldQuantize.TooltipText = TranslationServer.Translate("Repaint each terrain plane with its most common texture within this radius; 0 leaves textures alone");

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("APPLY");
	}

	public override void OpenDialog()
	{
		var terrain = GameContext.Host?.GroundTerrain;
		if (terrain?.SplatMap == null || !CaptureSnapshot(terrain))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("The editor has no terrain to refactor."));
			return;
		}

		(float lowest, float highest) = TerrainMapRefactor.MeasureHeightSpan(_snapshotCells!);
		float heightRange = highest - lowest;
		float sliderMaximum = MathF.Max(heightRange * 2f, 8f);

		_suppressLiveUpdate = true;

		_sldHeightRange.MinValue = 1f;
		_sldHeightRange.MaxValue = sliderMaximum;
		_sldHeightRange.Value = Math.Clamp(heightRange, 1f, sliderMaximum);
		_lblHeightRange.Text = heightRange.ToString("0.0#", CultureInfo.InvariantCulture);

		_chkResetTextures.ButtonPressed = false;
		_sldQuantize.Value = 0f;

		_suppressLiveUpdate = false;

		base.OpenDialog();
	}

	protected override void OnCancel()
	{
		var host = GameContext.Host;
		var terrain = host?.GroundTerrain;
		if (host == null || terrain == null) return;

		RestoreSnapshot(terrain);
		RefreshTerrain(host, terrain);
	}

	protected override void OnApply()
	{
		var host = GameContext.Host;
		var terrain = host?.GroundTerrain;
		if (host == null || terrain?.SplatMap == null || _snapshotCells == null || _snapshotGround == null) return;

		var afterCells = (TerrainCell[,])terrain.Cells.Clone();
		var afterGround = (TerrainSplatWeights[,])terrain.SplatMap.Clone();
		var afterCliff = terrain.CliffSplatMap is { } cliff ? (TerrainSplatWeights[,])cliff.Clone() : null;

		EditorHistoryManager.RecordAction(new TerrainModifyAction(
			_snapshotCells,
			afterCells,
			_snapshotGround,
			afterGround,
			beforePathing: null,
			afterPathing: null,
			beforeCliffSplatMap: _snapshotCliff,
			afterCliffSplatMap: afterCliff));

		host.EditorHasUnsavedChanges = true;
		Hud?.ShowFeedback(TranslationServer.Translate("Map refactor applied."));
	}

	private void ApplyLiveFilters()
	{
		if (_suppressLiveUpdate) return;

		var host = GameContext.Host;
		var terrain = host?.GroundTerrain;
		if (host == null || terrain?.SplatMap == null || _snapshotCells == null || _snapshotGround == null) return;

		RestoreSnapshot(terrain);

		TerrainCell[,] cells = terrain.Cells;
		TerrainSplatWeights[,] ground = terrain.SplatMap;
		TerrainSplatWeights[,]? cliff = terrain.CliffSplatMap;

		TerrainMapRefactor.ScaleHeights(cells, (float)_sldHeightRange.Value);

		if (_chkResetTextures.ButtonPressed)
		{
			TerrainMapRefactor.ResetTexturesByHeight(cells, ground, cliff, ResolveSwatchOrder());
		}

		TerrainMapRefactor.QuantizeTextures(cells, ground, cliff, (int)MathF.Round((float)_sldQuantize.Value));

		RefreshTerrain(host, terrain);
	}

	private bool CaptureSnapshot(RuntimeTerrain terrain)
	{
		if (terrain.SplatMap == null) return false;

		_snapshotCells = (TerrainCell[,])terrain.Cells.Clone();
		_snapshotGround = (TerrainSplatWeights[,])terrain.SplatMap.Clone();
		_snapshotCliff = terrain.CliffSplatMap is { } cliff ? (TerrainSplatWeights[,])cliff.Clone() : null;

		return true;
	}

	private void RestoreSnapshot(RuntimeTerrain terrain)
	{
		if (_snapshotCells == null || _snapshotGround == null) return;

		CopyInto(_snapshotCells, terrain.Cells);
		CopyInto(_snapshotGround, terrain.SplatMap);

		if (_snapshotCliff != null && terrain.CliffSplatMap != null)
		{
			CopyInto(_snapshotCliff, terrain.CliffSplatMap);
		}
	}

	private static void CopyInto<T>(T[,] source, T[,] target)
	{
		int width = Math.Min(source.GetLength(0), target.GetLength(0));
		int depth = Math.Min(source.GetLength(1), target.GetLength(1));

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				target[x, z] = source[x, z];
			}
		}
	}

	private static void RefreshTerrain(GameHost host, RuntimeTerrain terrain)
	{
		host.AlignAllEntitiesToTerrainExternal();
		terrain.UpdateMeshAndPhysics(true, false, (Rect2I?)null, true);
	}

	private static IReadOnlyList<int> ResolveSwatchOrder()
	{
		var swatchOrder = new List<int>();
		List<string> displayNames = GameContext.EditorService.Swatches.DisplayNames;

		for (int index = 0; index < displayNames.Count; index++)
		{
			string name = displayNames[index] ?? string.Empty;
			if (name.EndsWith("(Empty)", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			swatchOrder.Add(index);
		}

		return swatchOrder;
	}
}
