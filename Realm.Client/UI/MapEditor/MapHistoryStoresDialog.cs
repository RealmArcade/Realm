using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class MapHistoryStoresDialog : FloatingDialogBase
{
	private Label _summaryLabel;
	private VBoxContainer _storeList;
	private IReadOnlyList<MapHistoryStoreInfo> _stores = Array.Empty<MapHistoryStoreInfo>();

	public MapHistoryStoresDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("MAP HISTORY STORAGE"), new Vector2(600, 480))
	{
		Name = "MapHistoryStoresDialog";
		SetFooterCloseOnly(TranslationServer.Translate("CLOSE"));
		BuildControls();
	}

	public override void OpenDialog()
	{
		RefreshStores();
		base.OpenDialog();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(360);

		AddDescription(
			scrollBody,
			TranslationServer.Translate(
				"The editor keeps a private copy of every map's save history here, separate from the map folders themselves. Removing a map's history frees the space it uses. The map folder is not touched, but earlier versions of that map can no longer be opened."));

		_summaryLabel = AddLabel(scrollBody, TranslationServer.Translate("Measuring stored histories..."), 12);
		_summaryLabel.Name = "SummaryLabel_Label";
		_summaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_summaryLabel.CustomMinimumSize = new Vector2(460, 0);
		_summaryLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		AddSectionHeader(scrollBody, TranslationServer.Translate("MAPS SORTED BY SIZE"), UIStyle.ColorGold);

		_storeList = new VBoxContainer();
		_storeList.Name = "StoreList_VBoxContainer";
		_storeList.AddThemeConstantOverride("separation", 4);
		_storeList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollBody.AddChild(_storeList);
	}

	private static string ResolveEditingDirectory()
	{
		return GameContext.EditorService._editingDirectory;
	}

	private void RefreshStores()
	{
		ClearChildren(_storeList);
		AddWrappedLabel(_storeList, TranslationServer.Translate("Measuring stored histories..."), "LoadingLabel_Label");

		GameContext.MapHistoryService.ReadStores(stores =>
		{
			Callable.From(() => PopulateStores(stores)).CallDeferred();
		});
	}

	private void PopulateStores(IReadOnlyList<MapHistoryStoreInfo> stores)
	{
		if (!GodotObject.IsInstanceValid(this) || _storeList == null || !GodotObject.IsInstanceValid(_storeList)) return;

		_stores = stores;
		ClearChildren(_storeList);

		if (_stores.Count == 0)
		{
			_summaryLabel.Text = TranslationServer.Translate("No map histories are stored yet.");
			AddWrappedLabel(_storeList, TranslationServer.Translate("Saving a map creates its first stored version."), "EmptyLabel_Label");
			return;
		}

		long totalBytes = 0;
		foreach (MapHistoryStoreInfo store in _stores)
		{
			totalBytes += store.TotalBytes;
		}

		_summaryLabel.Text = string.Format(
			TranslationServer.Translate("{0} map(s) use {1} in total."),
			_stores.Count,
			FormatBytes(totalBytes));

		foreach (MapHistoryStoreInfo store in _stores)
		{
			BuildStoreRow(store);
		}
	}

	private void BuildStoreRow(MapHistoryStoreInfo store)
	{
		var row = new HBoxContainer();
		row.Name = "StoreRow_HBoxContainer";
		row.AddThemeConstantOverride("separation", 8);
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_storeList.AddChild(row);

		var nameLabel = new Label();
		nameLabel.Name = "NameLabel_Label";
		nameLabel.Text = DescribeStoreName(store);
		nameLabel.AddThemeFontSizeOverride("font_size", 11);
		nameLabel.AddThemeColorOverride("font_color", IsOpenMapStore(store) ? new Color(0.98f, 0.9f, 0.6f) : new Color(0.93f, 0.93f, 0.9f));
		nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		nameLabel.ClipText = false;
		row.AddChild(nameLabel);

		var detailLabel = new Label();
		detailLabel.Name = "DetailLabel_Label";
		detailLabel.Text = DescribeStoreDetail(store);
		detailLabel.AddThemeFontSizeOverride("font_size", 10);
		detailLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.74f, 0.72f));
		detailLabel.CustomMinimumSize = new Vector2(150, 0);
		detailLabel.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(detailLabel);

		var sizeLabel = new Label();
		sizeLabel.Name = "SizeLabel_Label";
		sizeLabel.Text = FormatBytes(store.TotalBytes);
		sizeLabel.AddThemeFontSizeOverride("font_size", 10);
		sizeLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 0.7f));
		sizeLabel.CustomMinimumSize = new Vector2(74, 0);
		sizeLabel.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(sizeLabel);

		MapHistoryStoreInfo capturedStore = store;
		var deleteButton = AddButton(
			row,
			UnicodeIcons.TRASH,
			() => ConfirmDelete(capturedStore),
			TranslationServer.Translate("Remove this map's saved versions"),
			11,
			new Vector2(30, 22));
		deleteButton.Name = "BtnDeleteStore";
	}

	private void ConfirmDelete(MapHistoryStoreInfo store)
	{
		string message = string.Format(
			TranslationServer.Translate(
				"Remove the stored versions of '{0}'?\n\nThis frees {1}. The map folder itself is not touched and the map still opens and saves normally.\n\nAnything that exists only in this history is gone for good: earlier versions you could have gone back to, and any work this history recovered."),
			DescribeStoreName(store),
			FormatBytes(store.TotalBytes));

		Hud.ShowConfirmationDialog(
			message,
			() => RequestDelete(store),
			confirmText: TranslationServer.Translate("REMOVE"),
			cancelText: TranslationServer.Translate("CANCEL"));
	}

	private void RequestDelete(MapHistoryStoreInfo store)
	{
		GameContext.MapHistoryService.DeleteStore(store.DirectoryPath, ResolveEditingDirectory(), result =>
		{
			Callable.From(() =>
			{
				if (result.Success)
				{
					Hud.ShowFeedback(string.Format(
						TranslationServer.Translate("Removed the stored versions of '{0}'."),
						DescribeStoreName(store)));
				}
				else
				{
					Hud.ShowFeedback(string.Format(
						TranslationServer.Translate("Could not remove that history: {0}"),
						result.ErrorMessage ?? string.Empty));
				}

				RefreshStores();
			}).CallDeferred();
		});
	}

	private static bool IsOpenMapStore(MapHistoryStoreInfo store)
	{
		string editingDirectory = ResolveEditingDirectory();
		if (string.IsNullOrEmpty(editingDirectory)) return false;

		try
		{
			MapHistoryStoreLayout layout = MapHistoryStore.Resolve(editingDirectory);
			return string.Equals(Path.GetFullPath(layout.StoreDirectory), store.DirectoryPath, StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
		{
			return false;
		}
	}

	private static string DescribeStoreName(MapHistoryStoreInfo store)
	{
		string readableName = !string.IsNullOrWhiteSpace(store.MapName) ? store.MapName : DecodeStoreKey(store.StoreKey);
		if (store.IsRecovery)
		{
			return $"{readableName}  •  {TranslationServer.Translate("recovered copy")}";
		}

		return readableName;
	}

	private static string DecodeStoreKey(string storeKey)
	{
		int hashSeparatorIndex = storeKey.LastIndexOf('-');
		string readableName = hashSeparatorIndex > 0 ? storeKey[..hashSeparatorIndex] : storeKey;
		return readableName.Replace('-', ' ');
	}

	private static string DescribeStoreDetail(MapHistoryStoreInfo store)
	{
		if (store.IsRecovery) return TranslationServer.Translate("kept when its history was unreadable");
		if (store.IsUnreadable) return TranslationServer.Translate("could not be read");
		if (store.VersionCount == 0) return TranslationServer.Translate("no versions");

		string lastSaved = store.LastVersionAt.HasValue
			? store.LastVersionAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
			: string.Empty;
		return string.Format(TranslationServer.Translate("{0} version(s)  •  {1}"), store.VersionCount, lastSaved);
	}

	private void AddWrappedLabel(VBoxContainer parent, string text, string nodeName)
	{
		var label = AddLabel(parent, text, 10, new Color(0.75f, 0.76f, 0.75f));
		label.Name = nodeName;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(460, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
	}

	private static string FormatBytes(long byteCount)
	{
		if (byteCount < 1024) return string.Format(TranslationServer.Translate("{0} B"), byteCount);
		if (byteCount < 1024 * 1024) return string.Format(TranslationServer.Translate("{0} KB"), (byteCount / 1024.0).ToString("0.#"));
		if (byteCount < 1024L * 1024 * 1024) return string.Format(TranslationServer.Translate("{0} MB"), (byteCount / (1024.0 * 1024)).ToString("0.#"));
		return string.Format(TranslationServer.Translate("{0} GB"), (byteCount / (1024.0 * 1024 * 1024)).ToString("0.##"));
	}

	private static void ClearChildren(Node parent)
	{
		if (parent == null) return;

		foreach (Node child in parent.GetChildren())
		{
			parent.RemoveChild(child);
			child.QueueFree();
		}
	}
}
