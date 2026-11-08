using System;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.VFX;

namespace Realm.Client.UI
{
	/// <summary>
	/// The inspector's refresh pass: reading the selection and writing it into the inspector's nodes and the
	/// view model. It stays with the HUD rather than moving into an inspector controller because one update writes
	/// four things at once - the view model, the inspector accordion's header and content, the tool highlight, and
	/// the rig/coverage rows - and those belong to four different owners, so moving the write would spread one
	/// decision across all of them.
	/// </summary>
	public partial class MapEditorHUD : Control
	{
		private void HideInspectorInfo()
		{
			if (_inspectorView.PlayerOwnerContainer != null) _inspectorView.PlayerOwnerContainer.Visible = false;
			if (_inspectorView.EditTemplateButton != null) _inspectorView.EditTemplateButton.Visible = false;
			if (_inspectorView.EditVfxButton != null) _inspectorView.EditVfxButton.Visible = false;
			if (_inspectorView.RigStatusContainer != null) _inspectorView.RigStatusContainer.Visible = false;
			if (_inspectorView.CoverageButton != null) _inspectorView.CoverageButton.Visible = false;
			if (_lblInfoText != null) _lblInfoText.Visible = true;
			if (_inspectorView.Root != null) _inspectorView.Root.Visible = false;
			if (_accordionInspector != null) _accordionInspector.Visible = false;
			if (_viewModel != null)
			{
				_viewModel.HasInspectorSelection = false;
				_viewModel.InspectorTitle = "No Selection";
				_viewModel.InspectorPos = "Position: (0, 0)";
			}

			if (GameContext.Host != null)
			{
				TriggerToolSelection(GameContext.Host.ActiveEditorTool, _activeToolButton, GameContext.Host.ActivePlaceId);
			}
		}

		private void ShowMultiSelectionInfo(int selectedCount)
		{
			SetInspectorVisibility();

			if (_inspectorView.PlayerOwnerContainer != null) _inspectorView.PlayerOwnerContainer.Visible = false;
			if (_inspectorView.CoverageButton != null) _inspectorView.CoverageButton.Visible = false;
			if (_inspectorView.RigStatusContainer != null) _inspectorView.RigStatusContainer.Visible = false;
			if (_inspectorView.EditTemplateButton != null) _inspectorView.EditTemplateButton.Visible = false;
			if (_inspectorView.EditVfxButton != null) _inspectorView.EditVfxButton.Visible = false;

			if (_viewModel == null) return;

			_viewModel.HasInspectorSelection = true;
			_viewModel.InspectorTitle = string.Format(LocalizationManager.Translate("{0} Objects Selected"), selectedCount);
			_viewModel.InspectorPos = LocalizationManager.Translate("Drag any selected object to move the whole group. Shift + MouseWheel rotates, Alt + MouseWheel scales, Delete removes all of them.");
		}

		private void SetInspectorVisibility()
		{
			if (_lblInfoText != null) _lblInfoText.Visible = false;
			if (_inspectorView.Root != null) _inspectorView.Root.Visible = true;
			if (_accordionInspector != null)
			{
				_accordionInspector.Visible = true;
				if (_btnHeaderInspector != null) _btnHeaderInspector.Text = "▼ Selected Object Inspector";
				if (_contentInspector != null) _contentInspector.Visible = true;
			}
		}

		private void GetObjectDetails(Node selected, out string typeStr, out string? idStr, out string nameStr)
		{
			typeStr = "";
			nameStr = selected.Name;
			idStr = null;

			if (selected is Unit3D unit)
			{
				typeStr = unit.IsBuilding ? "BUILDING" : "UNIT";
				idStr = Path.GetFileName(unit.UnitId).ToUpper();
				if (unit.IsResource && GameHost.ResourceRegistry.TryGetValue(unit.UnitId, out var resMeta) && !string.IsNullOrEmpty(resMeta.Name))
					nameStr = resMeta.Name.ToUpper();
				else if (unit.IsBuilding && GameHost.BuildingRegistry.TryGetValue(unit.UnitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.Name))
					nameStr = bldMeta.Name.ToUpper();
				else if (GameHost.UnitRegistry.TryGetValue(unit.UnitId, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.Name))
					nameStr = unitMeta.Name.ToUpper();
				else
					nameStr = idStr;
			}
			else if (selected is Prop3D prop)
			{
				typeStr = "PROP";
				idStr = Path.GetFileName(prop.PropId).ToUpper();
				if (GameHost.ResourceRegistry.TryGetValue(prop.PropId, out var propResMeta) && !string.IsNullOrEmpty(propResMeta.Name))
					nameStr = propResMeta.Name.ToUpper();
				else if (GameHost.PropRegistry.TryGetValue(prop.PropId, out var propMeta) && !string.IsNullOrEmpty(propMeta.Name))
					nameStr = propMeta.Name.ToUpper();
				else
					nameStr = idStr;
			}
			else if (selected is Decal decal)
			{
				typeStr = "DECAL";
				nameStr = Path.GetFileName(decal.Name).ToUpper();
			}
			else if (selected is ProceduralVfxInstance3D vfx)
			{
				typeStr = "VFX";
				nameStr = (!string.IsNullOrEmpty(vfx.Config?.Name) ? vfx.Config.Name : vfx.Config?.PrimitiveType.ToString() ?? "VFX").ToUpper();
				idStr = vfx.Config?.VfxId?.ToUpper() ?? "";
			}
		}

