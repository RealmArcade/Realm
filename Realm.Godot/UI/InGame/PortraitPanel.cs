using Godot;
using System;
using System.Collections.Generic;
using Arch.Core;
using Realm.Godot.Utils;

public class PortraitPanel
{
	private PanelContainer _portraitFrame;
	private PanelContainer _selectionFrame;
	private HBoxContainer _unitsContainer;
	private List<Button> _unitButtons;
	private HBoxContainer _statsContainer;
	private Label _statsLabel;
	private VBoxContainer _itemsBox;
	private VBoxContainer _productionBox;
	private Label _productionTitle;
	private ProgressBar _productionProgress;
	private Label _productionQueueLabel;
	private HBoxContainer _queueSlotsContainer;
	private Label _armyCompositionLabel;
	private Label _unitNameLabel;
	private TextureRect _portraitTexture;

	private List<string> _lastProductionQueue = new();

	public event Action<int> UnitSelectionButtonClicked;

	public PortraitPanel(PanelContainer portraitFrame, PanelContainer selectionFrame, 
		HBoxContainer unitsContainer, List<Button> unitButtons, HBoxContainer statsContainer,
		Label statsLabel, VBoxContainer itemsBox,
		VBoxContainer productionBox, Label productionTitle, ProgressBar productionProgress,
		Label productionQueueLabel, HBoxContainer queueSlotsContainer, Label armyCompositionLabel,
		Label unitNameLabel, TextureRect portraitTexture)
	{
		_portraitFrame = portraitFrame;
		_selectionFrame = selectionFrame;
		_unitsContainer = unitsContainer;
		_unitButtons = unitButtons;
		_statsContainer = statsContainer;
		_statsLabel = statsLabel;
		_itemsBox = itemsBox;
		_productionBox = productionBox;
		_productionTitle = productionTitle;
		_productionProgress = productionProgress;
		_productionQueueLabel = productionQueueLabel;
		_queueSlotsContainer = queueSlotsContainer;
		_armyCompositionLabel = armyCompositionLabel;
		_unitNameLabel = unitNameLabel;
		_portraitTexture = portraitTexture;

		for (int i = 0; i < _unitButtons.Count; i++)
		{
			int index = i;
			_unitButtons[i].Pressed += () => UnitSelectionButtonClicked?.Invoke(index);
		}

		if (_portraitTexture != null)
		{
			_portraitTexture.MouseFilter = Control.MouseFilterEnum.Stop;
			_portraitTexture.GuiInput += HandlePortraitTextureInput;
		}
	}

