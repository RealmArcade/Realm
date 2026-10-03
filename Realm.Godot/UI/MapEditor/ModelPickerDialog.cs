using Godot;
using System;
using System.Collections.Generic;
using Realm.Godot.Utils;

public partial class ModelPickerDialog : FloatingPreview3DDialogBase
{
	private Node3D _previewModelRoot;

	private LineEdit _txtModelPath;
	private Action<string> _setModelPathValue;
	private CheckBox _chkShowAllFolders;
	private Label _lblStatus;

	private string _entityId = "";
	private string _fieldName = "ModelPath";
	private string _domain = "units";
	private string _selectedModelPath = "";
	private string _initialModelPath = "";
	private bool _showAllFolders = false;
	private Action<string> _onApplied;

	private Vector3 _modelCenter = Vector3.Zero;

	public ModelPickerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Model Asset Picker"), new Vector2(480, 560))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(460, 240));

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

		AddCameraPresetToolbar(topControlsVBox, includeBack: true);

		AddLabel(topControlsVBox, TranslationServer.Translate("LMB: Orbit • RMB/MMB: Pan • Scroll: Zoom"), 10, UIStyle.ColorGoldDull);

		AddSectionHeader(BodyContainer, "📦 " + TranslationServer.Translate("MODEL ASSET SELECTION"), new Color(0.35f, 0.75f, 0.9f));

		_chkShowAllFolders = AddCheckBox(BodyContainer, TranslationServer.Translate("Show all model assets (all folders)"), _showAllFolders, (val) =>
		{
			_showAllFolders = val;
		}, "Include 3D models from all asset subdirectories");

		(_txtModelPath, _setModelPathValue) = AddAssetFilterDropdown(
			BodyContainer,
			TranslationServer.Translate("Model Asset (.rmesh):"),
			_selectedModelPath,
			(all) => ScanAvailableAssets("models", all || _showAllFolders, _domain),
			(val) =>
			{
				_selectedModelPath = val ?? string.Empty;
				LoadAndPreviewModel(_selectedModelPath);
			},
			TranslationServer.Translate("Select or search model asset..."),
			140f
		);