		public void UpdateSelectedObjectInfo()
		{
			if (GameContext.Host == null) return;
			var selected = GameContext.Host.SelectedEditorObject;
			int selectedCount = GameContext.Host.SelectedEditorObjectCount;

			if (selectedCount > 1)
			{
				ShowMultiSelectionInfo(selectedCount);
				return;
			}

			if (!GodotObject.IsInstanceValid(selected))
			{
				HideInspectorInfo();
				return;
			}

			SetInspectorVisibility();

			UpdateInspectorViewModel(selected);

			UpdateInspectorOwnerAndCoverage(selected);

			UpdateRigStatusIndicator(selected);

			UpdateInspectorActionVisibility(selected);
		}


		private void UpdateInspectorViewModel(Node selected)
		{
			Vector3 pos = selected is Node3D node3D ? node3D.Position : Vector3.Zero;
			Vector3 rot = selected is Node3D node3DRot ? node3DRot.RotationDegrees : Vector3.Zero;
			Vector3 scale = selected is Node3D node3DScale ? node3DScale.Scale : Vector3.One;

			GetObjectDetails(selected, out string typeStr, out string? idStr, out string nameStr);

			if (_viewModel != null)
			{
				_viewModel.HasInspectorSelection = true;
				_viewModel.InspectorTitle = idStr != null
					? $"SELECTED: {nameStr}\n({idStr})\n[{typeStr}]"
					: $"SELECTED: {nameStr}\n[{typeStr}]";
				_viewModel.InspectorPos = $"Pos: {pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}\nRot: {rot.Y:F1}° | Scale: {scale.X:F2}x";
			}
		}


		private void UpdateInspectorOwnerAndCoverage(Node selected)
		{
			if (selected is Unit3D unitOwner && _inspectorView.PlayerOwnerContainer != null && _inspectorView.PlayerOwnerOption != null)
			{
				_inspectorView.PlayerOwnerContainer.Visible = true;
				IsUpdatingInspectorUI = true;
				_inspectorView.PlayerOwnerOption.Selected = Mathf.Clamp(unitOwner.Player, 0, PlayerColorConfig.Palette.Length - 1);
				IsUpdatingInspectorUI = false;
			}
			else if (_inspectorView.PlayerOwnerContainer != null) _inspectorView.PlayerOwnerContainer.Visible = false;

			if (_inspectorView.CoverageButton != null)
			{
				bool isUnit = selected is Unit3D;
				_inspectorView.CoverageButton.Visible = isUnit;
				if (isUnit && GameContext.Host != null)
				{
					_inspectorView.CoverageButton.SetPressedNoSignal(GameContext.Host.EditorCoverageOverlayEnabled);
					_inspectorView.CoverageButton.Text = GameContext.Host.EditorCoverageOverlayEnabled ? TranslationServer.Translate("◉ VISION/ATTACK RANGES: ON") : TranslationServer.Translate("◉ VISION/ATTACK RANGES: OFF");
				}
			}
		}


		private void UpdateRigStatusIndicator(Node selected)
		{
			bool isUnitCharacter = (selected is Unit3D unitObj && !unitObj.IsBuilding);
			if (isUnitCharacter)
			{
				Node modelRoot = selected;
				if (selected is Unit3D uObj && uObj.ModelNode != null) modelRoot = uObj.ModelNode;

				var validation = SkeletonValidator.Validate(modelRoot);
				if (_inspectorView.RigStatusContainer != null && _inspectorView.RigStatusLabel != null)
				{
					_inspectorView.RigStatusContainer.Visible = true;
					if (validation.IsValid)
					{
						_inspectorView.RigStatusLabel.Text = TranslationServer.Translate("Rig: ✔ Compatible Humanoid");
						_inspectorView.RigStatusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
					}
					else if (validation.Skeleton == null)
					{
						_inspectorView.RigStatusLabel.Text = TranslationServer.Translate("Rig: ✖ Unrigged Mesh");
						_inspectorView.RigStatusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
					}
					else
					{
						_inspectorView.RigStatusLabel.Text = string.Format(TranslationServer.Translate("Rig: ✖ Incompatible ({0})"), string.Join(", ", validation.MissingRequiredBones));
						_inspectorView.RigStatusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
					}
				}
			}
			else
			{
				if (_inspectorView.RigStatusContainer != null) _inspectorView.RigStatusContainer.Visible = false;
			}
		}


		private void UpdateInspectorActionVisibility(Node selected)
		{
			string assetKey = GameContext.Host?.GetSelectedEntityOrAssetKey(selected) ?? "";
			bool isDecal = selected is Decal || (GameContext.Host != null && GameContext.Host.FindDecalInParentChain(selected) != null);
			if (!string.IsNullOrEmpty(assetKey) && !isDecal && _inspectorView.EditTemplateButton != null)
			{
				_inspectorView.EditTemplateButton.Visible = true;
				_inspectorView.EditTemplateButton.TooltipText = string.Format(TranslationServer.Translate("Edit global model scale, offsets, and shaders for {0}"), assetKey);
			}
			else if (_inspectorView.EditTemplateButton != null) _inspectorView.EditTemplateButton.Visible = false;

			if (selected is ProceduralVfxInstance3D && _inspectorView.EditVfxButton != null)
			{
				_inspectorView.EditVfxButton.Visible = true;
				_inspectorView.EditVfxButton.TooltipText = TranslationServer.Translate("Open Procedural VFX Studio to edit this effect");
			}
			else if (_inspectorView.EditVfxButton != null) _inspectorView.EditVfxButton.Visible = false;
		}
	}
}
