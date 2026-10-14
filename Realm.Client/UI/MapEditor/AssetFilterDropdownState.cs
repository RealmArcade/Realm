using System;
using System.Collections.Generic;
using Fastenshtein;
using Godot;

namespace Realm.Client.UI.MapEditor;

public class AssetFilterDropdownState
{
	public LineEdit Txt { get; set; }
	public PopupMenu Popup { get; set; }
	public string CommittedValue { get; set; }
	public bool IsUpdatingTextProgrammatically { get; set; }
	public bool IsPopupOpening { get; set; }
	public bool IsPopupSelectionInProgress { get; set; }
	public bool IsShowingAll { get; set; }
	public bool IncludeAllFolders { get; set; }
	public List<string> CurrentFilteredItems { get; set; } = new List<string>();
	public Func<bool, List<string>> ItemsProvider { get; set; }
	public Action<string> OnChanged { get; set; }

	public void CommitSelection(string selected)
	{
		string safeSelected = selected ?? string.Empty;
		IsUpdatingTextProgrammatically = true;
		try
		{
			Txt.Text = safeSelected;
		}
		finally
		{
			IsUpdatingTextProgrammatically = false;
		}

		if (!string.Equals(CommittedValue, safeSelected, StringComparison.Ordinal))
		{
			CommittedValue = safeSelected;
			OnChanged(safeSelected);
		}
	}

	public void PositionAndShowPopup()
	{
		if (!Txt.IsInsideTree()) return;

		IsPopupOpening = true;
		try
		{
			Vector2 globalPos = Txt.GlobalPosition;
			Vector2 txtSize = Txt.Size;
			Popup.Position = new Vector2I((int)globalPos.X, (int)(globalPos.Y + txtSize.Y + 2));
			Popup.ResetSize();
			if (Popup.Size.X < (int)txtSize.X)
			{
				Popup.Size = new Vector2I((int)txtSize.X, Popup.Size.Y);
			}

			Popup.Popup();
		}
		finally
		{
			Callable.From(() =>
			{
				IsPopupOpening = false;
				if (GodotObject.IsInstanceValid(Txt) && !Txt.HasFocus())
				{
					Txt.GrabFocus();
				}
			}).CallDeferred();
		}
	}

	public void ShowAssetPopup(bool showAll = false)
	{
		IsShowingAll = showAll;
		Popup.Clear();
		CurrentFilteredItems.Clear();

		var allItems = ItemsProvider(IncludeAllFolders);
		if (allItems == null || allItems.Count == 0)
		{
			Popup.AddItem(TranslationServer.Translate("No matching assets found"), 0);
			Popup.SetItemDisabled(0, true);
			PositionAndShowPopup();
			return;
		}

		string currentText = Txt.Text?.Trim() ?? string.Empty;
		string query = (showAll || (string.IsNullOrEmpty(currentText) && !showAll)) ? string.Empty : currentText;

		List<string> displayItems = GetDisplayItems(allItems, query, showAll);

		PopulatePopup(displayItems);
		PositionAndShowPopup();
	}

	private List<string> GetDisplayItems(List<string> allItems, string query, bool showAll)
	{
		if (showAll || string.IsNullOrEmpty(query)) return allItems;

		var lev = new Levenshtein(query.ToLowerInvariant());
		var scoredItems = new List<(string Item, int Score)>();
		for (int i = 0; i < allItems.Count; i++)
		{
			scoredItems.Add((allItems[i], FloatingDialogBase.ComputeMatchScore(allItems[i], query, lev)));
		}

		scoredItems.Sort((a, b) =>
		{
			int cmp = a.Score.CompareTo(b.Score);
			return cmp != 0 ? cmp : string.Compare(a.Item, b.Item, StringComparison.OrdinalIgnoreCase);
		});

		var genuineMatches = new List<string>();
		for (int i = 0; i < scoredItems.Count; i++)
		{
			if (scoredItems[i].Score < 1000) genuineMatches.Add(scoredItems[i].Item);
		}

		if (genuineMatches.Count > 0) return genuineMatches;

		var fallbackItems = new List<string>();
		int takeCount = Math.Min(5, scoredItems.Count);
		for (int i = 0; i < takeCount; i++)
		{
			fallbackItems.Add(scoredItems[i].Item);
		}

		return fallbackItems;
	}

	private void PopulatePopup(List<string> displayItems)
	{
		if (displayItems.Count == 0)
		{
			Popup.AddItem(TranslationServer.Translate("No matching assets found"), 0);
			Popup.SetItemDisabled(0, true);
			return;
		}

		int maxItems = Math.Min(displayItems.Count, 35);
		for (int i = 0; i < maxItems; i++)
		{
			CurrentFilteredItems.Add(displayItems[i]);
			Popup.AddItem(displayItems[i], i);
		}

		if (displayItems.Count > 35)
		{
			Popup.AddItem($"... ({displayItems.Count - 35} more)", 35);
			Popup.SetItemDisabled(35, true);
		}
	}

	public void ResolveClosestMatch()
	{
		var allItems = ItemsProvider(IncludeAllFolders);
		string closest = FloatingDialogBase.FindClosestMatch(Txt.Text, allItems, CommittedValue);
		CommitSelection(closest);
		Popup.Hide();
	}
}