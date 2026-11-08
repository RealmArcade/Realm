using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class ReplaceTextureDialog : FloatingDialogBase
{
	public ReplaceTextureDialog() { }

	private OptionButton _optSource = null!;
	private OptionButton _optTarget = null!;

	private readonly struct SplatSnapshot
	{
		public readonly TerrainSplatWeights[,] Splat;
		public readonly TerrainSplatWeights[,]? Cliff;

		public SplatSnapshot(TerrainSplatWeights[,] splat, TerrainSplatWeights[,]? cliff)
		{
			Splat = splat;
			Cliff = cliff;
		}
	}

	private readonly struct SplatSwap
	{
		public readonly RuntimeTerrain GroundTerrain;
		public readonly SplatSnapshot Snapshot;
		public readonly int SourceSlot;
		public readonly int TargetSlot;

		public SplatSwap(RuntimeTerrain groundTerrain, SplatSnapshot snapshot, int sourceSlot, int targetSlot)
		{
			GroundTerrain = groundTerrain;
			Snapshot = snapshot;
			SourceSlot = sourceSlot;
			TargetSlot = targetSlot;
		}
	}

	private SplatSnapshot? _snapshot;

	public ReplaceTextureDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("REPLACE TERRAIN TEXTURE"), new Vector2(420, 200))
	{
		BuildUI();
	}

	private void BuildUI()
	{
		var contentVBox = CreateContentColumn();
		BodyContainer.AddChild(contentVBox);

		AddSectionHeader(contentVBox, "🔄 " + TranslationServer.Translate("TEXTURE SWAP SELECTION"), new Color(0.35f, 0.75f, 0.9f));

		var lblInfo = new Label();
		lblInfo.Name = "LblInfo_Label";
		lblInfo.Text = TranslationServer.Translate("Select a source texture and a target texture to replace all painted occurrences across the terrain map.");
		lblInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblInfo.CustomMinimumSize = new Vector2(380, 0);
		lblInfo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblInfo.AddThemeFontSizeOverride("font_size", 11);
		lblInfo.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		contentVBox.AddChild(lblInfo);

		var rowSource = new HBoxContainer();
		rowSource.Name = "RowSource_HBoxContainer";
		rowSource.AddThemeConstantOverride("separation", 8);

		var lblSource = new Label();
		lblSource.Name = "LblSource_Label";
		lblSource.Text = TranslationServer.Translate("Source Texture:");
		lblSource.CustomMinimumSize = new Vector2(110, 0);
		lblSource.AddThemeFontSizeOverride("font_size", 11);
		lblSource.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rowSource.AddChild(lblSource);

		_optSource = new OptionButton();
		_optSource.Name = "OptSource_OptionButton";
		_optSource.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optSource.CustomMinimumSize = new Vector2(0, 26);
		_optSource.AddThemeFontSizeOverride("font_size", 11);
		_optSource.FocusMode = FocusModeEnum.None;
		_optSource.ItemSelected += (_) => ApplyLivePreview();
		rowSource.AddChild(_optSource);
		contentVBox.AddChild(rowSource);

		var rowTarget = new HBoxContainer();
		rowTarget.Name = "RowTarget_HBoxContainer";
		rowTarget.AddThemeConstantOverride("separation", 8);

		var lblTarget = new Label();
		lblTarget.Name = "LblTarget_Label";
		lblTarget.Text = TranslationServer.Translate("Target Texture:");
		lblTarget.CustomMinimumSize = new Vector2(110, 0);
		lblTarget.AddThemeFontSizeOverride("font_size", 11);
		lblTarget.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rowTarget.AddChild(lblTarget);

		_optTarget = new OptionButton();
		_optTarget.Name = "OptTarget_OptionButton";
		_optTarget.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optTarget.CustomMinimumSize = new Vector2(0, 26);
		_optTarget.AddThemeFontSizeOverride("font_size", 11);
		_optTarget.FocusMode = FocusModeEnum.None;
		_optTarget.ItemSelected += (_) => ApplyLivePreview();
		rowTarget.AddChild(_optTarget);
		contentVBox.AddChild(rowTarget);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("APPLY");
	}

	public override void OpenDialog()
	{
		_snapshot = null;
		if (GameContext.Host?.GroundTerrain is { SplatMap: { } splatMap } groundTerrain)
		{
			_snapshot = new SplatSnapshot(
				(TerrainSplatWeights[,])splatMap.Clone(),
				groundTerrain.CliffSplatMap is { } cliffSplatMap ? (TerrainSplatWeights[,])cliffSplatMap.Clone() : null);
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

		var host = GameContext.Host;
		IReadOnlyList<string>? displayNames = host != null ? GameContext.EditorService.Swatches.DisplayNames : null;

		int defaultSourceIndex = host != null ? GameContext.EditorService.EditorPaintTextureIndex : 0;
		int defaultTargetIndex = host != null ? GameContext.EditorService.EditorCliffPaintTextureIndex : 0;

		PopulateDropdownItems(displayNames, defaultSourceIndex, defaultTargetIndex);
	}

	private void PopulateDropdownItems(IReadOnlyList<string>? displayNames, int defaultSourceIndex, int defaultTargetIndex)
	{
		int itemIndex = 0;
		int selectedSourceItem = 0;
		int selectedTargetItem = 0;

		int count = displayNames?.Count ?? 0;
		for (int i = 0; i < count; i++)
		{
			string? rawName = displayNames![i];
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

	private SplatSwap? BuildSwap()
	{
		if (_snapshot is not { } snapshot) return null;
		if (GameContext.Host?.GroundTerrain is not { SplatMap: not null } groundTerrain) return null;
		if (_optSource.Selected < 0 || _optTarget.Selected < 0) return null;

		var sourceMeta = _optSource.GetItemMetadata(_optSource.Selected);
		var targetMeta = _optTarget.GetItemMetadata(_optTarget.Selected);

		if (sourceMeta.VariantType == Variant.Type.Nil || targetMeta.VariantType == Variant.Type.Nil) return null;

		return new SplatSwap(groundTerrain, snapshot, (int)sourceMeta, (int)targetMeta);
	}

	private void ApplyLivePreview()
	{
		if (BuildSwap() is not { } swap) return;

		if (swap.SourceSlot == swap.TargetSlot)
		{
			RestoreSplatMap(swap.GroundTerrain, swap.Snapshot);
		}
		else
		{
			ReplaceSplatMap(swap.GroundTerrain, swap.Snapshot, swap.SourceSlot, swap.TargetSlot);
		}

		swap.GroundTerrain.UpdateMeshAndPhysics(false, false, (Rect2I?)null, false);
	}

	private static void RestoreSplatMap(RuntimeTerrain groundTerrain, SplatSnapshot snapshot)
	{
		if (groundTerrain.SplatMap is not { } splatTarget) return;

		int width = snapshot.Splat.GetLength(0);
		int depth = snapshot.Splat.GetLength(1);
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				splatTarget[x, z] = snapshot.Splat[x, z];
			}
		}

		if (snapshot.Cliff is not { } cliffSnapshot || groundTerrain.CliffSplatMap is not { } cliffTarget) return;

		int cliffWidth = cliffSnapshot.GetLength(0);
		int cliffDepth = cliffSnapshot.GetLength(1);
		for (int z = 0; z < cliffDepth; z++)
		{
			for (int x = 0; x < cliffWidth; x++)
			{
				cliffTarget[x, z] = cliffSnapshot[x, z];
			}
		}
	}

	private static void ReplaceSplatMap(RuntimeTerrain groundTerrain, SplatSnapshot snapshot, int sourceSlot, int targetSlot)
	{
		if (groundTerrain.SplatMap is not { } splatTarget) return;

		int width = snapshot.Splat.GetLength(0);
		int depth = snapshot.Splat.GetLength(1);
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				splatTarget[x, z] = ReplaceIndexInSplat(snapshot.Splat[x, z], sourceSlot, targetSlot);
			}
		}

		if (snapshot.Cliff is not { } cliffSnapshot || groundTerrain.CliffSplatMap is not { } cliffTarget) return;

		int cliffWidth = cliffSnapshot.GetLength(0);
		int cliffDepth = cliffSnapshot.GetLength(1);
		for (int z = 0; z < cliffDepth; z++)
		{
			for (int x = 0; x < cliffWidth; x++)
			{
				cliffTarget[x, z] = ReplaceIndexInSplat(cliffSnapshot[x, z], sourceSlot, targetSlot);
			}
		}
	}

	protected override void OnCancel()
	{
		RevertToSnapshot();
	}

	protected override void OnApply()
	{
		if (BuildSwap() is not { } swap) return;
		if (swap.SourceSlot == swap.TargetSlot) return;

		RecordTerrainAction(swap.GroundTerrain, swap.Snapshot);
		ShowFeedback();
	}

	private void RecordTerrainAction(RuntimeTerrain groundTerrain, SplatSnapshot snapshot)
	{
		if (groundTerrain.SplatMap is not { } liveSplat) return;

		var currentSplat = (TerrainSplatWeights[,])liveSplat.Clone();
		var currentCliffSplat = groundTerrain.CliffSplatMap is { } liveCliff
			? (TerrainSplatWeights[,])liveCliff.Clone()
			: null;

		TerrainCell[,]? noCells = null;

		var action = new TerrainModifyAction(
			noCells,
			noCells,
			snapshot.Splat,
			currentSplat,
			null,
			null,
			snapshot.Cliff,
			currentCliffSplat
		);

		EditorHistoryManager.RecordAction(action);
		if (GameContext.Host != null)
		{
			GameContext.Host.EditorHasUnsavedChanges = true;
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
		if (_snapshot is not { } snapshot) return;
		if (GameContext.Host?.GroundTerrain is not { } groundTerrain) return;

		RestoreSplatMap(groundTerrain, snapshot);
		groundTerrain.UpdateMeshAndPhysics(false, false, (Rect2I?)null, false);
	}

	private static TerrainSplatWeights ReplaceIndexInSplat(TerrainSplatWeights current, int sourceIndex, int targetIndex)
	{
		if (sourceIndex == targetIndex) return current;

		int[] indices =
		{
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