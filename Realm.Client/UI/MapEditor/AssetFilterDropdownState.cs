using System;
using System.Collections.Generic;
using Fastenshtein;
using Godot;

namespace Realm.Client.UI.MapEditor;

public class AssetFilterDropdownState
{
	public LineEdit Txt { get; set; }
	public DropdownSuggestionList Suggestions { get; private set; }
	public string CommittedValue { get; set; }
	public bool IsUpdatingTextProgrammatically { get; set; }
	public bool IsPopupOpening { get; set; }
	public bool IsPopupSelectionInProgress { get; set; }
	public bool IsShowingAll { get; set; }
	public bool IncludeAllFolders { get; set; }
	public Func<bool, List<string>> ItemsProvider { get; set; }
	public Action<string> OnChanged { get; set; }

	public bool IsDropdownVisible => Suggestions != null && Suggestions.IsVisible;

	private string _previousText = string.Empty;

	public void InitializeDropdown(Node owner, string initialText)
	{
		_previousText = initialText ?? string.Empty;
		Suggestions = new DropdownSuggestionList(owner, Txt);
		Suggestions.ItemSelected += HandleItemSelected;
		Suggestions.Dismissed += HandleSuggestionsDismissed;
	}

	public void SyncProgrammaticText(string value)
	{
		_previousText = value ?? string.Empty;
	}

	public void FreeDropdown()
	{
		if (Suggestions == null) return;

		Suggestions.ItemSelected -= HandleItemSelected;
		Suggestions.Dismissed -= HandleSuggestionsDismissed;
		Suggestions.Free();
		Suggestions = null;
	}

	public void CommitSelection(string selected)
	{
		string safeSelected = selected ?? string.Empty;
		IsUpdatingTextProgrammatically = true;
		try
		{
			Txt.Text = safeSelected;
			Txt.CaretColumn = Txt.Text.Length;
		}
		finally
		{
			IsUpdatingTextProgrammatically = false;
		}

		_previousText = safeSelected;

		if (!string.Equals(CommittedValue, safeSelected, StringComparison.Ordinal))
		{
			CommittedValue = safeSelected;
			OnChanged(safeSelected);
		}
	}

	public void ShowAssetPopup(bool showAll = false)
	{
		if (Suggestions == null) return;

		IsShowingAll = showAll;

		string currentText = Txt.Text?.Trim() ?? string.Empty;
		string query = showAll ? string.Empty : currentText;
		var allItems = ItemsProvider(IncludeAllFolders);

		List<string> displayItems;
		if (allItems == null || allItems.Count == 0)
		{
			displayItems = new List<string>();
		}
		else
		{
			displayItems = GetDisplayItems(allItems, query, showAll);
		}

		bool looksLikeDeletion = currentText.Length < _previousText.Length
			&& _previousText.StartsWith(currentText, StringComparison.OrdinalIgnoreCase);
		_previousText = currentText;

		if (!showAll && !looksLikeDeletion && ShouldAutoComplete(displayItems, currentText))
		{
			CommitSelection(displayItems[0]);
			HideDropdown();
			Txt.ReleaseFocus();
			return;
		}

		IsPopupOpening = true;
		try
		{
			Suggestions.ShowItems(displayItems);
		}
		finally
		{
			Callable.From(() => IsPopupOpening = false).CallDeferred();
		}
	}

	public void HideDropdown()
	{
		IsShowingAll = false;
		Suggestions?.Hide();
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

	private static bool ShouldAutoComplete(List<string> displayItems, string query)
	{
		return displayItems.Count == 1
			&& !string.IsNullOrEmpty(query)
			&& displayItems[0].Length > query.Length
			&& displayItems[0].StartsWith(query, StringComparison.OrdinalIgnoreCase);
	}

	private void HandleItemSelected(int index, string selected)
	{
		IsPopupSelectionInProgress = true;
		CommitSelection(selected);
		HideDropdown();
		Callable.From(() => IsPopupSelectionInProgress = false).CallDeferred();
	}

	private void HandleSuggestionsDismissed()
	{
		IsShowingAll = false;
	}

	public void ResolveClosestMatch()
	{
		var allItems = ItemsProvider(IncludeAllFolders);
		string closest = FloatingDialogBase.FindClosestMatch(Txt.Text, allItems, CommittedValue);
		CommitSelection(closest);
		HideDropdown();
	}
}
