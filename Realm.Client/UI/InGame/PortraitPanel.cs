using System;
using System.Collections.Generic;
using System.Text;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;

namespace Realm.Client.UI.InGame;

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

	private readonly List<string> _lastProductionQueue = new();
	private readonly Dictionary<string, int> _unitTypeCounts = new();
	private readonly List<string> _compParts = new();
	private readonly StringBuilder _compStringBuilder = new();

	private readonly StyleBoxFlat[] _buttonStylesFocused3;
	private readonly StyleBoxFlat[] _buttonStylesFocused2;
	private readonly StyleBoxFlat[] _buttonStylesSelected3;
	private readonly StyleBoxFlat[] _healthBarFillStyles;
	private readonly ProgressBar[] _unitButtonHpBars;
	private readonly Label[] _unitButtonStatusLabels;

	private Entity _lastSingleEntity = Entity.Null;
	private string _lastSingleName;
	private string _lastSingleUnitId;
	private float _lastSingleHp = -1f;
	private float _lastSingleMaxHp = -1f;
	private float _lastSingleDamage = -1f;
	private float _lastSingleRange = -1f;
	private float _lastSingleDps = -1f;
	private float _lastSingleArmor = -1f;
	private float _lastSingleSpeed = -1f;
	private string _lastSingleStateText;
	private string _lastSingleDescription;

	private Entity _lastPropEntity = Entity.Null;
	private string _lastPropId;
	private float _lastPropRemaining = -1f;

	private enum SelectionType { None, Prop, Empty, Single, Multi }
	private SelectionType _lastSelectionType = SelectionType.None;

	private readonly string[] _lastUnitStatuses;

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

		int btnCount = _unitButtons.Count;
		_buttonStylesFocused3 = new StyleBoxFlat[btnCount];
		_buttonStylesFocused2 = new StyleBoxFlat[btnCount];
		_buttonStylesSelected3 = new StyleBoxFlat[btnCount];
		_healthBarFillStyles = new StyleBoxFlat[btnCount];
		_unitButtonHpBars = new ProgressBar[btnCount];
		_unitButtonStatusLabels = new Label[btnCount];
		_lastUnitStatuses = new string[btnCount];

		for (int i = 0; i < btnCount; i++)
		{
			int index = i;
			_unitButtons[i].Pressed += () => UnitSelectionButtonClicked?.Invoke(index);

			_unitButtonHpBars[i] = _unitButtons[i].GetNodeOrNull<ProgressBar>("HealthBar");
			_unitButtonStatusLabels[i] = _unitButtons[i].GetNodeOrNull<Label>("StatusIcon");

			_buttonStylesFocused3[i] = new StyleBoxFlat
			{
				BgColor = new Color(0, 0, 0, 0),
				BorderColor = new Color(0.95f, 0.82f, 0.55f, 1.0f)
			};
			_buttonStylesFocused3[i].SetBorderWidthAll(3);

			_buttonStylesFocused2[i] = new StyleBoxFlat
			{
				BgColor = new Color(0, 0, 0, 0),
				BorderColor = new Color(0.95f, 0.82f, 0.55f, 1.0f)
			};
			_buttonStylesFocused2[i].SetBorderWidthAll(2);

			_buttonStylesSelected3[i] = new StyleBoxFlat
			{
				BgColor = new Color(0, 0, 0, 0),
				BorderColor = new Color(0.1f, 0.8f, 0.2f, 0.8f)
			};
			_buttonStylesSelected3[i].SetBorderWidthAll(3);

			_healthBarFillStyles[i] = new StyleBoxFlat();
		}

		if (_portraitTexture != null)
		{
			_portraitTexture.MouseFilter = Control.MouseFilterEnum.Stop;
			_portraitTexture.GuiInput += HandlePortraitTextureInput;
		}
	}

	private void HandlePortraitTextureInput(InputEvent e)
	{
		if (!IsLeftClick(e) || !HasSelectedUnits()) return;
		InvokeSelection();
	}

	private bool IsLeftClick(InputEvent e)
	{
		return e is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left;
	}

	private bool HasSelectedUnits()
	{
		return GameContext.Host != null && GameContext.Host.SelectedUnits != null && GameContext.Host.SelectedUnits.Count > 0;
	}

	private void InvokeSelection()
	{
		int idx = GameContext.Host.CycleSelectionIndex;
		bool isValidIdx = idx >= 0 && idx < GameContext.Host.SelectedUnits.Count;
		UnitSelectionButtonClicked?.Invoke(isValidIdx ? idx : 0);
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

		if (viewModel.SelectedProp != null && GodotObject.IsInstanceValid(viewModel.SelectedProp) && GameContext.Host != null)
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

		var prop = viewModel.SelectedProp;
		float remainingAmount = GetPropRemainingAmount(prop);

		bool propChanged = _lastSelectionType != SelectionType.Prop || prop.Entity != _lastPropEntity || prop.PropId != _lastPropId || Math.Abs(remainingAmount - _lastPropRemaining) > 0.01f;
		if (propChanged)
		{
			_lastSelectionType = SelectionType.Prop;
			_lastPropEntity = prop.Entity;
			_lastPropId = prop.PropId;
			_lastPropRemaining = remainingAmount;

			string propDisplayName = GetPropDisplayName(prop.PropId);
			_unitNameLabel.Text = TranslationServer.Translate(propDisplayName);

			if (_portraitTexture != null)
			{
				string iconPath = GetPropIconPath(prop.PropId);
				_portraitTexture.Texture = RtexIconLoader.Load(iconPath);
			}

			_statsLabel.Text = $"{TranslationServer.Translate("REMAINING")}: {remainingAmount:F0}";
			_itemsBox.Visible = false;
			_productionBox.Visible = false;
		}
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
		return "res://Assets/UI/unit_placeholder.webp";
	}

	private float GetPropRemainingAmount(Prop3D selectedProp)
	{
		float remainingAmount = 0f;
		var ecsWorld = GameContext.World;
		var propEntity = selectedProp.Entity;
		if (ecsWorld.IsAlive(propEntity) && ecsWorld.Has<ResourceNode>(propEntity))
		{
			remainingAmount = ecsWorld.Get<ResourceNode>(propEntity).Amount;
		}

		return remainingAmount;
	}

	private void UpdateEmptySelection()
	{
		_statsContainer.Visible = false;
		_armyCompositionLabel?.Hide();

		if (_lastSelectionType != SelectionType.Empty)
		{
			_lastSelectionType = SelectionType.Empty;
			_unitNameLabel.Text = TranslationServer.Translate("No Selection");
			if (_portraitTexture != null)
			{
				_portraitTexture.Texture = RtexIconLoader.Load("res://Assets/UI/alliance_flag.webp");
			}
		}
	}

	private void UpdateSingleSelection(InGameHUDViewModel viewModel)
	{
		_unitsContainer.Visible = false;
		_statsContainer.Visible = true;
		_armyCompositionLabel?.Hide();

		var info = viewModel.SelectedUnits[viewModel.CycleSelectionIndex < viewModel.SelectedUnits.Count ? viewModel.CycleSelectionIndex : 0];

		bool unitHeaderChanged = _lastSelectionType != SelectionType.Single || info.Entity != _lastSingleEntity || info.UnitId != _lastSingleUnitId || info.Name != _lastSingleName;
		if (unitHeaderChanged)
		{
			_lastSelectionType = SelectionType.Single;
			_lastSingleEntity = info.Entity;
			_lastSingleUnitId = info.UnitId;
			_lastSingleName = info.Name;

			_unitNameLabel.Text = string.IsNullOrEmpty(info.Name) ? "" : TranslationServer.Translate(info.Name);
			if (_portraitTexture != null)
			{
				_portraitTexture.Texture = RtexIconLoader.Load(GetUnitIcon(info.UnitId));
			}
		}

		UpdateSingleSelectionStatsLabel(info, unitHeaderChanged);

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

	private void UpdateSingleSelectionStatsLabel(SelectedUnitInfo info, bool forceRefresh)
	{
		bool statsChanged = forceRefresh
			|| Math.Abs(info.Health - _lastSingleHp) > 0.1f
			|| Math.Abs(info.MaxHealth - _lastSingleMaxHp) > 0.1f
			|| Math.Abs(info.Damage - _lastSingleDamage) > 0.1f
			|| Math.Abs(info.Range - _lastSingleRange) > 0.1f
			|| Math.Abs(info.Dps - _lastSingleDps) > 0.1f
			|| Math.Abs(info.Armor - _lastSingleArmor) > 0.1f
			|| Math.Abs(info.Speed - _lastSingleSpeed) > 0.1f
			|| info.StateText != _lastSingleStateText
			|| info.Description != _lastSingleDescription;

		if (!statsChanged) return;

		_lastSingleHp = info.Health;
		_lastSingleMaxHp = info.MaxHealth;
		_lastSingleDamage = info.Damage;
		_lastSingleRange = info.Range;
		_lastSingleDps = info.Dps;
		_lastSingleArmor = info.Armor;
		_lastSingleSpeed = info.Speed;
		_lastSingleStateText = info.StateText;
		_lastSingleDescription = info.Description;

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
			statsText += $"\n\n{TranslationServer.Translate(info.Description)}";
		}

		_statsLabel.Text = statsText;
	}

	private void UpdateProductionBox(SelectedUnitInfo info)
	{
		if (info.ProductionQueue.Count > 0)
		{
			UpdateQueueProductionBox(info);
		}
		else
		{
			UpdateSimpleProductionBox(info);
		}
	}

	private void UpdateSimpleProductionBox(SelectedUnitInfo info)
	{
		_productionTitle.Text = info.ProductionTitle;
		_productionProgress.Visible = true;
		_productionProgress.Value = info.ProductionProgress;
		_productionProgress.MaxValue = info.ProductionMaxProgress;
		_productionQueueLabel.Text = $"{(int)(info.ProductionProgress / Math.Max(info.ProductionMaxProgress, 0.001f) * 100f)}%";
		if (_lastProductionQueue.Count > 0)
		{
			_lastProductionQueue.Clear();
			ClearQueueSlots();
		}
	}

	private void UpdateQueueProductionBox(SelectedUnitInfo info)
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

		if (_lastSelectionType != SelectionType.Multi)
		{
			_lastSelectionType = SelectionType.Multi;
			if (_portraitTexture != null)
			{
				_portraitTexture.Texture = RtexIconLoader.Load("res://Assets/UI/alliance_flag.webp");
			}
		}

		_unitNameLabel.Text = string.Format(TranslationServer.Translate("{0} Units Selected"), viewModel.SelectedUnits.Count);

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

	private void UpdateArmyCompositionLabel(List<SelectedUnitInfo> selectedUnits)
	{
		if (_armyCompositionLabel == null) return;

		_unitTypeCounts.Clear();
		foreach (var u in selectedUnits)
		{
			string tid = u.UnitId;
			if (!_unitTypeCounts.TryGetValue(tid, out int count)) count = 0;
			_unitTypeCounts[tid] = count + 1;
		}

		_compParts.Clear();
		foreach (var kv in _unitTypeCounts)
		{
			_compParts.Add($"{kv.Value}× {kv.Key}");
		}

		_compStringBuilder.Clear();
		for (int i = 0; i < _compParts.Count; i++)
		{
			if (i > 0) _compStringBuilder.Append(", ");
			_compStringBuilder.Append(_compParts[i]);
		}

		_armyCompositionLabel.Text = _compStringBuilder.ToString();
		_armyCompositionLabel.Show();
	}

	private void UpdateMultiSelectionUnitButton(int index, InGameHUDViewModel viewModel, string activeUnitId)
	{
		var btn = _unitButtons[index];
		var hpBar = _unitButtonHpBars[index];

		if (index < viewModel.SelectedUnits.Count)
		{
			var uInfo = viewModel.SelectedUnits[index];
			btn.Visible = true;
			btn.Icon = RtexIconLoader.Load(GetUnitIcon(uInfo.UnitId));
			btn.TooltipText = uInfo.UnitId.ToUpper();

			bool isFocused = index == viewModel.CycleSelectionIndex;
			bool inActiveSubGroup = uInfo.UnitId == activeUnitId;

			UpdateUnitButtonStyle(btn, index, isFocused, inActiveSubGroup);
			UpdateUnitButtonHealthBar(hpBar, index, uInfo);
		}
		else
		{
			btn.Visible = false;
		}
	}

	private void UpdateUnitButtonStyle(Button btn, int index, bool isFocused, bool inActiveSubGroup)
	{
		StyleBox targetStyle = inActiveSubGroup
			? (isFocused ? _buttonStylesFocused3[index] : _buttonStylesFocused2[index])
			: _buttonStylesSelected3[index];

		btn.AddThemeStyleboxOverride("normal", targetStyle);
	}

	private void UpdateUnitButtonHealthBar(ProgressBar hpBar, int index, SelectedUnitInfo uInfo)
	{
		if (hpBar == null) return;

		if (GameContext.Host != null && GameContext.World.IsAlive(uInfo.Entity))
		{
			hpBar.Visible = true;
			hpBar.MaxValue = uInfo.MaxHealth;
			hpBar.Value = uInfo.Health;

			float hpPct = uInfo.MaxHealth > 0 ? uInfo.Health / uInfo.MaxHealth : 1f;
			Color targetColor = hpPct < 0.35f ? new Color(0.9f, 0.2f, 0.1f)
				: hpPct < 0.7f ? new Color(0.9f, 0.7f, 0.1f)
				: new Color(0.1f, 0.85f, 0.2f);

			var fillStyle = _healthBarFillStyles[index];
			if (fillStyle.BgColor != targetColor)
			{
				fillStyle.BgColor = targetColor;
			}
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
			var statusLbl = _unitButtonStatusLabels[i];

			if (statusLbl == null) continue;

			if (i >= viewModel.SelectedUnits.Count)
			{
				if (_lastUnitStatuses[i] != null)
				{
					_lastUnitStatuses[i] = null;
					statusLbl.Visible = false;
				}
				continue;
			}

			var uInfo = viewModel.SelectedUnits[i];
			if (!GameContext.World.IsAlive(uInfo.Entity))
			{
				if (_lastUnitStatuses[i] != null)
				{
					_lastUnitStatuses[i] = null;
					statusLbl.Visible = false;
				}
				continue;
			}

			string status = GetUnitStatus(uInfo.Entity);
			if (status != _lastUnitStatuses[i])
			{
				_lastUnitStatuses[i] = status;
				statusLbl.Text = status;
				statusLbl.Visible = !string.IsNullOrEmpty(status);
			}
		}
	}

	private string GetUnitStatus(Entity entity)
	{
		var world = GameContext.World;
		if (world.Has<HoldPosition>(entity)) return "H";
		if (world.Has<Patrol>(entity)) return "P";
		if (world.Has<AttackMove>(entity)) return "A";
		if (world.Has<Follow>(entity)) return "F";
		if (world.Has<MoveTo>(entity)) return "M";
		return "";
	}

	private string GetUnitIcon(string unitId)
	{
		return "res://Assets/UI/unit_placeholder.webp";
	}

	private void PopulateQueueSlots(Entity producerEntity, List<string> unitIds)
	{
		if (_queueSlotsContainer == null) return;

		ClearQueueSlots();

		for (int i = 0; i < unitIds.Count; i++)
		{
			CreateQueueSlot(producerEntity, unitIds[i], i);
		}
	}

	private void CreateQueueSlot(Entity producerEntity, string unitId, int index)
	{
		string unitDisplayName = GetQueueSlotDisplayName(unitId);

		var slot = CreateSlotPanel(unitDisplayName);
		var icon = CreateSlotIcon(unitId);
		var btnCancel = CreateSlotCancelButton();

		slot.GuiInput += (InputEvent e) => HandleQueueSlotInput(e, producerEntity, index);

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
		slot.Name = "Slot_PanelContainer";
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
		icon.Name = "Icon_TextureRect";
		icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		icon.Texture = RtexIconLoader.Load(GetUnitIcon(unitId));
		icon.MouseFilter = Control.MouseFilterEnum.Pass;
		return icon;
	}

	private Button CreateSlotCancelButton()
	{
		var btnCancel = new Button();
		btnCancel.Name = "BtnCancel_Button";
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

	private void HandleQueueSlotInput(InputEvent e, Entity producerEntity, int index)
	{
		if (e is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed) return;
		if (mouseEvent.ButtonIndex != MouseButton.Left && mouseEvent.ButtonIndex != MouseButton.Right) return;
		if (GameContext.Host == null || !GameContext.World.IsAlive(producerEntity)) return;

		GameContext.Host.CancelQueuedUnitAt(producerEntity, index);
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
