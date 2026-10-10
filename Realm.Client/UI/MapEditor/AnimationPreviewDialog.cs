using Godot;
using Realm.Client.Animation;
using Realm.Client.Services;
using System;
using System.Collections.Generic;

namespace Realm.Client.UI.MapEditor;

public partial class AnimationPreviewDialog : FloatingPreview3DDialogBase
{
	private static readonly string[] StandardActionTypes = new[]
	{
		"Idle",
		"Walk",
		"Attack",
		"Death",
		"Labor",
		"Spell_Cast",
		"Dance"
	};

	private static readonly Dictionary<string, string> ActionIcons = new()
	{
		{ "Idle", "💤" },
		{ "Walk", "🚶" },
		{ "Attack", "⚔️" },
		{ "Death", "💀" },
		{ "Labor", "⚒️" },
		{ "Spell_Cast", "🪄" },
		{ "Dance", "💃" }
	};

	private Node3D _previewModelRoot;
	private AnimationPlayer _animPlayer;

	private LineEdit _txtPreviewRanim;
	private Action<string> _setPreviewRanimValue;
	private OptionButton _optTargetAction;
	private VBoxContainer _actionListContainer;
	private VBoxContainer _configuredAttachmentsContainer;

	private readonly Dictionary<string, Node3D> _socketAnchorNodes = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Node3D> _attachmentVisualNodes = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, bool> _attachmentVisibilities = new(StringComparer.OrdinalIgnoreCase);

	private Node _sourceSelectedObject;
	private string _currentUnitId = "";
	private string _currentPreviewRanim = "";
	private float _currentSpeed = 1.0f;

	private Dictionary<string, List<UnitAnimationEntry>> _workingAnimations = new(StringComparer.OrdinalIgnoreCase);
	private Dictionary<string, List<UnitAnimationEntry>> _initialAnimations = new(StringComparer.OrdinalIgnoreCase);