    private void HandlePortraitTextureInput(InputEvent e)
    {
        if (e is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (GameHost.Instance == null || GameHost.Instance.SelectedUnits == null || GameHost.Instance.SelectedUnits.Count == 0)
        {
            return;
        }

        int idx = GameHost.Instance.CycleSelectionIndex;
        if (idx >= 0 && idx < GameHost.Instance.SelectedUnits.Count)
        {
            UnitSelectionButtonClicked?.Invoke(idx);
        }
        else
        {
            UnitSelectionButtonClicked?.Invoke(0);
        }
    }

	public void Update(InGameHUDViewModel viewModel)
	{
		if (viewModel.SelectedUnits.Count == 0)
		{
            UpdateNoSelection(viewModel);
		}
		else if (viewModel.SelectedUnits.Count == 1)
		{
            UpdateSingleSelection(viewModel);
		}
		else
		{
            UpdateMultiSelection(viewModel);
		}

		if (_unitsContainer.Visible)
		{
            UpdateUnitStatusLabels(viewModel);
		}
	}

    private void UpdateNoSelection(InGameHUDViewModel viewModel)
	{
		_unitsContainer.Visible = false;
			
		if (viewModel.SelectedProp != null && GodotObject.IsInstanceValid(viewModel.SelectedProp) && GameHost.Instance != null)
		{
            UpdatePropSelection(viewModel);
		}
		else
		{
            UpdateEmptySelection();
		}
	}

    private void UpdatePropSelection(InGameHUDViewModel viewModel)
    {
		_statsContainer.Visible = true;
		_armyCompositionLabel?.Hide();
        
        string propDisplayName = GetPropDisplayName(viewModel.SelectedProp.PropId);
		_unitNameLabel.Text = TranslationServer.Translate(propDisplayName);

		if (_portraitTexture != null)
		{
			string iconPath = GetPropIconPath(viewModel.SelectedProp.PropId);
			_portraitTexture.Texture = RtexIconLoader.Load(iconPath);
		}

		float remainingAmount = GetPropRemainingAmount(viewModel.SelectedProp);

		_statsLabel.Text = $"{TranslationServer.Translate("REMAINING")}: {remainingAmount:F0}";
		_itemsBox.Visible = false;
		_productionBox.Visible = false;
    }

    private string GetPropDisplayName(string propId)
    {
        string propDisplayName = propId.ToUpper();
        if (GameHost.ResourceRegistry.TryGetValue(propId, out var rMeta) && !string.IsNullOrEmpty(rMeta.Name))
            propDisplayName = rMeta.Name.ToUpper();
        else if (GameHost.PropRegistry.TryGetValue(propId, out var pMeta) && !string.IsNullOrEmpty(pMeta.Name))
            propDisplayName = pMeta.Name.ToUpper();
        return propDisplayName;
    }

    private string GetPropIconPath(string propId)
    {
        string iconPath = propId switch
        {
            "goldmine" => "res://Assets/UI/goldmine_icon.png",
            "tree" => "res://Assets/UI/tree_icon.png",
            "rock" => "res://Assets/UI/rock_icon.png",
            _ => "res://Assets/UI/unit_placeholder.png"
        };
        // Fallback to placeholder if icon doesn't exist
        if (!ResourceLoader.Exists(iconPath)) iconPath = "res://Assets/UI/unit_placeholder.png";
        return iconPath;
    }

    private float GetPropRemainingAmount(Prop3D selectedProp)
    {
        float remainingAmount = 0f;
        var ecsWorld = GameHost.Instance.EcsWorld;
        var propEntity = selectedProp.Entity;
        if (ecsWorld.IsAlive(propEntity) && ecsWorld.Has<Realm.Ecs.Components.Resources.ResourceNode>(propEntity))
        {
            remainingAmount = ecsWorld.Get<Realm.Ecs.Components.Resources.ResourceNode>(propEntity).Amount;
        }
        return remainingAmount;
    }

    private void UpdateEmptySelection()
    {
        _statsContainer.Visible = false;
        _armyCompositionLabel?.Hide();
        
        _unitNameLabel.Text = TranslationServer.Translate("No Selection");
        if (_portraitTexture != null)
        {
            _portraitTexture.Texture = RtexIconLoader.Load("res://Assets/UI/alliance_flag.png");
        }
    }

    private void UpdateSingleSelection(InGameHUDViewModel viewModel)
    {
		_unitsContainer.Visible = false;
		_statsContainer.Visible = true;
		_armyCompositionLabel?.Hide();

		var info = viewModel.SelectedUnits[viewModel.CycleSelectionIndex < viewModel.SelectedUnits.Count ? viewModel.CycleSelectionIndex : 0];
		_unitNameLabel.Text = info.Name;
		if (_portraitTexture != null)
		{
			_portraitTexture.Texture = RtexIconLoader.Load(GetUnitIcon(info.UnitId));
		}

        UpdateSingleSelectionStatsLabel(info);

		if (info.IsBuilding)
		{
			_itemsBox.Visible = false;
		}

		if (info.IsBuilding && !info.IsEnemy && info.HasProduction)
		{
			_productionBox.Visible = true;
            UpdateProductionBox(info);
		}
		else
		{
			_productionBox.Visible = false;
			if (_lastProductionQueue.Count > 0)
			{
				_lastProductionQueue.Clear();
			}
		}
    }

    private void UpdateSingleSelectionStatsLabel(InGameHUDViewModel.SelectedUnitInfo info)
    {
		string statsText = $"{TranslationServer.Translate("HP")}: {info.Health:F0} / {info.MaxHealth:F0}";
		if (info.Damage > 0)
		{
			string label = TranslationServer.Translate("ATK");
			statsText += $"   {label}: {info.Damage:F0}   {TranslationServer.Translate("RNG")}: {info.Range:F0}";
			if (info.Dps > 0)
			{
				statsText += $"   {TranslationServer.Translate("DPS")}: {info.Dps:F1}";
			}
		}
		statsText += $"\n{TranslationServer.Translate("Armor")}: {info.Armor:F0}";
		if (info.Speed > 0) statsText += $"   {TranslationServer.Translate("Speed")}: {info.Speed:F0}";
		
		statsText += $"\n{info.StateText}";
		if (!string.IsNullOrEmpty(info.Description))
		{
			string description = info.Description.Length > 200
				? string.Concat(info.Description.AsSpan(0, 197), "...")
				: info.Description;
			statsText += $"\n\n{description}";
		}
		_statsLabel.Text = statsText;
    }

    private void UpdateProductionBox(InGameHUDViewModel.SelectedUnitInfo info)
    {
        if (info.UnitId != "castle")
        {
            UpdateNonCastleProductionBox(info);
        }
        else if (info.ProductionQueue.Count > 0)
        {
            UpdateCastleProductionBox(info);
        }
    }

    private void UpdateNonCastleProductionBox(InGameHUDViewModel.SelectedUnitInfo info)
    {
        _productionTitle.Text = info.ProductionTitle;
        _productionProgress.Visible = true;
        _productionProgress.Value = info.ProductionProgress;
        _productionProgress.MaxValue = info.ProductionMaxProgress;
        _productionQueueLabel.Text = $"{(int)(info.ProductionProgress / System.Math.Max(info.ProductionMaxProgress, 0.001f) * 100f)}%";
        if (_lastProductionQueue.Count > 0)
        {
            _lastProductionQueue.Clear();
            ClearQueueSlots();
        }
    }

    private void UpdateCastleProductionBox(InGameHUDViewModel.SelectedUnitInfo info)
    {
        _productionTitle.Text = info.ProductionTitle;
        _productionProgress.Visible = true;
        _productionProgress.Value = info.ProductionProgress;
        _productionProgress.MaxValue = info.ProductionMaxProgress;
        _productionQueueLabel.Text = string.Format(TranslationServer.Translate("Queue: {0}"), info.ProductionQueue.Count);
        
        bool queueChanged = HasProductionQueueChanged(info.ProductionQueue);
        if (queueChanged)
        {
            _lastProductionQueue.Clear();
            _lastProductionQueue.AddRange(info.ProductionQueue);
            PopulateQueueSlots(info.Entity, info.ProductionQueue);
        }
    }

    private bool HasProductionQueueChanged(List<string> productionQueue)
    {
        if (_lastProductionQueue.Count != productionQueue.Count)
        {
            return true;
        }

        for (int i = 0; i < productionQueue.Count; i++)
        {
            if (_lastProductionQueue[i] != productionQueue[i])
            {
                return true;
            }
        }
        return false;
    }

    private void UpdateMultiSelection(InGameHUDViewModel viewModel)
    {
		_unitsContainer.Visible = true;
		_statsContainer.Visible = false;

		_unitNameLabel.Text = string.Format(TranslationServer.Translate("{0} Units Selected"), viewModel.SelectedUnits.Count);
		if (_portraitTexture != null)
		{
			_portraitTexture.Texture = RtexIconLoader.Load("res://Assets/UI/alliance_flag.png");
		}

		UpdateArmyCompositionLabel(viewModel.SelectedUnits);

		int focusedIdx = viewModel.SelectedUnits.Count > 0
			? (viewModel.CycleSelectionIndex < viewModel.SelectedUnits.Count ? viewModel.CycleSelectionIndex : 0)
			: -1;
		string activeUnitId = focusedIdx >= 0 ? viewModel.SelectedUnits[focusedIdx].UnitId : null;

		for (int i = 0; i < _unitButtons.Count; i++)
		{
            UpdateMultiSelectionUnitButton(i, viewModel, activeUnitId);
		}
    }

    private void UpdateArmyCompositionLabel(List<InGameHUDViewModel.SelectedUnitInfo> selectedUnits)
    {
        if (_armyCompositionLabel == null) return;

        var unitTypeCounts = new Dictionary<string, int>();
        foreach (var u in selectedUnits)
        {
            string tid = u.UnitId;
            if (!unitTypeCounts.ContainsKey(tid)) unitTypeCounts[tid] = 0;
            unitTypeCounts[tid]++;
        }
        var compParts = new List<string>();
        foreach (var kv in unitTypeCounts)
            compParts.Add($"{kv.Value}× {kv.Key}");
        
        _armyCompositionLabel.Text = string.Join(", ", compParts);
        _armyCompositionLabel.Show();
    }

    private void UpdateMultiSelectionUnitButton(int index, InGameHUDViewModel viewModel, string activeUnitId)
    {
        var btn = _unitButtons[index];
        var hpBar = btn.GetNodeOrNull<ProgressBar>("HealthBar");

        if (index < viewModel.SelectedUnits.Count)
        {
            var uInfo = viewModel.SelectedUnits[index];
            btn.Visible = true;
            btn.Icon = RtexIconLoader.Load(GetUnitIcon(uInfo.UnitId));
            btn.TooltipText = uInfo.UnitId.ToUpper();

            bool isFocused = index == viewModel.CycleSelectionIndex;
            bool inActiveSubGroup = uInfo.UnitId == activeUnitId;

            UpdateUnitButtonStyle(btn, isFocused, inActiveSubGroup);
            UpdateUnitButtonHealthBar(hpBar, uInfo);
        }
        else
        {
            btn.Visible = false;
        }
    }

    private void UpdateUnitButtonStyle(Button btn, bool isFocused, bool inActiveSubGroup)
    {
        if (inActiveSubGroup)
        {
            var focusedBorder = new StyleBoxFlat();
            focusedBorder.BgColor = new Color(0, 0, 0, 0);
            focusedBorder.BorderColor = new Color(0.95f, 0.82f, 0.55f, 1.0f);
            focusedBorder.SetBorderWidthAll(isFocused ? 3 : 2);
            btn.AddThemeStyleboxOverride("normal", focusedBorder);
        }
        else
        {
            var selectedBorder = new StyleBoxFlat();
            selectedBorder.BgColor = new Color(0, 0, 0, 0);
            selectedBorder.BorderColor = new Color(0.1f, 0.8f, 0.2f, 0.8f);
            selectedBorder.SetBorderWidthAll(3);
            btn.AddThemeStyleboxOverride("normal", selectedBorder);
        }
    }

    private void UpdateUnitButtonHealthBar(ProgressBar hpBar, InGameHUDViewModel.SelectedUnitInfo uInfo)
    {
        if (hpBar == null) return;

        if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(uInfo.Entity))
        {
            hpBar.Visible = true;
            hpBar.MaxValue = uInfo.MaxHealth;
            hpBar.Value = uInfo.Health;

            float hpPct = uInfo.MaxHealth > 0 ? uInfo.Health / uInfo.MaxHealth : 1f;
            var fillStyle = new StyleBoxFlat();
            fillStyle.BgColor = hpPct < 0.35f ? new Color(0.9f, 0.2f, 0.1f)
                              : hpPct < 0.7f  ? new Color(0.9f, 0.7f, 0.1f)
                              : new Color(0.1f, 0.85f, 0.2f);
            hpBar.AddThemeStyleboxOverride("fill", fillStyle);
        }
        else
        {
            hpBar.Visible = false;
        }
    }

