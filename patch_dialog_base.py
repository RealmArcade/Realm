with open('Realm.Client/UI/MapEditor/FloatingDialogBase.cs', 'r') as f:
    content = f.read()

new_method = """	public (ItemList, LineEdit, Action<string>) AddAssetRuleList(
		VBoxContainer contentVBox,
		string itemName,
		string assetFilterType,
		string translationKeyId,
		ItemList.ItemSelectedEventHandler onItemSelected,
		Action onAddAction,
		Action onRemoveAction)
	{
		var listRow = new HBoxContainer();
		listRow.Name = $"{itemName}ListRow_HBoxContainer";
		var list = new ItemList { CustomMinimumSize = new Vector2(240, 75), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.ItemSelected += onItemSelected;
		listRow.AddChild(list);

		var btnBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var filterResult = AddAssetFilterDropdown(
			btnBox, string.Empty, string.Empty, (all) => ScanAvailableAssets(assetFilterType, all), (val) => { }, TranslationServer.Translate(translationKeyId), 0f);
		
		var txtId = filterResult.inputField;
		var setIdValue = filterResult.setValueAction;

		var btnAdd = new Button { Text = "+ " + TranslationServer.Translate($"Add {itemName}") };
		btnAdd.Set("icon_max_width", 0);
		btnAdd.Pressed += onAddAction;
		btnBox.AddChild(btnAdd);

		var btnRemove = new Button { Text = "✕ " + TranslationServer.Translate("Remove") };
		btnRemove.Set("icon_max_width", 0);
		btnRemove.Pressed += onRemoveAction;
		btnBox.AddChild(btnRemove);

		listRow.AddChild(btnBox);
		contentVBox.AddChild(listRow);

		return (list, txtId, setIdValue);
	}
"""

# Insert before AddTextInput
content = content.replace("	public LineEdit AddTextInput(", new_method + "\n	public LineEdit AddTextInput(")

with open('Realm.Client/UI/MapEditor/FloatingDialogBase.cs', 'w') as f:
    f.write(content)