	public AnimationPreviewDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Unit Animation Studio"), new Vector2(500, 720))
	{
		DefaultDistance = 3.0f;
		CameraDistance = 3.0f;
		DefaultYaw = Mathf.DegToRad(30.0f);
		DefaultPitch = Mathf.DegToRad(15.0f);
		CameraYaw = Mathf.DegToRad(30.0f);
		CameraPitch = Mathf.DegToRad(15.0f);

		BuildControls();
	}

	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(480, 220));

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

		var presetRow = AddCameraPresetToolbar(topControlsVBox, includeBack: true);

		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		presetRow.AddChild(spacer);

		AddButton(presetRow, "▶ " + TranslationServer.Translate("Play"), () => PlayCurrentPreview(), "Play Animation", 10, new Vector2(0, 22));
		AddButton(presetRow, "⏸ " + TranslationServer.Translate("Pause"), () => PauseAnimation(), "Pause Animation", 10, new Vector2(0, 22));
		AddButton(presetRow, "⏹ " + TranslationServer.Translate("Stop"), () => StopAnimation(), "Stop Animation", 10, new Vector2(0, 22));

		var animInputSection = new VBoxContainer();
		animInputSection.AddThemeConstantOverride("separation", 4);

		AddSectionHeader(animInputSection, "🎬 " + TranslationServer.Translate("ANIMATION PREVIEW & ASSIGNMENT"), new Color(0.35f, 0.75f, 0.9f));

		(_txtPreviewRanim, _setPreviewRanimValue) = AddAssetFilterDropdown(
			animInputSection,
			TranslationServer.Translate("Preview .ranim:"),
			_currentPreviewRanim,
			(all) => ScanAvailableAssets("animations", all),
			(val) =>
			{
				_currentPreviewRanim = val ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(_currentPreviewRanim))
				{
					PlayAnimationFile(_currentPreviewRanim);
				}
			},
			TranslationServer.Translate("Select or search .ranim asset from metadata..."),
			130f
		);

		var attHeaderRow = new HBoxContainer();
		attHeaderRow.AddThemeConstantOverride("separation", 6);

		var lblAttHeader = new Label();
		lblAttHeader.Text = "📎 " + TranslationServer.Translate("PREVIEW ATTACHMENTS & SOCKETS");
		lblAttHeader.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.4f));
		lblAttHeader.AddThemeFontSizeOverride("font_size", 11);
		lblAttHeader.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		attHeaderRow.AddChild(lblAttHeader);

		AddButton(attHeaderRow, "📎 " + TranslationServer.Translate("Sockets & VFX Studio..."), () => OpenFullSocketStudio(), "Open full Socket & VFX studio to configure attachments, ground auras, overhead effects, and non-hand sockets", 10, new Vector2(0, 22));
		animInputSection.AddChild(attHeaderRow);

		_configuredAttachmentsContainer = new VBoxContainer();
		_configuredAttachmentsContainer.AddThemeConstantOverride("separation", 4);
		_configuredAttachmentsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		animInputSection.AddChild(_configuredAttachmentsContainer);

		var addActionRow = new HBoxContainer();
		addActionRow.AddThemeConstantOverride("separation", 6);

		var lblAssign = new Label();
		lblAssign.Text = TranslationServer.Translate("Assign to Action:");
		lblAssign.CustomMinimumSize = new Vector2(130, 0);
		lblAssign.AddThemeFontSizeOverride("font_size", 11);
		addActionRow.AddChild(lblAssign);

		_optTargetAction = new OptionButton();
		_optTargetAction.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optTargetAction.AddThemeFontSizeOverride("font_size", 11);
		for (int i = 0; i < StandardActionTypes.Length; i++)
		{
			string act = StandardActionTypes[i];
			string icon = ActionIcons.TryGetValue(act, out var ic) ? ic : "⚡";
			_optTargetAction.AddItem($"{icon} {act}", i);
		}
		addActionRow.AddChild(_optTargetAction);

		AddButton(addActionRow, "+ " + TranslationServer.Translate("Add to Action"), () => AddCurrentPreviewToSelectedAction(), "Add previewed animation into action's random array list", 11, new Vector2(120, 26));

		animInputSection.AddChild(addActionRow);
		topControlsVBox.AddChild(animInputSection);

		AddSectionHeader(BodyContainer, "📋 " + TranslationServer.Translate("CONFIGURED UNIT ANIMATIONS"), new Color(0.85f, 0.75f, 0.4f));

		var scrollBody = CreateScrollBody(250);
		_actionListContainer = new VBoxContainer();
		_actionListContainer.AddThemeConstantOverride("separation", 10);
		_actionListContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(_actionListContainer);
	}

	public void OpenForObject(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject)) return;

		_sourceSelectedObject = selectedObject;
		_currentUnitId = (selectedObject is Realm.Client.Unit3D unit) ? unit.UnitId : "";

		Node modelRoot = selectedObject;
		if (selectedObject is Realm.Client.Unit3D u && u.ModelNode != null)
		{
			modelRoot = u.ModelNode;
		}
		else if (selectedObject is Realm.Client.Prop3D prop)
		{
			modelRoot = prop.GetNodeOrNull<Node3D>("VisualModel") ?? selectedObject;
		}

		var validation = SkeletonValidator.Validate(modelRoot);
		if (!validation.IsValid)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Selected model is not a compatible rigged mesh."));
			return;
		}

		TitleLabel.Text = $"{TranslationServer.Translate("Unit Animation Studio")} - {selectedObject.Name}";

		InitWorkingAnimations();
		ClearPreviewModel();
		OpenDialog();
		ResetCameraDefault();

		SetupPreviewModel(modelRoot);
		RebuildConfiguredAttachmentsUI();

		SelectFirstAvailableOrAssignedAnimation();
		RebuildActionListUI();
	}

	private string ResolveModelPath(string unitId, string providedModelPath)
	{
		if (!string.IsNullOrEmpty(providedModelPath) || string.IsNullOrEmpty(unitId))
			return providedModelPath;

		if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(unitId, out var uMeta) && !string.IsNullOrEmpty(uMeta.ModelPath))
			return uMeta.ModelPath;

		try
		{
			string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			var metadata = MetadataService.Instance.LoadMetadata(wsPath);
			var unit = metadata.GetUnit(unitId);
			if (unit != null && !string.IsNullOrEmpty(unit.ModelPath))
			{
				return unit.ModelPath;
			}
		}
		catch { }

		return providedModelPath;
	}

	public void OpenForUnitId(string unitId, string modelPath = null)
	{
		_currentUnitId = unitId;
		TitleLabel.Text = $"{TranslationServer.Translate("Unit Animation Studio")} - {unitId}";

		modelPath = ResolveModelPath(unitId, modelPath);

		InitWorkingAnimations();
		ClearPreviewModel();

		OpenDialog();
		ResetCameraDefault();

		Node3D loadedModel = null;
		if (!string.IsNullOrEmpty(modelPath))
		{
			var loaded = ModelCache.GetModel(modelPath);
			if (loaded is Node3D node3D) loadedModel = node3D;
		}

		if (loadedModel != null)
		{
			SetupPreviewModel(loadedModel);
		}
		RebuildConfiguredAttachmentsUI();

		SelectFirstAvailableOrAssignedAnimation();
		RebuildActionListUI();
	}

	private void CopyAnimationsToWorking(Dictionary<string, List<UnitAnimationEntry>> sourceAnims)
	{
		if (sourceAnims == null) return;
		foreach (var kvp in sourceAnims)
		{
			var list = kvp.Value != null
				? new List<UnitAnimationEntry>(kvp.Value)
				: new List<UnitAnimationEntry>();
			_workingAnimations[kvp.Key] = list;
			_initialAnimations[kvp.Key] = new List<UnitAnimationEntry>(list);
		}
	}

	private void InitWorkingAnimations()
	{
		_workingAnimations.Clear();
		_initialAnimations.Clear();

		if (string.IsNullOrEmpty(_currentUnitId)) return;

		if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(_currentUnitId, out var uMeta))
		{
			CopyAnimationsToWorking(uMeta.Animations);
		}

		if (_workingAnimations.Count == 0)
		{
			try
			{
				string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
				var metadata = MetadataService.Instance.LoadMetadata(wsPath);
				var unit = metadata.GetUnit(_currentUnitId);
				if (unit != null)
				{
					CopyAnimationsToWorking(unit.Animations);
				}
			}
			catch { }
		}
	}

	private void BuildAttachmentCardUI(ObjectAttachmentDialog.ConfiguredAttachmentEntry entry, string key, bool isVisible)
	{
		var card = new PanelContainer();
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		row.AddChild(CreateVisibilityButton(key, isVisible));
		row.AddChild(CreateSocketBadge(entry));
		row.AddChild(CreateDisplayNameLabel(entry.AttachmentId));
		row.AddChild(CreateEditButton(entry));

		card.AddChild(row);
		_configuredAttachmentsContainer.AddChild(card);
	}

	private Button CreateVisibilityButton(string key, bool isVisible)
	{
		var btnEye = new Button();
		btnEye.Set("icon_max_width", 0);
		btnEye.Text = isVisible ? "👁️" : "🚫";
		btnEye.TooltipText = TranslationServer.Translate("Toggle attachment preview visibility");
		btnEye.CustomMinimumSize = new Vector2(28, 22);
		btnEye.FocusMode = Control.FocusModeEnum.None;

		string capturedKey = key;
		btnEye.Pressed += () => ToggleAttachmentVisibility(capturedKey, btnEye);
		return btnEye;
	}

	private void ToggleAttachmentVisibility(string key, Button btnEye)
	{
		bool curVis = !_attachmentVisibilities.TryGetValue(key, out bool v) || v;
		bool newVis = !curVis;
		_attachmentVisibilities[key] = newVis;
		btnEye.Text = newVis ? "👁️" : "🚫";

		if (_attachmentVisualNodes.TryGetValue(key, out var visualNode) && GodotObject.IsInstanceValid(visualNode))
		{
			visualNode.Visible = newVis;
		}
	}

	private Label CreateSocketBadge(ObjectAttachmentDialog.ConfiguredAttachmentEntry entry)
	{
		string parentAttId = entry.Orientation.ParentAttachmentId;
		bool isChild = !string.IsNullOrEmpty(parentAttId);
		string badgeText = isChild
			? $"[{entry.SocketId} ➔ {System.IO.Path.GetFileNameWithoutExtension(parentAttId)}]"
			: $"[{entry.SocketId}]";

		var badge = new Label
		{
			Text = badgeText,
			CustomMinimumSize = new Vector2(isChild ? 110 : 75, 0),
			ClipText = true
		};
		badge.AddThemeColorOverride("font_color", isChild ? new Color(0.9f, 0.6f, 1.0f) : UIStyle.ColorCyanGlow);
		badge.AddThemeFontSizeOverride("font_size", 10);
		return badge;
	}

	private Label CreateDisplayNameLabel(string attachmentId)
	{
		string displayName = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? $"✨ {attachmentId.Substring(4)}"
			: $"🗡️ {attachmentId}";

		var nameLbl = new Label
		{
			Text = displayName,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			ClipText = true
		};
		nameLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		nameLbl.AddThemeFontSizeOverride("font_size", 11);
		return nameLbl;
	}

	private Button CreateEditButton(ObjectAttachmentDialog.ConfiguredAttachmentEntry entry)
	{
		string capturedSocket = entry.SocketId;
		string capturedAtt = entry.AttachmentId;
		var btnEdit = new Button();
		btnEdit.Set("icon_max_width", 0);
		btnEdit.Text = "✏️";
		btnEdit.TooltipText = TranslationServer.Translate("Edit in Socket & VFX Studio");
		btnEdit.CustomMinimumSize = new Vector2(28, 22);
		btnEdit.FocusMode = Control.FocusModeEnum.None;
		btnEdit.Pressed += () =>
		{
			Hud?.OpenObjectAttachmentDialog(
				_currentUnitId,
				capturedAtt,
				capturedSocket,
				_previewModelRoot,
				(orientation) =>
				{
					SetupSocketAnchors();
					MountAllConfiguredAttachments();
					RebuildConfiguredAttachmentsUI();
				}
			);
		};
		return btnEdit;
	}

	private void RebuildConfiguredAttachmentsUI()
	{
		if (_configuredAttachmentsContainer == null) return;

		foreach (Node child in _configuredAttachmentsContainer.GetChildren())
		{
			child.QueueFree();
		}

		var configured = string.IsNullOrEmpty(_currentUnitId)
			? new List<ObjectAttachmentDialog.ConfiguredAttachmentEntry>()
			: ObjectAttachmentDialog.GetConfiguredAttachmentsForObject(_currentUnitId);

		if (configured.Count == 0)
		{
			var emptyLabel = new Label
			{
				Text = TranslationServer.Translate("No attachments configured on this unit.")
			};
			emptyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			emptyLabel.AddThemeFontSizeOverride("font_size", 11);
			_configuredAttachmentsContainer.AddChild(emptyLabel);
			return;
		}

		for (int i = 0; i < configured.Count; i++)
		{
			var entry = configured[i];
			string normSocket = ObjectAttachmentDialog.NormalizeSocketId(entry.SocketId);
			string key = ObjectAttachmentDialog.GetAttachmentKey(normSocket, entry.AttachmentId, entry.Index, entry.Orientation.ParentAttachmentId);

			bool isVisible = !_attachmentVisibilities.TryGetValue(key, out bool vis) || vis;

			BuildAttachmentCardUI(entry, key, isVisible);
		}
	}

	private void OpenFullSocketStudio()
	{
		Hud?.OpenObjectAttachmentDialog(
			_currentUnitId,
			null,
			"RightHand",
			_previewModelRoot,
			(orientation) =>
			{
				SetupSocketAnchors();
				MountAllConfiguredAttachments();
				RebuildConfiguredAttachmentsUI();
			}
		);
	}

	private void SelectFirstAvailableOrAssignedAnimation()
	{
		foreach (var act in StandardActionTypes)
		{
			if (_workingAnimations.TryGetValue(act, out var list) && list.Count > 0)
			{
				var entry = list[0];
				_currentPreviewRanim = entry.Animation;
				_setPreviewRanimValue?.Invoke(_currentPreviewRanim);
				PlayAnimationFile(_currentPreviewRanim);
				return;
			}
		}

		var allAvailable = ScanAvailableAssets("animations");
		if (allAvailable.Count > 0)
		{
			_currentPreviewRanim = allAvailable[0];
			_setPreviewRanimValue?.Invoke(_currentPreviewRanim);
			PlayAnimationFile(_currentPreviewRanim);
		}
	}

	private void BuildAnimItemUI(VBoxContainer cardVBox, string actionType, UnitAnimationEntry entry, int index)
	{
		string animFile = entry.Animation;

		var itemRow = new HBoxContainer();
		itemRow.AddThemeConstantOverride("separation", 6);

		var badge = new Label();
		badge.Text = $"{actionType}_{index}";
		badge.CustomMinimumSize = new Vector2(55, 0);
		badge.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		badge.AddThemeFontSizeOverride("font_size", 10);
		itemRow.AddChild(badge);

		var nameLbl = new Label();
		nameLbl.Text = animFile;
		nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		nameLbl.AddThemeFontSizeOverride("font_size", 11);
		nameLbl.ClipText = true;
		itemRow.AddChild(nameLbl);

		AddButton(itemRow, "▶ " + TranslationServer.Translate("Preview"), () =>
		{
			_currentPreviewRanim = entry.Animation;
			_setPreviewRanimValue?.Invoke(entry.Animation);

			for (int a = 0; a < StandardActionTypes.Length; a++)
			{
				if (StandardActionTypes[a].Equals(actionType, StringComparison.OrdinalIgnoreCase))
				{
					_optTargetAction.Selected = a;
					break;
				}
			}

			PlayAnimationFile(entry.Animation);
		}, "Preview this animation", 10, new Vector2(65, 22));

		AddButton(itemRow, "✕ " + TranslationServer.Translate("Remove"), () =>
		{
			RemoveAnimationFromAction(actionType, index);
		}, "Remove this animation from action array", 10, new Vector2(65, 22));

		cardVBox.AddChild(itemRow);
	}

	private void BuildActionCardUI(string actionType)
	{
		var actionCard = new PanelContainer();
		actionCard.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		var cardVBox = new VBoxContainer();
		cardVBox.AddThemeConstantOverride("separation", 6);
		actionCard.AddChild(cardVBox);

		var headerRow = new HBoxContainer();
		headerRow.AddThemeConstantOverride("separation", 6);

		string icon = ActionIcons.TryGetValue(actionType, out var ic) ? ic : "⚡";
		var lblHeader = new Label();
		lblHeader.Text = $"{icon} {actionType}";
		lblHeader.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		lblHeader.AddThemeFontSizeOverride("font_size", 12);
		lblHeader.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		headerRow.AddChild(lblHeader);

		_workingAnimations.TryGetValue(actionType, out var animList);
		int count = animList?.Count ?? 0;
		var countBadge = new Label();
		countBadge.Text = count == 1 ? "1 anim" : $"{count} anims";
		countBadge.AddThemeColorOverride("font_color", count > 0 ? UIStyle.ColorCyanGlow : UIStyle.ColorGoldDull);
		countBadge.AddThemeFontSizeOverride("font_size", 10);
		headerRow.AddChild(countBadge);

		cardVBox.AddChild(headerRow);

		if (animList == null || animList.Count == 0)
		{
			var fallbackLbl = new Label();
			fallbackLbl.Text = TranslationServer.Translate($"[Default fallback: {actionType.ToLowerInvariant()}.ranim]");
			fallbackLbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f, 0.7f));
			fallbackLbl.AddThemeFontSizeOverride("font_size", 11);
			cardVBox.AddChild(fallbackLbl);
		}
		else
		{
			for (int i = 0; i < animList.Count; i++)
			{
				BuildAnimItemUI(cardVBox, actionType, animList[i], i);
			}
		}

		_actionListContainer.AddChild(actionCard);
	}

	private void RebuildActionListUI()
	{
		if (_actionListContainer == null) return;

		foreach (Node child in _actionListContainer.GetChildren())
		{
			child.QueueFree();
		}

		foreach (string actionType in StandardActionTypes)
		{
			BuildActionCardUI(actionType);
		}
	}

	private void AddCurrentPreviewToSelectedAction()
	{
		string animFile = GetValidPreviewAnimationOrNull();
		if (animFile == null) return;

		string actionType = GetSelectedActionType();
		var list = GetOrCreateAnimationList(actionType);

		if (AnimationExistsInList(list, animFile))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("This animation already exists in this action."));
			return;
		}

		list.Add(new UnitAnimationEntry { Animation = animFile });

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Added {0} to {1}"), animFile, actionType));
		RebuildActionListUI();
	}

	private string GetValidPreviewAnimationOrNull()
	{
		string animFile = _currentPreviewRanim?.Trim();
		if (string.IsNullOrEmpty(animFile))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Please select an animation in the preview field first."));
			return null;
		}
		return animFile;
	}

	private string GetSelectedActionType()
	{
		if (_optTargetAction == null) return StandardActionTypes[0];
			
		int idx = _optTargetAction.Selected;
		if (idx < 0) return StandardActionTypes[0];
		if (idx >= StandardActionTypes.Length) return StandardActionTypes[0];
			
		return StandardActionTypes[idx];
	}

	private List<UnitAnimationEntry> GetOrCreateAnimationList(string actionType)
	{
		if (!_workingAnimations.TryGetValue(actionType, out var list))
		{
			list = new List<UnitAnimationEntry>();
			_workingAnimations[actionType] = list;
		}
		else if (list == null)
		{
			list = new List<UnitAnimationEntry>();
			_workingAnimations[actionType] = list;
		}
		return list;
	}

	private bool AnimationExistsInList(List<UnitAnimationEntry> list, string animFile)
	{
		foreach (var e in list)
		{
			if (string.Equals(e.Animation, animFile, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private void RemoveAnimationFromAction(string actionType, int index)
	{
		if (_workingAnimations.TryGetValue(actionType, out var list) && list != null && index >= 0 && index < list.Count)
		{
			var removed = list[index];
			list.RemoveAt(index);
			if (list.Count == 0)
			{
				_workingAnimations.Remove(actionType);
			}
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Removed {0} from {1}"), removed.Animation, actionType));
			RebuildActionListUI();
		}
	}

	private void ClearPreviewModel()
	{
		ClearAttachmentVisuals();
		_socketAnchorNodes.Clear();
		if (_previewModelRoot != null && GodotObject.IsInstanceValid(_previewModelRoot))
		{
			if (_animPlayer != null && GodotObject.IsInstanceValid(_animPlayer))
			{
				_animPlayer.Stop(true);
			}
			_previewModelRoot.QueueFree();
			_previewModelRoot = null;
			_animPlayer = null;
		}
	}

	private void ClearAttachmentVisuals()
	{
		foreach (var kvp in _attachmentVisualNodes)
		{
			if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
			{
				kvp.Value.GetParent()?.RemoveChild(kvp.Value);
				kvp.Value.QueueFree();
			}
		}
		_attachmentVisualNodes.Clear();
	}

	private void SetupPreviewModel(Node sourceModelRoot)
	{
		if (sourceModelRoot == null || PreviewSubViewport == null) return;

		var clonedNode = (Node3D)sourceModelRoot.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
		if (clonedNode == null) return;

		RemoveAllBoneAttachments(clonedNode);

		clonedNode.Position = Vector3.Zero;
		clonedNode.Rotation = Vector3.Zero;
		clonedNode.Scale = Vector3.One;

		PreviewSubViewport.AddChild(clonedNode);
		_previewModelRoot = clonedNode;

		if (_previewModelRoot.IsInsideTree())
		{
			_previewModelRoot.PropagateNotification((int)Node3D.NotificationTransformChanged);
		}

		FrameCameraOnNode(_previewModelRoot);

		SetupSocketAnchors();
		MountAllConfiguredAttachments();

		_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
	}

	private void SetupSocketAnchors()
	{
		_socketAnchorNodes.Clear();
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;

		Aabb modelAabb = CalculatePreviewModelAabb(_previewModelRoot);
		var skeleton = SkeletonValidator.FindSkeleton(_previewModelRoot);

		if (skeleton != null)
		{
			AddPseudoAnchor("Ground", new Vector3(0, modelAabb.Position.Y, 0));
			AddPseudoAnchor("Center", new Vector3(0, modelAabb.GetCenter().Y, 0));
			AddPseudoAnchor("Overhead", new Vector3(0, modelAabb.End.Y + 0.3f, 0));
			AddPseudoAnchor("Pivot", Vector3.Zero);

			AddBoneAnchor(skeleton, HumanoidBone.RightHand, "RightHand");
			AddBoneAnchor(skeleton, HumanoidBone.LeftHand, "LeftHand");
			AddBoneAnchor(skeleton, HumanoidBone.Chest, "Chest");
			AddBoneAnchor(skeleton, HumanoidBone.Hips, "Hips");
			AddBoneAnchor(skeleton, HumanoidBone.Head, "Head");
			AddBoneAnchor(skeleton, HumanoidBone.LeftFoot, "LeftFoot");
			AddBoneAnchor(skeleton, HumanoidBone.RightFoot, "RightFoot");
		}
		else
		{
			AddPseudoAnchor("Center", modelAabb.GetCenter());
			AddPseudoAnchor("Top", new Vector3(modelAabb.GetCenter().X, modelAabb.End.Y, modelAabb.GetCenter().Z));
			AddPseudoAnchor("Base", new Vector3(modelAabb.GetCenter().X, modelAabb.Position.Y, modelAabb.GetCenter().Z));
			AddPseudoAnchor("Pivot", Vector3.Zero);
		}
	}

	private void AddPseudoAnchor(string socketId, Vector3 pos)
	{
		string normSocket = ObjectAttachmentDialog.NormalizeSocketId(socketId);
		var anchor = new Node3D { Name = $"PreviewSocketAnchor_{normSocket}" };
		anchor.Position = pos;
		_previewModelRoot.AddChild(anchor);
		_socketAnchorNodes[normSocket] = anchor;
	}

	private void AddBoneAnchor(Skeleton3D skeleton, HumanoidBone bone, string socketId)
	{
		string normSocket = ObjectAttachmentDialog.NormalizeSocketId(socketId);
		int boneIdx = skeleton.FindBoneInSkeleton(bone);
		if (boneIdx >= 0)
		{
			var ba = new BoneAttachment3D
			{
				Name = $"PreviewBoneAttachment_{normSocket}",
				BoneName = skeleton.GetBoneName(boneIdx),
				BoneIdx = boneIdx
			};
			skeleton.AddChild(ba);
			_socketAnchorNodes[normSocket] = ba;
		}
	}

	private void MountAllConfiguredAttachments()
	{
		ClearAttachmentVisuals();
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;
		if (string.IsNullOrEmpty(_currentUnitId)) return;

		var configured = ObjectAttachmentDialog.GetConfiguredAttachmentsForObject(_currentUnitId);

		foreach (var entry in configured)
		{
			if (string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				MountAttachmentVisual(entry);
			}
		}

		foreach (var entry in configured)
		{
			if (!string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				MountAttachmentVisual(entry);
			}
		}
	}

	private Node3D ResolveAttachmentTarget(Node3D targetAnchor, string parentAttachmentId)
	{
		if (string.IsNullOrEmpty(parentAttachmentId))
			return targetAnchor;

		var parentMesh = Realm.Client.Unit3D.FindAttachmentInNode(targetAnchor, parentAttachmentId)
		                 ?? (_previewModelRoot != null ? Realm.Client.Unit3D.FindAttachmentInNode(_previewModelRoot, parentAttachmentId) : null);
		
		return parentMesh ?? targetAnchor;
	}

	private void MountAttachmentVisual(ObjectAttachmentDialog.ConfiguredAttachmentEntry entry)
	{
		string normSocket = ObjectAttachmentDialog.NormalizeSocketId(entry.SocketId);
		if (IsInvalidSocketAnchor(normSocket, out var targetAnchor)) return;

		if (IsInvalidAttachmentId(entry.AttachmentId)) return;

		Node3D loaded = Realm.Client.Unit3D.ResolveAndInstantiateAttachment(entry.AttachmentId, out _, out _, out _);
		if (loaded == null) return;

		string key = ObjectAttachmentDialog.GetAttachmentKey(normSocket, entry.AttachmentId, entry.Index, entry.Orientation.ParentAttachmentId);

		SetupAttachmentMetadata(loaded, entry.AttachmentId, key);
		ApplyAttachmentTransform(loaded, entry.Orientation);
		ApplyAttachmentVisibility(loaded, key);

		Node3D attachTarget = ResolveAttachmentTarget(targetAnchor, entry.Orientation.ParentAttachmentId);
		attachTarget.AddChild(loaded);
		_attachmentVisualNodes[key] = loaded;
	}

	private bool IsInvalidSocketAnchor(string normSocket, out Node3D targetAnchor)
	{
		if (!_socketAnchorNodes.TryGetValue(normSocket, out targetAnchor)) return true;
		if (targetAnchor == null) return true;
		if (!GodotObject.IsInstanceValid(targetAnchor)) return true;
		return false;
	}

	private bool IsInvalidAttachmentId(string attachmentId)
	{
		if (string.IsNullOrEmpty(attachmentId)) return true;
		if (attachmentId.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
		if (attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	private void SetupAttachmentMetadata(Node3D loaded, string attachmentId, string key)
	{
		string cleanAttId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: System.IO.Path.GetFileNameWithoutExtension(attachmentId);

		loaded.Name = $"AttVisual_{key}";
		loaded.SetMeta("AttachmentId", attachmentId);
		loaded.SetMeta("CleanAttachmentId", cleanAttId);
	}

	private void ApplyAttachmentTransform(Node3D loaded, Realm.Shared.Metadata.HandAttachmentOrientation orientation)
	{
		loaded.Position = orientation.Position.ToGodotVector3() + (loaded.Transform.Basis.Y * orientation.NormalOffset);
		loaded.RotationDegrees = orientation.RotationDegrees.ToGodotVector3();
			
		var scaleVec = orientation.ScaleVector.ToGodotVector3();
		if (scaleVec == Vector3.Zero)
		{
			float scale = orientation.Scale <= 0f ? 1.0f : orientation.Scale;
			loaded.Scale = Vector3.One * scale;
		}
		else
		{
			loaded.Scale = scaleVec;
		}
	}

	private void ApplyAttachmentVisibility(Node3D loaded, string key)
	{
		if (!_attachmentVisibilities.TryGetValue(key, out bool vis))
		{
			loaded.Visible = true;
		}
		else
		{
			loaded.Visible = vis;
		}
	}

	private static void UpdateAabbWithMeshCorners(ref Aabb combinedAabb, ref bool hasAabb, MeshInstance3D meshInst, Transform3D relXform)
	{
		Aabb mAabb = meshInst.Mesh.GetAabb();
		Vector3 min = mAabb.Position;
		Vector3 max = mAabb.End;
		Vector3[] corners = new[]
		{
			new Vector3(min.X, min.Y, min.Z),
			new Vector3(min.X, min.Y, max.Z),
			new Vector3(min.X, max.Y, min.Z),
			new Vector3(min.X, max.Y, max.Z),
			new Vector3(max.X, min.Y, min.Z),
			new Vector3(max.X, min.Y, max.Z),
			new Vector3(max.X, max.Y, min.Z),
			new Vector3(max.X, max.Y, max.Z)
		};
		for (int i = 0; i < 8; i++)
		{
			Vector3 pt = relXform * corners[i];
			if (!hasAabb)
			{
				combinedAabb = new Aabb(pt, Vector3.Zero);
				hasAabb = true;
			}
			else
			{
				combinedAabb = combinedAabb.Expand(pt);
			}
		}
	}

	private static Aabb CalculatePreviewModelAabb(Node3D root)
	{
		Aabb combinedAabb = new Aabb();
		bool hasAabb = false;

		void Collect(Node current)
		{
			if (current is MeshInstance3D meshInst && meshInst.Mesh != null && meshInst.Visible)
			{
				Transform3D relXform = root.GlobalTransform.AffineInverse() * meshInst.GlobalTransform;
				UpdateAabbWithMeshCorners(ref combinedAabb, ref hasAabb, meshInst, relXform);
			}
			foreach (Node child in current.GetChildren())
			{
				if (child is not BoneAttachment3D && !child.Name.ToString().StartsWith("PreviewSocketAnchor_") && !child.Name.ToString().StartsWith("AttVisual_"))
				{
					Collect(child);
				}
			}
		}

		Collect(root);
		if (!hasAabb)
		{
			combinedAabb = new Aabb(new Vector3(-0.5f, 0f, -0.5f), new Vector3(1.0f, 1.8f, 1.0f));
		}
		return combinedAabb;
	}

	private static void RemoveAllBoneAttachments(Node node)
	{
		if (node == null) return;
		var boneAttachments = new List<BoneAttachment3D>();
		CollectBoneAttachmentsRecursive(node, boneAttachments);
		foreach (var ba in boneAttachments)
		{
			ba.GetParent()?.RemoveChild(ba);
			ba.QueueFree();
		}
	}

	private static void CollectBoneAttachmentsRecursive(Node node, List<BoneAttachment3D> list)
	{
		if (node is BoneAttachment3D ba)
		{
			list.Add(ba);
			return;
		}
		foreach (Node child in node.GetChildren())
		{
			CollectBoneAttachmentsRecursive(child, list);
		}
	}

	private void PlayCurrentPreview()
	{
		if (!string.IsNullOrEmpty(_currentPreviewRanim))
		{
			PlayAnimationFile(_currentPreviewRanim);
		}
	}

	private void PauseAnimation()
	{
		if (_animPlayer != null && _animPlayer.IsPlaying())
		{
			_animPlayer.Pause();
		}
	}

	private void StopAnimation()
	{
		if (_animPlayer != null)
		{
			_animPlayer.Stop(true);
		}
	}

	private bool PlayExistingAnimation(string animName)
	{
		if (_animPlayer == null || !_animPlayer.HasAnimation(animName)) return false;

		var anim = _animPlayer.GetAnimation(animName);
		if (anim != null) anim.LoopMode = global::Godot.Animation.LoopModeEnum.Linear;
		_animPlayer.SpeedScale = _currentSpeed;
		_animPlayer.Play(animName);
		return true;
	}

	private RealmAnimationData LoadOrGetFallbackAnimationData(string animFileName, string animName)
	{
		string filePath = AnimationRetargetingService.ResolveAnimationFilePath(animFileName, _currentUnitId);
		if (!string.IsNullOrEmpty(filePath))
		{
			return AnimationRetargetingService.GetOrLoadRanimData(filePath);
		}

		return animName switch
		{
			"Idle" => RealmDefaultAnimations.Idle,
			"Walk" => RealmDefaultAnimations.Walk,
			"Attack" => RealmDefaultAnimations.Attack,
			"Death" => RealmDefaultAnimations.Death,
			"Labor" => RealmDefaultAnimations.Labor,
			"Spell_Cast" => RealmDefaultAnimations.Spell_Cast,
			"Dance" => RealmDefaultAnimations.Dance,
			_ => null
		};
	}

	public void PlayAnimationFile(string animFileName)
	{
		if (string.IsNullOrEmpty(animFileName)) return;
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;

		_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
		if (_animPlayer == null) return;

		string animName = System.IO.Path.GetFileNameWithoutExtension(animFileName);

		if (PlayExistingAnimation(animName)) return;

		RealmAnimationData animData = LoadOrGetFallbackAnimationData(animFileName, animName);

		if (animData != null && AnimationRetargetingService.RetargetAndBind(animData, _previewModelRoot, animName, out _))
		{
			_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
			PlayExistingAnimation(animName);
		}
	}

	protected override void OnApply()
	{
		if (!string.IsNullOrEmpty(_currentUnitId))
		{
			Hud?.SaveCustomUnitAnimations(_currentUnitId, _workingAnimations);
			Hud?.ShowFeedback(TranslationServer.Translate("Unit animations applied successfully."));
		}

		ClearPreviewModel();
	}

	protected override void OnCancel()
	{
		ClearPreviewModel();
	}

	public override void CloseDialog()
	{
		ClearPreviewModel();
		base.CloseDialog();
	}
}