		_lblStatus = new Label();
		_lblStatus.AddThemeFontSizeOverride("font_size", 11);
		_lblStatus.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		_lblStatus.AutowrapMode = TextServer.AutowrapMode.Word;
		BodyContainer.AddChild(_lblStatus);
	}

	public void OpenForEntity(string entityId, string fieldName, string domain, string currentPath, Action<string> onApplied = null)
	{
		_entityId = entityId ?? string.Empty;
		_fieldName = string.IsNullOrEmpty(fieldName) ? "ModelPath" : fieldName;
		_domain = string.IsNullOrEmpty(domain) ? "units" : domain;
		_selectedModelPath = currentPath ?? string.Empty;
		_initialModelPath = _selectedModelPath;
		_onApplied = onApplied;

		string fieldDisplay = _fieldName == "PortraitModelPath" ? "Portrait Model" : "Model Asset";
		TitleLabel.Text = $"{TranslationServer.Translate("Model Asset Picker")} - {_entityId} ({fieldDisplay})";

		_setModelPathValue?.Invoke(_selectedModelPath);
		ClearPreviewModel();

		OpenDialog();
		ResetCameraDefault();

		if (!string.IsNullOrWhiteSpace(_selectedModelPath))
		{
			LoadAndPreviewModel(_selectedModelPath);
		}
		else
		{
			var available = ScanAvailableAssets("models", false, _domain);
			if (available.Count > 0)
			{
				_selectedModelPath = available[0];
				_setModelPathValue?.Invoke(_selectedModelPath);
				LoadAndPreviewModel(_selectedModelPath);
			}
			else
			{
				if (_lblStatus != null) _lblStatus.Text = TranslationServer.Translate("No model currently selected.");
			}
		}
	}

	private void ClearPreviewModel()
	{
		if (_previewModelRoot != null && GodotObject.IsInstanceValid(_previewModelRoot))
		{
			_previewModelRoot.QueueFree();
			_previewModelRoot = null;
		}
	}

	private void LoadAndPreviewModel(string modelPath)
	{
		ClearPreviewModel();

		if (string.IsNullOrWhiteSpace(modelPath) || PreviewSubViewport == null)
		{
			if (_lblStatus != null) _lblStatus.Text = TranslationServer.Translate("No model path specified.");
			return;
		}

		string resolvedPath = ModelCache.ResolveModelPath(modelPath);
		if (modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || modelPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || modelPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || (resolvedPath != null && (resolvedPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || resolvedPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || resolvedPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))))
		{
			var root = new Node3D();
			root.Name = "PreviewDecalRoot";

			var floorMesh = new MeshInstance3D();
			floorMesh.Name = "PreviewFloor";
			var plane = new PlaneMesh { Size = new Vector2(10f, 10f) };
			floorMesh.Mesh = plane;
			root.AddChild(floorMesh);

			var decalNode = new Decal3D();
			decalNode.Name = "PreviewDecal";
			decalNode.DecalId = !string.IsNullOrEmpty(resolvedPath) ? resolvedPath : modelPath;
			decalNode.CullMask = 1u;
			decalNode.Size = new Vector3(6.0f, 20.0f, 6.0f);
			root.AddChild(decalNode);

			GameHost.Instance?.ApplyDecalPropertiesFromMetadata(decalNode, !string.IsNullOrEmpty(resolvedPath) ? resolvedPath : modelPath);

			PreviewSubViewport.AddChild(root);
			_previewModelRoot = root;
			FrameCameraOnNode(root);

			if (_lblStatus != null)
			{
				_lblStatus.Text = $"{TranslationServer.Translate("Loaded Decal:")} {modelPath}";
				_lblStatus.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			}
			return;
		}

		Node loaded = ModelCache.GetModel(modelPath);
		if (loaded is Node3D node3D)
		{
			var cloned = (Node3D)node3D.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
			cloned.Position = Vector3.Zero;
			cloned.Rotation = Vector3.Zero;
			cloned.Scale = Vector3.One;

			PreviewSubViewport.AddChild(cloned);
			_previewModelRoot = cloned;
			Realm.Godot.Animation.AnimationRetargetingService.TryApplyRiggedIdlePose(cloned, modelPath);
			if (_previewModelRoot.IsInsideTree())
			{
				_previewModelRoot.PropagateNotification((int)Node3D.NotificationTransformChanged);
			}

			FrameCameraOnNode(_previewModelRoot);

			string details = "";
			if (!string.IsNullOrEmpty(resolvedPath) && System.IO.File.Exists(resolvedPath))
			{
				bool teamCol = Realm.Shared.Metadata.RealmMetadataHelper.ExtractSupportsTeamColor(resolvedPath) == true;
				string? author = Realm.Shared.Metadata.RealmMetadataHelper.ExtractAuthor(resolvedPath);
				if (teamCol) details += " • TeamColor: Yes";
				if (!string.IsNullOrEmpty(author)) details += $" • Author: {author}";
			}
			if (_lblStatus != null)
			{
				_lblStatus.Text = $"{TranslationServer.Translate("Loaded:")} {modelPath}{details}";
				_lblStatus.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			}
		}
		else
		{
			if (_lblStatus != null)
			{
				_lblStatus.Text = $"{TranslationServer.Translate("Failed to load model:")} {modelPath}";
				_lblStatus.AddThemeColorOverride("font_color", new Color(1.0f, 0.4f, 0.4f));
			}
		}
	}

	protected override void OnApply()
	{
		if (!string.IsNullOrEmpty(_entityId))
		{
			Hud?.SaveEntityModelPathToMetadata(_entityId, _fieldName, _domain, _selectedModelPath);
			_onApplied?.Invoke(_selectedModelPath);
			Hud?.ShowFeedback(TranslationServer.Translate("Model asset updated successfully."));
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
