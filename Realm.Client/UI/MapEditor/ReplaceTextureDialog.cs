using Godot;

namespace Realm.Client.UI.MapEditor;

public partial class ReplaceTextureDialog : Realm.Client.UI.MapEditor.FloatingDialogBase
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
		if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null && Realm.Client.Core.GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			_snapshotSplatMap = (TerrainSplatWeights[,])Realm.Client.Core.GameHost.Instance.GroundTerrain.SplatMap.Clone();
			_snapshotCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap != null
				? (TerrainSplatWeights[,])Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap.Clone()
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
		int count = displayNames?.Count ?? 0;

		int defaultSourceIndex = Realm.Client.Core.GameHost.Instance?.EditorPaintTextureIndex ?? 0;
		int defaultTargetIndex = Realm.Client.Core.GameHost.Instance?.EditorCliffPaintTextureIndex ?? 0;

		PopulateDropdownItems(displayNames, count, defaultSourceIndex, defaultTargetIndex);
	}

	private void PopulateDropdownItems(System.Collections.Generic.IReadOnlyList<string> displayNames, int count, int defaultSourceIndex, int defaultTargetIndex)
	{
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

			if (i == defaultSourceIndex) selectedSourceItem = itemIndex;
			if (i == defaultTargetIndex) selectedTargetItem = itemIndex;

			itemIndex++;
		}

		if (_optSource.ItemCount > 0) _optSource.Selected = selectedSourceItem;
		if (_optTarget.ItemCount > 0) _optTarget.Selected = selectedTargetItem;
	}

	private bool TryGetValidState(out Realm.Client.RuntimeTerrain? groundTerrain, out int sourceSlot, out int targetSlot)
	{
		sourceSlot = -1;
		targetSlot = -1;
		groundTerrain = Realm.Client.Core.GameHost.Instance?.GroundTerrain;

		if (_snapshotSplatMap == null || groundTerrain?.SplatMap == null) return false;
		if (_optSource.Selected < 0 || _optTarget.Selected < 0) return false;

		var sourceMeta = _optSource.GetItemMetadata(_optSource.Selected);
		var targetMeta = _optTarget.GetItemMetadata(_optTarget.Selected);

		if (sourceMeta.VariantType == Variant.Type.Nil || targetMeta.VariantType == Variant.Type.Nil) return false;

		sourceSlot = (int)sourceMeta;
		targetSlot = (int)targetMeta;
		return true;
	}

	private void ApplyLivePreview()
	{
		if (!TryGetValidState(out var groundTerrain, out int sourceSlot, out int targetSlot)) return;

		if (sourceSlot == targetSlot)
		{
			RestoreSplatMap(groundTerrain!);
		}
		else
		{
			ReplaceSplatMap(groundTerrain!, sourceSlot, targetSlot);
		}

		groundTerrain!.UpdateMeshAndPhysics(false, false, (Rect2I?)null, false);
	}

	private void RestoreSplatMap(Realm.Client.RuntimeTerrain groundTerrain)
	{
		int width = _snapshotSplatMap.GetLength(0);
		int depth = _snapshotSplatMap.GetLength(1);
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				groundTerrain.SplatMap[x, z] = _snapshotSplatMap[x, z];
			}
		}

		if (_snapshotCliffSplatMap == null || groundTerrain.CliffSplatMap == null) return;

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

	private void ReplaceSplatMap(Realm.Client.RuntimeTerrain groundTerrain, int sourceSlot, int targetSlot)
	{
		int width = _snapshotSplatMap.GetLength(0);
		int depth = _snapshotSplatMap.GetLength(1);
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				groundTerrain.SplatMap[x, z] = ReplaceIndexInSplat(_snapshotSplatMap[x, z], sourceSlot, targetSlot);
			}
		}

		if (_snapshotCliffSplatMap == null || groundTerrain.CliffSplatMap == null) return;

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

	protected override void OnCancel()
	{
		RevertToSnapshot();
	}

	protected override void OnApply()
	{
		if (!TryGetValidState(out var groundTerrain, out int sourceSlot, out int targetSlot)) return;
		if (sourceSlot == targetSlot) return;

		RecordTerrainAction(groundTerrain!);
		ShowFeedback();
	}

	private void RecordTerrainAction(Realm.Client.RuntimeTerrain groundTerrain)
	{
		var currentSplat = (TerrainSplatWeights[,])groundTerrain.SplatMap.Clone();
		var currentCliffSplat = groundTerrain.CliffSplatMap != null
			? (TerrainSplatWeights[,])groundTerrain.CliffSplatMap.Clone()
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
		if (Realm.Client.Core.GameHost.Instance != null)
		{
			Realm.Client.Core.GameHost.Instance.EditorHasUnsavedChanges = true;
		}
	}

	private void ShowFeedback()
	{
		string sourceName = _optSource.GetItemText(_optSource.Selected);
		string targetName = _optTarget.GetItemText(_optTarget.Selected);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Replaced {0} with {1}"), sourceName, targetName));
	}

	private void RevertToSnapshot()
	{
		var groundTerrain = Realm.Client.Core.GameHost.Instance?.GroundTerrain;
		if (_snapshotSplatMap == null || groundTerrain?.SplatMap == null) return;

		RestoreSplatMap(groundTerrain);
		groundTerrain.UpdateMeshAndPhysics(false, false, (Rect2I?)null, false);
	}

	private static TerrainSplatWeights ReplaceIndexInSplat(TerrainSplatWeights current, int sourceIndex, int targetIndex)
	{
		if (sourceIndex == targetIndex) return current;

		int[] indices = {
			current.Index0 == sourceIndex ? targetIndex : current.Index0,
			current.Index1 == sourceIndex ? targetIndex : current.Index1,
			current.Index2 == sourceIndex ? targetIndex : current.Index2,
			current.Index3 == sourceIndex ? targetIndex : current.Index3
		};

		float[] weights = { current.Weight0, current.Weight1, current.Weight2, current.Weight3 };

		AccumulateWeights(indices, weights);

		return new TerrainSplatWeights
		{
			Index0 = indices[0],
			Index1 = indices[1],
			Index2 = indices[2],
			Index3 = indices[3],
			Weight0 = weights[0],
			Weight1 = weights[1],
			Weight2 = weights[2],
			Weight3 = weights[3]
		};
	}

	private static void AccumulateWeights(int[] indices, float[] weights)
	{
		for (int i = 1; i < 4; i++)
		{
			for (int j = 0; j < i; j++)
			{
				if (indices[i] == indices[j])
				{
					weights[j] += weights[i];
					weights[i] = 0f;
					break;
				}
			}
		}
	}
}