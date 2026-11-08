using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class ObjectAttachmentDialog : FloatingPreview3DDialogBase
{
	public ObjectAttachmentDialog() { }

	private Node3D? _previewModel;

	private readonly Dictionary<string, Node3D> _socketAnchorNodes = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Node3D> _socketModelNodes = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Node3D> _activeAttachmentVisuals = new(StringComparer.OrdinalIgnoreCase);
	private Node3D? _currentActiveModelNode;
	private Node3D? _uncommittedPreviewNode;
	private string? _uncommittedPreviewKey;
	private VBoxContainer _configuredAttachmentsContainer = null!;

	private Label _lblUnitTitle = null!;
	private Label _lblUnitValue = null!;
	private OptionButton _optSocket = null!;
	private OptionButton _optAttachmentPicker = null!;
	private OptionButton _optParent = null!;
	private string? _currentParentAttachmentId = null;
	private readonly List<string?> _availableParents = new();

	private HSlider _sliderPosX = null!;
	private HSlider _sliderPosY = null!;
	private HSlider _sliderPosZ = null!;
	private Label _lblPosX = null!;
	private Label _lblPosY = null!;
	private Label _lblPosZ = null!;

	private HSlider _sliderRotX = null!;
	private HSlider _sliderRotY = null!;
	private HSlider _sliderRotZ = null!;
	private Label _lblRotX = null!;
	private Label _lblRotY = null!;
	private Label _lblRotZ = null!;

	private HSlider _sliderScaleX = null!;
	private HSlider _sliderScaleY = null!;
	private HSlider _sliderScaleZ = null!;
	private Label _lblScaleX = null!;
	private Label _lblScaleY = null!;
	private Label _lblScaleZ = null!;

	private HSlider _sliderNormalOffset = null!;
	private Label _lblNormalOffset = null!;

	private bool _isTargetBuilding = false;
	private string _targetObjectId = string.Empty;
	private string _currentAttachmentId = string.Empty;
	private string _currentSocketId = "RightHand";

	private Vector3 _currentPosOffset = Vector3.Zero;
	private Vector3 _currentRotOffset = Vector3.Zero;
	private Vector3 _currentScaleOffset = Vector3.One;
	private float _currentNormalOffset = 0.0f;

	private List<string> _availableAttachments = new();
	private bool _isUpdatingUI;

	// Applied is signalled without an orientation: the dialog commits the stored attachment,
	// and the caller only needs to know that something changed.
	private Action<HandAttachmentOrientation?>? _onApplied;
	private UnitObjectAttachments? _initialSnapshot;
	private UnitObjectAttachments? _workingAttachments;

	private UnitObjectAttachments WorkingAttachments => _workingAttachments
		?? throw new InvalidOperationException($"{nameof(ObjectAttachmentDialog)} has no working attachment copy until a target is opened.");


	private static readonly SocketDefinition[] BoneSockets = new[]
	{
		new SocketDefinition("RightHand", "Right Hand (Weapon)", false, HumanoidBone.RightHand),
		new SocketDefinition("LeftHand", "Left Hand (Offhand/Shield)", false, HumanoidBone.LeftHand),
		new SocketDefinition("Chest", "Chest (Torso Bone)", false, HumanoidBone.Chest),
		new SocketDefinition("Hips", "Hips (Pelvis Bone)", false, HumanoidBone.Hips),
		new SocketDefinition("Head", "Head (Head Bone)", false, HumanoidBone.Head),
		new SocketDefinition("LeftFoot", "Left Foot", false, HumanoidBone.LeftFoot),
		new SocketDefinition("RightFoot", "Right Foot", false, HumanoidBone.RightFoot)
	};

	private readonly List<PseudoSocketAnchor> _workingPseudoSockets = new();
	private LineEdit _txtPseudoSocketName = null!;

	private readonly List<SocketDefinition> _currentAvailableSockets = new();

	public ObjectAttachmentDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Socket & VFX Attachment Studio"), new Vector2(580, 720))
	{
		DefaultDistance = 3.5f;
		CameraDistance = 3.5f;
		DefaultYaw = 0f;
		DefaultPitch = 0.2f;
		CameraYaw = 0f;
		CameraPitch = 0.2f;
		DefaultTargetPosition = new Vector3(0f, 1.0f, 0f);
		TargetPosition = DefaultTargetPosition;

		BuildControls();
	}


	private void AddOrUpdatePseudoSocketAnchor()
	{
		string anchorId = _txtPseudoSocketName?.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(anchorId))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Enter a name for the custom anchor."));
			return;
		}

		if (_socketAnchorNodes.TryGetValue(_currentSocketId, out var anchorNode) && anchorNode != null && GodotObject.IsInstanceValid(anchorNode))
		{
			Vector3 localPosition = anchorNode.Position + _currentPosOffset + (Vector3.Up * _currentNormalOffset);
			Vector3 localRotation = anchorNode.RotationDegrees + _currentRotOffset;

			_workingPseudoSockets.RemoveAll(anchor => anchor.SocketId.Equals(anchorId, StringComparison.OrdinalIgnoreCase));
			_workingPseudoSockets.Add(new PseudoSocketAnchor
			{
				SocketId = anchorId,
				Position = new Vector3Data(localPosition.X, localPosition.Y, localPosition.Z),
				RotationDegrees = new Vector3Data(localRotation.X, localRotation.Y, localRotation.Z)
			});

			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved anchor {0} on {1}."), anchorId, _targetObjectId));
			RebuildSocketsAndAttachments(anchorId);
			return;
		}

		Hud?.ShowFeedback(TranslationServer.Translate("Select a socket before saving an anchor."));
	}

	private void RemovePseudoSocketAnchor()
	{
		string anchorId = _txtPseudoSocketName?.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(anchorId))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Enter the name of the custom anchor to remove."));
			return;
		}

		int removed = _workingPseudoSockets.RemoveAll(anchor => anchor.SocketId.Equals(anchorId, StringComparison.OrdinalIgnoreCase));
		if (removed == 0)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Only custom anchors can be removed. Generated anchors are always available."));
			return;
		}

		PseudoSocketCache.Clear();
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Removed anchor {0} from {1}."), anchorId, _targetObjectId));
		RebuildSocketsAndAttachments(null);
	}

	private void RebuildSocketsAndAttachments(string? socketToSelect)
	{
		PseudoSocketCache.RegisterCustomSocketIds(_workingPseudoSockets);
		ClearSocketAnchors();
		SetupSockets(null);

		if (!string.IsNullOrEmpty(socketToSelect))
		{
			_currentSocketId = socketToSelect;
		}

		UpdateSocketDropdown();
		AttachAllConfiguredAttachments();
		PreviewCurrentAttachment();
	}

	private void ClearSocketAnchors()
	{
		foreach (var anchorNode in _socketAnchorNodes.Values)
		{
			if (anchorNode != null && GodotObject.IsInstanceValid(anchorNode))
			{
				anchorNode.GetParent()?.RemoveChild(anchorNode);
				anchorNode.QueueFree();
			}
		}

		_socketAnchorNodes.Clear();
		_socketModelNodes.Clear();
		_activeAttachmentVisuals.Clear();
		_currentActiveModelNode = null;
	}


	public void OpenForUnitAndAttachment(
		string? unitId,
		string? attachmentId = null,
		string? socket = null,
		Node3D? sourceModel = null,
		Action<HandAttachmentOrientation?>? onApplied = null)
	{
		OpenForTarget(unitId, attachmentId, socket, sourceModel, onApplied);
	}

	public void OpenForTarget(
		string? targetId,
		string? attachmentId = null,
		string? socket = null,
		Node3D? sourceModel = null,
		Action<HandAttachmentOrientation?>? onApplied = null)
	{
		_onApplied = onApplied;

		SetTargetObject(targetId);

		if (GameHost.TryGetUnitOrBuildingMetadata(_targetObjectId, out var meta))
		{
			_initialSnapshot = meta.ObjectAttachments?.Clone();
			LoadPseudoSocketsIntoWorkingCopy(meta.PseudoSockets);
		}
		else
		{
			_initialSnapshot = null;
			LoadPseudoSocketsIntoWorkingCopy(null);
		}

		_workingAttachments = _initialSnapshot?.Clone() ?? new UnitObjectAttachments();

		string defaultSocket = _isTargetBuilding ? "Center" : "RightHand";
		_currentSocketId = NormalizeSocketId(string.IsNullOrEmpty(socket) ? defaultSocket : socket);
		_currentAttachmentId = attachmentId ?? string.Empty;

		UpdateTitles();

		RefreshAttachmentList();
		LoadTargetModelAndRig(_targetObjectId, sourceModel);
		LoadAttachmentOrientationIntoSliders(_targetObjectId, _currentSocketId, _currentAttachmentId, _currentParentAttachmentId);
		UpdateParentDropdown();
		RebuildConfiguredAttachmentsUI();

		ResetCameraDefault();
		OpenDialog();
	}

	private void LoadPseudoSocketsIntoWorkingCopy(List<PseudoSocketAnchor>? anchors)
	{
		_workingPseudoSockets.Clear();

		if (anchors == null)
		{
			anchors = LoadPseudoSocketsFromPlaceableRegistries();
		}

		if (anchors != null)
		{
			foreach (var anchor in anchors)
			{
				if (anchor != null) _workingPseudoSockets.Add(anchor.Clone());
			}
		}

		PseudoSocketCache.RegisterCustomSocketIds(_workingPseudoSockets);
	}

	private List<PseudoSocketAnchor>? LoadPseudoSocketsFromPlaceableRegistries()
	{
		var registryService = GameContext.RegistryService;
		string cleanId = Path.GetFileNameWithoutExtension(_targetObjectId);

		if (registryService.PropRegistry != null)
		{
			if (registryService.PropRegistry.TryGetValue(_targetObjectId, out var propMeta)) return propMeta.PseudoSockets;
			if (registryService.PropRegistry.TryGetValue(cleanId, out var cleanPropMeta)) return cleanPropMeta.PseudoSockets;
		}

		if (registryService.ResourceRegistry != null)
		{
			if (registryService.ResourceRegistry.TryGetValue(_targetObjectId, out var resourceMeta)) return resourceMeta.PseudoSockets;
			if (registryService.ResourceRegistry.TryGetValue(cleanId, out var cleanResourceMeta)) return cleanResourceMeta.PseudoSockets;
		}

		return null;
	}

	private void SetTargetObject(string? targetId)
	{
		if (string.IsNullOrEmpty(targetId))
		{
			if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.Count > 0)
			{
				targetId = GameHost.UnitRegistry.Keys.First();
			}
			else if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.Count > 0)
			{
				targetId = GameHost.BuildingRegistry.Keys.First();
			}
		}

		_targetObjectId = targetId ?? string.Empty;
		_isTargetBuilding = GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(_targetObjectId);
	}

	private void UpdateTitles()
	{
		if (_isTargetBuilding)
		{
			TitleLabel.Text = TranslationServer.Translate("Building VFX & Attachment Studio");
			if (_lblUnitTitle != null) _lblUnitTitle.Text = TranslationServer.Translate("Building:");
		}
		else
		{
			TitleLabel.Text = TranslationServer.Translate("Socket & VFX Attachment Studio");
			if (_lblUnitTitle != null) _lblUnitTitle.Text = TranslationServer.Translate("Unit:");
		}

		if (_lblUnitValue != null)
		{
			_lblUnitValue.Text = !string.IsNullOrEmpty(_targetObjectId) ? _targetObjectId : "-";
		}
	}

	public static string NormalizeSocketId(string socket)
	{
		if (string.IsNullOrEmpty(socket)) return "RightHand";
		string s = socket.ToLowerInvariant().Replace("_", "").Replace(" ", "");

		return PseudoSocketCache.NormalizeSocketId(socket) ?? TryMatchExactSocket(s) ?? TryMatchContainsSocket(s) ?? "RightHand";
	}

	private static string? TryMatchExactSocket(string s)
	{
		if (s == "top" || s == "roof") return "Top";
		if (s == "base") return "Base";
		if (s == "pivot" || s == "origin") return "Pivot";
		return null;
	}

	private static readonly Dictionary<string, string[]> _socketMappings = new()
	{
		{ "Ground", new[] { "ground", "footprint" } },
		{ "Overhead", new[] { "overhead", "crown" } },
		{ "Chest", new[] { "chest" } },
		{ "Center", new[] { "center", "centerofmass", "middle" } },
		{ "LeftHand", new[] { "lefthand" } },
		{ "RightHand", new[] { "righthand" } },
		{ "Hips", new[] { "hips", "pelvis", "root" } },
		{ "Head", new[] { "head" } },
		{ "LeftFoot", new[] { "leftfoot" } },
		{ "RightFoot", new[] { "rightfoot" } }
	};

	private static string? TryMatchContainsSocket(string s)
	{
		foreach (var kvp in _socketMappings)
		{
			foreach (var match in kvp.Value)
			{
				if (s.Contains(match)) return kvp.Key;
			}
		}

		return null;
	}







	private bool IsAttachmentConfigured(string socketId, string attachmentId, string? parentAttachmentId = null)
	{
		if (string.IsNullOrEmpty(attachmentId)) return false;
		string normSocket = NormalizeSocketId(socketId);
		string cleanId = Path.GetFileNameWithoutExtension(attachmentId);
		var configured = GetConfiguredAttachments();
		for (int i = 0; i < configured.Count; i++)
		{
			var entry = configured[i];
			if (entry.SocketId.Equals(normSocket, StringComparison.OrdinalIgnoreCase))
			{
				if (string.Equals(entry.Orientation.ParentAttachmentId ?? string.Empty, parentAttachmentId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
				{
					if (entry.AttachmentId.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
					    entry.AttachmentId.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
					    Path.GetFileNameWithoutExtension(entry.AttachmentId).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}
		}

		return false;
	}

	private void PreviewCurrentAttachment()
	{
		ClearUncommittedPreview();
		ResetConfiguredVisualTransforms();

		if (string.IsNullOrEmpty(_currentAttachmentId))
		{
			_currentActiveModelNode = null;
			return;
		}

		LoadAttachmentOrientationIntoSliders(_targetObjectId, _currentSocketId, _currentAttachmentId, _currentParentAttachmentId);

		string normSocket = NormalizeSocketId(_currentSocketId);
		string key = GetAttachmentKey(normSocket, _currentAttachmentId, -1, _currentParentAttachmentId);

		if (IsAttachmentConfigured(normSocket, _currentAttachmentId, _currentParentAttachmentId))
		{
			if (_activeAttachmentVisuals.TryGetValue(key, out var visualNode) && GodotObject.IsInstanceValid(visualNode))
			{
				_currentActiveModelNode = visualNode;
				_socketModelNodes[normSocket] = visualNode;
			}
			else
			{
				var loaded = AttachVisualToAnchor(normSocket, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset, -1, _currentParentAttachmentId);
				_currentActiveModelNode = loaded;
				if (loaded != null)
				{
					_socketModelNodes[normSocket] = loaded;
				}
			}

			_uncommittedPreviewNode = null;
			_uncommittedPreviewKey = null;
		}
		else
		{
			var loaded = AttachVisualToAnchor(normSocket, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset, -1, _currentParentAttachmentId);
			_currentActiveModelNode = loaded;
			_uncommittedPreviewNode = loaded;
			_uncommittedPreviewKey = key;
			if (loaded != null)
			{
				_socketModelNodes[normSocket] = loaded;
			}
		}

		UpdateActiveAttachmentTransform();
	}

	private void AttachAllConfiguredAttachments()
	{
		ClearUncommittedPreview();
		foreach (var kvp in _activeAttachmentVisuals)
		{
			if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
			{
				kvp.Value.GetParent()?.RemoveChild(kvp.Value);
				kvp.Value.QueueFree();
			}
		}

		_activeAttachmentVisuals.Clear();
		var configured = GetConfiguredAttachments();
		foreach (var entry in configured)
		{
			if (string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				AttachVisualToAnchor(entry.SocketId, entry.AttachmentId, entry.Orientation.Position.ToGodotVector3(), entry.Orientation.RotationDegrees.ToGodotVector3(), entry.Orientation.ScaleVector.ToGodotVector3(), entry.Orientation.NormalOffset, entry.Index, null);
			}
		}

		foreach (var entry in configured)
		{
			if (!string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				AttachVisualToAnchor(entry.SocketId, entry.AttachmentId, entry.Orientation.Position.ToGodotVector3(), entry.Orientation.RotationDegrees.ToGodotVector3(), entry.Orientation.ScaleVector.ToGodotVector3(), entry.Orientation.NormalOffset, entry.Index, entry.Orientation.ParentAttachmentId);
			}
		}
	}

	private Node3D? AttachVisualToAnchor(string socketId, string? attachmentId, Vector3 pos, Vector3 rot, Vector3 scale, float normalOffset, int index = -1, string? parentAttachmentId = null)
	{
		string normSocket = NormalizeSocketId(socketId);
		if (!_socketAnchorNodes.TryGetValue(normSocket, out var targetAnchor) || targetAnchor == null || !GodotObject.IsInstanceValid(targetAnchor)) return null;

		if (string.IsNullOrEmpty(attachmentId) || attachmentId.Equals("null", StringComparison.OrdinalIgnoreCase) || attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;

		string key = GetAttachmentKey(normSocket, attachmentId, index, parentAttachmentId);
		RemoveOldAttachmentVisual(key);

		if (Unit3D.IsPlayerColorGlowAttachment(attachmentId))
		{
			MeshInstance3D previewGlow = CreatePreviewPlayerColorGlow(attachmentId, key, pos, rot, scale, normalOffset);
			GetAttachTarget(targetAnchor, parentAttachmentId).AddChild(previewGlow);
			_activeAttachmentVisuals[key] = previewGlow;
			return previewGlow;
		}

		Node3D? loaded = Unit3D.ResolveAndInstantiateAttachment(attachmentId, out _, out _, out _);
		if (loaded == null) return null;

		ConfigureLoadedVisual(loaded, attachmentId, key, pos, rot, scale, normalOffset);
		Node3D attachTarget = GetAttachTarget(targetAnchor, parentAttachmentId);

		attachTarget.AddChild(loaded);
		_activeAttachmentVisuals[key] = loaded;
		return loaded;
	}

	private static MeshInstance3D CreatePreviewPlayerColorGlow(string attachmentId, string key, Vector3 pos, Vector3 rot, Vector3 scale, float normalOffset)
	{
		var previewGlow = new MeshInstance3D
		{
			Name = $"AttVisual_{key}",
			Mesh = VisualSelectionHelper.GetOrCreatePlayerColorGlowMesh(1.0f),
			MaterialOverride = SharedMaterials.GetOrCreatePlayerColorGlowMaterial(Realm.Client.PlayerColorConfig.GetColor(Realm.Client.PlayerColorConfig.NEUTRAL_PLAYER_INDEX)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			Position = pos + (Vector3.Up * normalOffset),
			RotationDegrees = rot,
			Scale = scale == Vector3.Zero ? Vector3.One : scale
		};

		previewGlow.SetMeta("AttachmentId", attachmentId);
		previewGlow.SetMeta("CleanAttachmentId", attachmentId);
		return previewGlow;
	}

	private void RemoveOldAttachmentVisual(string key)
	{
		if (_activeAttachmentVisuals.TryGetValue(key, out var oldNode) && GodotObject.IsInstanceValid(oldNode))
		{
			oldNode.GetParent()?.RemoveChild(oldNode);
			oldNode.QueueFree();
			_activeAttachmentVisuals.Remove(key);
		}
	}

	private void ConfigureLoadedVisual(Node3D loaded, string attachmentId, string key, Vector3 pos, Vector3 rot, Vector3 scale, float normalOffset)
	{
		string cleanAttId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: Path.GetFileNameWithoutExtension(attachmentId);
		loaded.Name = $"AttVisual_{key}";
		loaded.SetMeta("AttachmentId", attachmentId);
		loaded.SetMeta("CleanAttachmentId", cleanAttId);
		loaded.Position = pos + (Vector3.Up * normalOffset);
		loaded.RotationDegrees = rot;
		loaded.Scale = scale == Vector3.Zero ? Vector3.One : scale;
	}

	private Node3D GetAttachTarget(Node3D targetAnchor, string? parentAttachmentId)
	{
		if (string.IsNullOrEmpty(parentAttachmentId)) return targetAnchor;

		var parentMesh = Unit3D.FindAttachmentInNode(targetAnchor, parentAttachmentId)
		                 ?? (_previewModel != null ? Unit3D.FindAttachmentInNode(_previewModel, parentAttachmentId) : null);

		return parentMesh ?? targetAnchor;
	}

	private void UpdateActiveAttachmentTransform()
	{
		if (_currentActiveModelNode != null && GodotObject.IsInstanceValid(_currentActiveModelNode))
		{
			_currentActiveModelNode.Position = _currentPosOffset + (Vector3.Up * _currentNormalOffset);
			_currentActiveModelNode.RotationDegrees = _currentRotOffset;
			_currentActiveModelNode.Scale = _currentScaleOffset;
		}
		else if (_socketModelNodes.TryGetValue(_currentSocketId, out var fallback) && GodotObject.IsInstanceValid(fallback))
		{
			fallback.Position = _currentPosOffset + (Vector3.Up * _currentNormalOffset);
			fallback.RotationDegrees = _currentRotOffset;
			fallback.Scale = _currentScaleOffset;
		}
	}

	private void RebuildConfiguredAttachmentsUI()
	{
		if (_configuredAttachmentsContainer == null) return;

		foreach (Node child in _configuredAttachmentsContainer.GetChildren())
		{
			child.QueueFree();
		}

		var configured = GetConfiguredAttachments();
		if (configured.Count == 0)
		{
			var emptyLabel = new Label
			{
				Text = TranslationServer.Translate("No attachments configured on this object."),
				AutowrapMode = TextServer.AutowrapMode.Word,
				CustomMinimumSize = new Vector2(530, 0),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			emptyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			emptyLabel.AddThemeFontSizeOverride("font_size", 11);
			_configuredAttachmentsContainer.AddChild(emptyLabel);
			return;
		}

		for (int i = 0; i < configured.Count; i++)
		{
			var entry = configured[i];
			var card = new PanelContainer();
			card.Name = "Card_PanelContainer";
			card.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

			var row = new HBoxContainer();
			row.Name = "Row_HBoxContainer";
			row.AddThemeConstantOverride("separation", 8);

			string? parentAttId = entry.Orientation.ParentAttachmentId;
			bool isChild = !string.IsNullOrEmpty(parentAttId);

			string badgeText = isChild
				? $"[{entry.SocketId} ➔ {Path.GetFileNameWithoutExtension(parentAttId)}]"
				: $"[{entry.SocketId}]";

			var badge = new Label
			{
				Text = badgeText,
				CustomMinimumSize = new Vector2(isChild ? 130 : 85, 0),
				ClipText = true
			};
			badge.AddThemeColorOverride("font_color", isChild ? new Color(0.9f, 0.6f, 1.0f) : UIStyle.ColorCyanGlow);
			badge.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(badge);

			string displayName = FormatAttachmentDisplayName(entry.AttachmentId);

			var nameLbl = new Label
			{
				Text = displayName,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true
			};
			nameLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			nameLbl.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(nameLbl);

			var summaryLbl = new Label
			{
				Text = $"Pos: ({entry.Orientation.PositionX:F2},{entry.Orientation.PositionY:F2},{entry.Orientation.PositionZ:F2})",
				CustomMinimumSize = new Vector2(130, 0),
				ClipText = true
			};
			summaryLbl.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.7f));
			summaryLbl.AddThemeFontSizeOverride("font_size", 10);
			row.AddChild(summaryLbl);

			string capturedSocket = entry.SocketId;
			string capturedAtt = entry.AttachmentId;
			string? capturedParent = entry.Orientation.ParentAttachmentId;
			int capturedIndex = entry.Index;

			var btnEdit = new Button();
			btnEdit.Name = "BtnEdit_Button";
			btnEdit.Set("icon_max_width", 0);
			btnEdit.Text = "✏️ " + TranslationServer.Translate("Edit");
			btnEdit.AddThemeFontSizeOverride("font_size", 11);
			btnEdit.CustomMinimumSize = new Vector2(55, 22);
			btnEdit.FocusMode = FocusModeEnum.None;
			btnEdit.TooltipText = TranslationServer.Translate("Select and tune transform in sliders");
			btnEdit.Pressed += () => SelectConfiguredAttachment(capturedSocket, capturedAtt, capturedIndex);
			row.AddChild(btnEdit);

			var btnRemove = UIStyle.CreateBareButton("BtnRemove_Button");
			btnRemove.Text = $"{UnicodeIcons.TRASH} " + TranslationServer.Translate("Remove");
			btnRemove.AddThemeFontSizeOverride("font_size", 11);
			btnRemove.AddThemeColorOverride("font_color", new Color(1.0f, 0.45f, 0.45f));
			btnRemove.CustomMinimumSize = new Vector2(65, 22);
			btnRemove.FocusMode = FocusModeEnum.None;
			btnRemove.TooltipText = TranslationServer.Translate("Detach and remove this attachment");
			btnRemove.Pressed += () => RemoveAttachment(capturedSocket, capturedAtt, capturedParent);
			row.AddChild(btnRemove);

			card.AddChild(row);
			_configuredAttachmentsContainer.AddChild(card);
		}
	}

	private void SelectConfiguredAttachment(string socketId, string attachmentId, int index = -1)
	{
		ClearUncommittedPreview();
		ResetConfiguredVisualTransforms();

		_isUpdatingUI = true;
		_currentSocketId = NormalizeSocketId(socketId);
		_currentAttachmentId = attachmentId;

		UpdateSocketDropdownSelection();
		UpdateAttachmentPickerSelection(attachmentId);

		_isUpdatingUI = false;

		ConfiguredAttachmentEntry? matched = FindMatchedConfiguredAttachment(index);

		if (matched.HasValue)
		{
			ApplyConfiguredOrientation(matched.Value.Orientation);
			UpdateSliderDisplayValues();
		}
		else
		{
			LoadAttachmentOrientationIntoSliders(_targetObjectId, _currentSocketId, _currentAttachmentId);
		}

		UpdateParentDropdown();
		UpdateActiveVisualNode(index);

		_uncommittedPreviewNode = null;
		_uncommittedPreviewKey = null;
		UpdateActiveAttachmentTransform();
	}

	private void UpdateSocketDropdownSelection()
	{
		for (int i = 0; i < _currentAvailableSockets.Count; i++)
		{
			if (_currentAvailableSockets[i].SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase))
			{
				_optSocket.Selected = i;
				break;
			}
		}
	}

	private void UpdateAttachmentPickerSelection(string attachmentId)
	{
		string cleanId = GetCleanAttachmentId(attachmentId);
		for (int i = 0; i < _availableAttachments.Count; i++)
		{
			string item = _availableAttachments[i];
			if (item.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
			    item.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
			    Path.GetFileNameWithoutExtension(item).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
			{
				_optAttachmentPicker.Selected = i;
				break;
			}
		}
	}

	private ConfiguredAttachmentEntry? FindMatchedConfiguredAttachment(int index)
	{
		if (index < 0) return null;
		var configured = GetConfiguredAttachments();
		for (int i = 0; i < configured.Count; i++)
		{
			if (configured[i].Index == index && configured[i].SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase))
			{
				return configured[i];
			}
		}

		return null;
	}

	private void UpdateActiveVisualNode(int index)
	{
		string key = GetAttachmentKey(_currentSocketId, _currentAttachmentId, index, _currentParentAttachmentId);
		if (_activeAttachmentVisuals.TryGetValue(key, out var visualNode) && GodotObject.IsInstanceValid(visualNode))
		{
			_currentActiveModelNode = visualNode;
			_socketModelNodes[_currentSocketId] = visualNode;
		}
		else
		{
			var loaded = AttachVisualToAnchor(_currentSocketId, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset, index, _currentParentAttachmentId);
			_currentActiveModelNode = loaded;
			if (loaded != null)
			{
				_socketModelNodes[_currentSocketId] = loaded;
			}
		}
	}

	private void RemoveAttachment(string socketId, string attachmentId, string? parentAttachmentId = null)
	{
		if (string.IsNullOrEmpty(_targetObjectId) || string.IsNullOrEmpty(attachmentId)) return;

		string normSocket = NormalizeSocketId(socketId);
		ClearUncommittedPreview();

		WorkingAttachments.RemoveSocketAttachment(normSocket, attachmentId, parentAttachmentId);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Removed {0} from {1} ({2})."), attachmentId, _targetObjectId, normSocket));

		AttachAllConfiguredAttachments();
		UpdateParentDropdown();

		_currentActiveModelNode = null;
		RebuildConfiguredAttachmentsUI();
	}

	private void AddOrUpdateCurrentAttachment()
	{
		if (string.IsNullOrEmpty(_targetObjectId) || string.IsNullOrEmpty(_currentAttachmentId))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Please select an attachment to add."));
			return;
		}

		var orientation = new HandAttachmentOrientation
		{
			PositionX = _currentPosOffset.X,
			PositionY = _currentPosOffset.Y,
			PositionZ = _currentPosOffset.Z,
			PitchX = _currentRotOffset.X,
			YawY = _currentRotOffset.Y,
			RollZ = _currentRotOffset.Z,
			Scale = _currentScaleOffset.X,
			ScaleX = _currentScaleOffset.X,
			ScaleY = _currentScaleOffset.Y,
			ScaleZ = _currentScaleOffset.Z,
			NormalOffset = _currentNormalOffset,
			ParentAttachmentId = _currentParentAttachmentId
		};

		WorkingAttachments.SetSocketOrientation(_currentSocketId, _currentAttachmentId, orientation);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Added {0} to {1} on socket {2}."), _currentAttachmentId, _targetObjectId, _currentSocketId));

		ClearUncommittedPreview();
		AttachAllConfiguredAttachments();
		UpdateParentDropdown();

		RebuildConfiguredAttachmentsUI();
	}

	protected override void OnApply()
	{
		if (!string.IsNullOrEmpty(_targetObjectId))
		{
			Hud?.SaveAllUnitObjectAttachments(_targetObjectId, WorkingAttachments);
			Hud?.SaveUnitPseudoSockets(_targetObjectId, _workingPseudoSockets);
		}

		ClearPreviewModel();
		_onApplied?.Invoke(default);
	}

	protected override void OnCancel()
	{
		_workingAttachments = null;
		ClearPreviewModel();
	}

	public override void CloseDialog()
	{
		_workingAttachments = null;
		ClearPreviewModel();
		base.CloseDialog();
	}

	public static List<string> GetAvailableObjectAttachmentIds()
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var result = new List<string>();

		if (seen.Add(Unit3D.PlayerColorGlowAttachmentId)) result.Add(Unit3D.PlayerColorGlowAttachmentId);

		AddVfxRegistryAttachments(seen, result);

		string wsPath = !string.IsNullOrEmpty(MapWorkspaceService.GetActiveWorkspacePath())
			? MapWorkspaceService.GetActiveWorkspacePath()
			: GameContext.EditorService._editingDirectory;

		AddUnionedAssets(wsPath, seen, result);
		AddMetadataAssets(wsPath, seen, result);
		AddGameHostAttachments(seen, result);

		return result;
	}

	private static void AddVfxRegistryAttachments(HashSet<string> seen, List<string> result)
	{
		if (GameContext.RegistryService.VfxRegistry == null) return;
		foreach (var kvp in GameContext.RegistryService.VfxRegistry)
		{
			string vfxKey = kvp.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? kvp.Key : $"vfx:{kvp.Key}";
			if (seen.Add(vfxKey)) result.Add(vfxKey);
		}
	}

	private static void AddUnionedAssets(string wsPath, HashSet<string> seen, List<string> result)
	{
		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj == null) return;

			var itemsDict = assetsObj.GetCategory("Item");
			if (itemsDict != null)
			{
				foreach (var modelProp in itemsDict)
				{
					string id = Path.GetFileNameWithoutExtension(modelProp.Key);
					if (seen.Add(id)) result.Add(id);
				}
			}

			var vfxDict = assetsObj.GetCategory("Spritesheet");
			if (vfxDict != null)
			{
				foreach (var prop in vfxDict)
				{
					string vfxKey = prop.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? prop.Key : $"vfx:{prop.Key}";
					if (seen.Add(vfxKey)) result.Add(vfxKey);
				}
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[ObjectAttachmentDialog] Error loading unioned assets: {ex.Message}");
		}
	}

	private static void AddMetadataAssets(string wsPath, HashSet<string> seen, List<string> result)
	{
		try
		{
			var metadata = GameContext.MetadataService.LoadMetadata(wsPath);
			if (metadata.Templates == null) return;

			ProcessTemplates(metadata.Templates, seen, result);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[ObjectAttachmentDialog] Error scanning metadata: {ex.Message}");
		}
	}

	private static void ProcessTemplates(TemplateContainer templates, HashSet<string> seen, List<string> result)
	{
		if (templates.Items != null)
			foreach (var it in templates.Items)
				TryAddTemplateId(it.TemplateID, seen, result);

		if (templates.Weapons != null)
			foreach (var wpn in templates.Weapons)
				TryAddTemplateId(wpn.TemplateID, seen, result);

		if (templates.Attachments != null)
			foreach (var att in templates.Attachments)
				TryAddTemplateId(att.AttachmentId, seen, result);

		if (templates.Vfx != null)
			foreach (var vfx in templates.Vfx)
				TryAddVfx(vfx.VfxId, seen, result);
	}

	private static void TryAddVfx(string? vfxId, HashSet<string> seen, List<string> result)
	{
		if (string.IsNullOrEmpty(vfxId)) return;
		string vfxKey = $"vfx:{vfxId}";
		if (seen.Add(vfxKey)) result.Add(vfxKey);
	}

	private static void TryAddTemplateId(string? templateId, HashSet<string> seen, List<string> result)
	{
		if (string.IsNullOrEmpty(templateId)) return;
		string cleanId = Path.GetFileNameWithoutExtension(templateId);
		if (seen.Add(cleanId)) result.Add(cleanId);
	}

	private static void AddGameHostAttachments(HashSet<string> seen, List<string> result)
	{
		if (GameHost.AttachmentRegistry == null) return;
		foreach (var kvp in GameHost.AttachmentRegistry)
		{
			string id = Path.GetFileNameWithoutExtension(kvp.Key);
			if (seen.Add(id)) result.Add(id);
		}
	}
}