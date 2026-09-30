using Godot;
using System;
using System.Collections.Generic;

public partial class ReplaceTextureDialog : FloatingDialogBase
{
	private OptionButton _optSource;
	private OptionButton _optTarget;

	private TerrainSplatWeights[,]? _snapshotSplatMap;
	private TerrainSplatWeights[,]? _snapshotCliffSplatMap;

	public ReplaceTextureDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("REPLACE TERRAIN TEXTURE"), new Vector2(420, 200))
	{
		BuildUI();
	}

	private void BuildUI()
	{
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		BodyContainer.AddChild(contentVBox);

		AddSectionHeader(contentVBox, "🔄 " + TranslationServer.Translate("TEXTURE SWAP SELECTION"), new Color(0.35f, 0.75f, 0.9f));

		var lblInfo = new Label();
		lblInfo.Text = TranslationServer.Translate("Select a source texture and a target texture to replace all painted occurrences across the terrain map.");
		lblInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblInfo.CustomMinimumSize = new Vector2(380, 0);
		lblInfo.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		lblInfo.AddThemeFontSizeOverride("font_size", 11);
		lblInfo.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		contentVBox.AddChild(lblInfo);

		var rowSource = new HBoxContainer();
		rowSource.AddThemeConstantOverride("separation", 8);

		var lblSource = new Label();
		lblSource.Text = TranslationServer.Translate("Source Texture:");
		lblSource.CustomMinimumSize = new Vector2(110, 0);
		lblSource.AddThemeFontSizeOverride("font_size", 11);
		lblSource.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rowSource.AddChild(lblSource);

		_optSource = new OptionButton();
		_optSource.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optSource.CustomMinimumSize = new Vector2(0, 26);
		_optSource.AddThemeFontSizeOverride("font_size", 11);
		_optSource.FocusMode = Control.FocusModeEnum.None;
		_optSource.ItemSelected += (_) => ApplyLivePreview();
		rowSource.AddChild(_optSource);
		contentVBox.AddChild(rowSource);

		var rowTarget = new HBoxContainer();
		rowTarget.AddThemeConstantOverride("separation", 8);

		var lblTarget = new Label();
		lblTarget.Text = TranslationServer.Translate("Target Texture:");
		lblTarget.CustomMinimumSize = new Vector2(110, 0);
		lblTarget.AddThemeFontSizeOverride("font_size", 11);
		lblTarget.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rowTarget.AddChild(lblTarget);

		_optTarget = new OptionButton();
		_optTarget.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optTarget.CustomMinimumSize = new Vector2(0, 26);
		_optTarget.AddThemeFontSizeOverride("font_size", 11);
		_optTarget.FocusMode = Control.FocusModeEnum.None;
		_optTarget.ItemSelected += (_) => ApplyLivePreview();
		rowTarget.AddChild(_optTarget);
		contentVBox.AddChild(rowTarget);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("APPLY");
	}

	public override void OpenDialog()
	{
		if (GameHost.Instance?.GroundTerrain != null && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			_snapshotSplatMap = (TerrainSplatWeights[,])GameHost.Instance.GroundTerrain.SplatMap.Clone();
			_snapshotCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap != null
				? (TerrainSplatWeights[,])GameHost.Instance.GroundTerrain.CliffSplatMap.Clone()
				: null;
		}

		PopulateDropdowns();
		ApplyLivePreview();
		base.OpenDialog();
	}

	private void PopulateDropdowns()
	{
		if (_optSource == null || _optTarget == null) return;

		_optSource.Clear();
		_optTarget.Clear();

		var displayNames = Hud?.SwatchDisplayNames;
		int count = displayNames != null ? displayNames.Count : 0;

		int defaultSourceIndex = GameHost.Instance != null ? GameHost.Instance.EditorPaintTextureIndex : 0;
		int defaultTargetIndex = GameHost.Instance != null ? GameHost.Instance.EditorCliffPaintTextureIndex : 0;

		int itemIndex = 0;
		int selectedSourceItem = 0;
		int selectedTargetItem = 0;

		for (int i = 0; i < count; i++)
		{
			string rawName = displayNames![i];
			if (string.IsNullOrWhiteSpace(rawName) || rawName.EndsWith("(Empty)"))
			{
				continue;
			}

			string label = $"[{i}] {TranslationServer.Translate(rawName)}";
			_optSource.AddItem(label, itemIndex);
			_optSource.SetItemMetadata(itemIndex, i);

			_optTarget.AddItem(label, itemIndex);
			_optTarget.SetItemMetadata(itemIndex, i);

			if (i == defaultSourceIndex)
			{
				selectedSourceItem = itemIndex;
			}
			if (i == defaultTargetIndex)
			{
				selectedTargetItem = itemIndex;
			}

			itemIndex++;
		}

		if (_optSource.ItemCount > 0)
		{
			_optSource.Selected = selectedSourceItem;
		}
		if (_optTarget.ItemCount > 0)
		{
			_optTarget.Selected = selectedTargetItem;
		}
	}

	private void ApplyLivePreview()
	{
		if (_snapshotSplatMap == null || GameHost.Instance?.GroundTerrain == null || GameHost.Instance.GroundTerrain.SplatMap == null)
		{
			return;
		}

		if (_optSource.Selected < 0 || _optTarget.Selected < 0)
		{
			return;
		}

		var sourceMeta = _optSource.GetItemMetadata(_optSource.Selected);
		var targetMeta = _optTarget.GetItemMetadata(_optTarget.Selected);
		if (sourceMeta.VariantType == Variant.Type.Nil || targetMeta.VariantType == Variant.Type.Nil)
		{
			return;
		}

		int sourceSlot = (int)sourceMeta;
		int targetSlot = (int)targetMeta;

		var groundTerrain = GameHost.Instance.GroundTerrain;
		int width = _snapshotSplatMap.GetLength(0);
		int depth = _snapshotSplatMap.GetLength(1);

		if (sourceSlot == targetSlot)
		{
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					groundTerrain.SplatMap[x, z] = _snapshotSplatMap[x, z];
				}
			}

			if (_snapshotCliffSplatMap != null && groundTerrain.CliffSplatMap != null)
			{
				int cliffWidth = _snapshotCliffSplatMap.GetLength(0);
				int cliffDepth = _snapshotCliffSplatMap.GetLength(1);
				for (int z = 0; z < cliffDepth; z++)
				{
					for (int x = 0; x < cliffWidth; x++)
					{
						groundTerrain.CliffSplatMap[x, z] = _snapshotCliffSplatMap[x, z];
					}
				}
			}
		}
		else
		{
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					groundTerrain.SplatMap[x, z] = ReplaceIndexInSplat(_snapshotSplatMap[x, z], sourceSlot, targetSlot);
				}
			}

			if (_snapshotCliffSplatMap != null && groundTerrain.CliffSplatMap != null)
			{
				int cliffWidth = _snapshotCliffSplatMap.GetLength(0);
				int cliffDepth = _snapshotCliffSplatMap.GetLength(1);
				for (int z = 0; z < cliffDepth; z++)
				{
					for (int x = 0; x < cliffWidth; x++)
					{
						groundTerrain.CliffSplatMap[x, z] = ReplaceIndexInSplat(_snapshotCliffSplatMap[x, z], sourceSlot, targetSlot);
					}
				}
			}
		}

		groundTerrain.UpdateMeshAndPhysics(false, false, (Rect2I?)null, false);
	}

	protected override void OnCancel()
	{
		RevertToSnapshot();
	}

	protected override void OnApply()
	{
		if (_snapshotSplatMap == null || GameHost.Instance?.GroundTerrain == null || GameHost.Instance.GroundTerrain.SplatMap == null)
		{
			return;
		}

		if (_optSource.Selected < 0 || _optTarget.Selected < 0)
		{
			return;
		}

		var sourceMeta = _optSource.GetItemMetadata(_optSource.Selected);
		var targetMeta = _optTarget.GetItemMetadata(_optTarget.Selected);
		if (sourceMeta.VariantType == Variant.Type.Nil || targetMeta.VariantType == Variant.Type.Nil)
		{
			return;
		}

		int sourceSlot = (int)sourceMeta;
		int targetSlot = (int)targetMeta;

		if (sourceSlot != targetSlot)
		{
			var currentSplat = (TerrainSplatWeights[,])GameHost.Instance.GroundTerrain.SplatMap.Clone();
			var currentCliffSplat = GameHost.Instance.GroundTerrain.CliffSplatMap != null
				? (TerrainSplatWeights[,])GameHost.Instance.GroundTerrain.CliffSplatMap.Clone()
				: null;

			var action = new TerrainModifyAction(
				(Realm.Ecs.Components.Terrain.TerrainCell[,])null,
				(Realm.Ecs.Components.Terrain.TerrainCell[,])null,
				_snapshotSplatMap,
				currentSplat,
				null,
				null,
				_snapshotCliffSplatMap,
				currentCliffSplat
			);

			EditorHistoryManager.RecordAction(action);
			GameHost.Instance.EditorHasUnsavedChanges = true;

			string sourceName = _optSource.GetItemText(_optSource.Selected);
			string targetName = _optTarget.GetItemText(_optTarget.Selected);
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Replaced {0} with {1}"), sourceName, targetName));
		}
	}

	private void RevertToSnapshot()
	{
		if (_snapshotSplatMap == null || GameHost.Instance?.GroundTerrain == null || GameHost.Instance.GroundTerrain.SplatMap == null)
		{
			return;
		}

		var groundTerrain = GameHost.Instance.GroundTerrain;
		int width = _snapshotSplatMap.GetLength(0);
		int depth = _snapshotSplatMap.GetLength(1);

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				groundTerrain.SplatMap[x, z] = _snapshotSplatMap[x, z];
			}
		}

		if (_snapshotCliffSplatMap != null && groundTerrain.CliffSplatMap != null)
		{
			int cliffWidth = _snapshotCliffSplatMap.GetLength(0);
			int cliffDepth = _snapshotCliffSplatMap.GetLength(1);
			for (int z = 0; z < cliffDepth; z++)
			{
				for (int x = 0; x < cliffWidth; x++)
				{
					groundTerrain.CliffSplatMap[x, z] = _snapshotCliffSplatMap[x, z];
				}
			}
		}

		groundTerrain.UpdateMeshAndPhysics(false, false, (Rect2I?)null, false);
	}

	private static TerrainSplatWeights ReplaceIndexInSplat(TerrainSplatWeights current, int sourceIndex, int targetIndex)
	{
		if (sourceIndex == targetIndex) return current;

		int i0 = current.Index0 == sourceIndex ? targetIndex : current.Index0;
		int i1 = current.Index1 == sourceIndex ? targetIndex : current.Index1;
		int i2 = current.Index2 == sourceIndex ? targetIndex : current.Index2;
		int i3 = current.Index3 == sourceIndex ? targetIndex : current.Index3;

		float w0 = current.Weight0;
		float w1 = current.Weight1;
		float w2 = current.Weight2;
		float w3 = current.Weight3;

		if (i1 == i0) { w0 += w1; w1 = 0f; }
		if (i2 == i0) { w0 += w2; w2 = 0f; }
		else if (i2 == i1) { w1 += w2; w2 = 0f; }
		if (i3 == i0) { w0 += w3; w3 = 0f; }
		else if (i3 == i1) { w1 += w3; w3 = 0f; }
		else if (i3 == i2) { w2 += w3; w3 = 0f; }

		return new TerrainSplatWeights
		{
			Index0 = i0,
			Index1 = i1,
			Index2 = i2,
			Index3 = i3,
			Weight0 = w0,
			Weight1 = w1,
			Weight2 = w2,
			Weight3 = w3
		};
	}
}