	private void UpdateUnitStatusLabels(InGameHUDViewModel viewModel)
    {
		for (int i = 0; i < _unitButtons.Count; i++)
		{
			var btn = _unitButtons[i];
			var statusLbl = btn.GetNodeOrNull<Label>("StatusIcon");
			
            if (statusLbl == null) continue;
            
            if (i >= viewModel.SelectedUnits.Count)
            {
                statusLbl.Visible = false;
                continue;
            }

			var uInfo = viewModel.SelectedUnits[i];
			if (!GameHost.Instance.EcsWorld.IsAlive(uInfo.Entity))
			{
				statusLbl.Visible = false;
				continue;
			}
			
            string status = GetUnitStatus(uInfo.Entity);
			statusLbl.Text = status;
			statusLbl.Visible = !string.IsNullOrEmpty(status);
		}
    }

    private string GetUnitStatus(Entity entity)
    {
        var world = GameHost.Instance.EcsWorld;
        if (world.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity))       return "H";
        if (world.Has<Realm.Ecs.Components.Movement.Patrol>(entity))         return "P";
        if (world.Has<Realm.Ecs.Components.Movement.AttackMove>(entity))     return "A";
        if (world.Has<Realm.Ecs.Components.Movement.Follow>(entity))         return "F";
        if (world.Has<Realm.Ecs.Components.Movement.MoveTo>(entity))         return "M";
        return "";
    }

	private string GetUnitIcon(string unitId)
	{
		return "res://Assets/UI/unit_placeholder.png";
	}

	private void PopulateQueueSlots(Entity castleEntity, List<string> unitIds)
	{
        if (_queueSlotsContainer == null) return;
        
		ClearQueueSlots();

		for (int i = 0; i < unitIds.Count; i++)
		{
            CreateQueueSlot(castleEntity, unitIds[i], i);
		}
	}

    private void CreateQueueSlot(Entity castleEntity, string unitId, int index)
    {
        string unitDisplayName = GetQueueSlotDisplayName(unitId);

        var slot = CreateSlotPanel(unitDisplayName);
        var icon = CreateSlotIcon(unitId);
        var btnCancel = CreateSlotCancelButton();

        slot.GuiInput += (InputEvent e) => HandleQueueSlotInput(e, castleEntity, index);

        slot.AddChild(icon);
        slot.AddChild(btnCancel);
        _queueSlotsContainer.AddChild(slot);
    }

    private string GetQueueSlotDisplayName(string unitId)
    {
        string unitDisplayName = unitId.ToUpper();
        if (GameHost.UnitRegistry.TryGetValue(unitId, out var uMeta) && !string.IsNullOrEmpty(uMeta.Name))
        {
            unitDisplayName = uMeta.Name;
        }
        return TranslationServer.Translate(unitDisplayName);
    }

    private PanelContainer CreateSlotPanel(string unitDisplayName)
    {
        var slot = new PanelContainer();
        slot.CustomMinimumSize = new Vector2(32, 32);
        slot.MouseFilter = Control.MouseFilterEnum.Stop;
        slot.TooltipText = $"{unitDisplayName}\n" + TranslationServer.Translate("Click to cancel & refund");

        var border = new StyleBoxFlat();
        border.BgColor = new Color(0, 0, 0, 0.4f);
        border.BorderColor = UIStyle.ColorBronze;
        border.SetBorderWidthAll(1);
        slot.AddThemeStyleboxOverride("panel", border);

        return slot;
    }

    private TextureRect CreateSlotIcon(string unitId)
    {
        var icon = new TextureRect();
        icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        icon.Texture = RtexIconLoader.Load(GetUnitIcon(unitId));
        icon.MouseFilter = Control.MouseFilterEnum.Pass;
        return icon;
    }

    private Button CreateSlotCancelButton()
    {
        var btnCancel = new Button();
        btnCancel.Text = "×";
        btnCancel.FocusMode = Control.FocusModeEnum.None;
        btnCancel.MouseFilter = Control.MouseFilterEnum.Ignore;
        btnCancel.AddThemeFontSizeOverride("font_size", 9);
        btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f));
        btnCancel.AddThemeColorOverride("font_outline_color", Colors.Black);
        btnCancel.AddThemeConstantOverride("outline_size", 3);
        btnCancel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        btnCancel.OffsetRight = 2;
        btnCancel.OffsetTop = -2;

        var styleEmpty = new StyleBoxEmpty();
        btnCancel.AddThemeStyleboxOverride("normal", styleEmpty);
        btnCancel.AddThemeStyleboxOverride("hover", styleEmpty);
        btnCancel.AddThemeStyleboxOverride("pressed", styleEmpty);
        btnCancel.AddThemeStyleboxOverride("focus", styleEmpty);

        return btnCancel;
    }

    private void HandleQueueSlotInput(InputEvent e, Entity castleEntity, int index)
    {
        if (e is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed) return;
        if (mouseEvent.ButtonIndex != MouseButton.Left && mouseEvent.ButtonIndex != MouseButton.Right) return;
        if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(castleEntity)) return;
        
        GameHost.Instance.CancelQueuedUnitAt(castleEntity, index);
    }

	private void ClearQueueSlots()
	{
		if (_queueSlotsContainer == null) return;
		foreach (Node child in _queueSlotsContainer.GetChildren())
		{
			_queueSlotsContainer.RemoveChild(child);
			child.QueueFree();
		}
	}
}
