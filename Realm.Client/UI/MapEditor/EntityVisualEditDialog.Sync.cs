// Controls <- model. One entry point, then a refresher per section that mirrors the Build* methods, so a
// field added to a section needs its line here too.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;


namespace Realm.Client.UI.MapEditor;

public partial class EntityVisualEditDialog
{
	private void SyncControls()
	{
		_isSyncing = true;
		try
		{
			SyncIdentityControls();
			SyncTransformControls();
			SyncAppearanceControls();
			SyncShaderControls();

			ValidateSlug();
			UpdateStudioButtonVisibility();
		}
		finally
		{
			_isSyncing = false;
		}
	}

	private void SyncIdentityControls()
	{
		if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
		if (_txtSlug != null) _txtSlug.Text = _slug;
		SyncVisualModeControl();
		_setModelPathValue?.Invoke(_modelPath);
		_setPortraitModelPathValue?.Invoke(_portraitModelPath);
		if (_txtName != null) _txtName.Text = _name;
		if (_txtDescription != null) _txtDescription.Text = _description;
	}

	private void SyncTransformControls()
	{
		if (_sldScale != null)
		{
			if (_scale < _sldScale.MinValue) _sldScale.MinValue = _scale;
			if (_scale > _sldScale.MaxValue) _sldScale.MaxValue = _scale;
			_sldScale.Value = _scale;
			_lblScale.Text = $"{_scale:F2}x";
		}

		if (_sldYOffset != null)
		{
			if (_yOffset < _sldYOffset.MinValue) _sldYOffset.MinValue = _yOffset - 5.0f;
			if (_yOffset > _sldYOffset.MaxValue) _sldYOffset.MaxValue = _yOffset + 5.0f;
			_sldYOffset.Value = _yOffset;
			_lblYOffset.Text = $"{_yOffset:F2}";
		}

		if (_sldCollisionCircle != null)
		{
			if (_collisionCircle < _sldCollisionCircle.MinValue) _sldCollisionCircle.MinValue = _collisionCircle;
			if (_collisionCircle > _sldCollisionCircle.MaxValue) _sldCollisionCircle.MaxValue = _collisionCircle;
			_sldCollisionCircle.Value = _collisionCircle;
			_lblCollisionCircle.Text = $"{_collisionCircle:F2}";
		}
	}

	private void SyncAppearanceControls()
	{
		if (_sldBrightness != null)
		{
			_sldBrightness.Value = _brightness;
			_lblBrightness.Text = $"{_brightness:F2}x";
		}

		if (_btnTint != null) _btnTint.Color = _tint;
		if (_sldTintHue != null)
		{
			if (Mathf.Abs(_tint.R - 1.0f) < 0.001f && Mathf.Abs(_tint.G - 1.0f) < 0.001f && Mathf.Abs(_tint.B - 1.0f) < 0.001f)
			{
				_sldTintHue.Value = 0.0f;
			}
			else
			{
				_sldTintHue.Value = _tint.H;
			}
		}

		if (_chkNormalizeLuminance != null) _chkNormalizeLuminance.ButtonPressed = _normalizeLuminance;
		if (_chkIgnorePlayerColor != null) _chkIgnorePlayerColor.ButtonPressed = _ignorePlayerColor;
		if (_chkDespillPlayerColor != null) _chkDespillPlayerColor.ButtonPressed = _despillPlayerColor;
	}

	private void SyncShaderControls()
	{
		if (_chkEnableProceduralAnim != null) _chkEnableProceduralAnim.ButtonPressed = _enableProceduralAnimation;

		var allShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
		PopulateShaderDropdowns(allShaders);
		SelectActiveShaders(allShaders);

		RefreshProceduralAnimationDropdown(_proceduralAnimation);
	}

	private void PopulateShaderDropdowns(Dictionary<string, CustomShaderConfig> allShaders)
	{
		if (_optSpawnShader != null)
		{
			_optSpawnShader.Clear();
			_optSpawnShader.AddItem(TranslationServer.Translate("(None)"));
			foreach (var s in allShaders.Values)
			{
				_optSpawnShader.AddItem(s.Name);
			}
		}

		if (_optDeathShader != null)
		{
			_optDeathShader.Clear();
			_optDeathShader.AddItem(TranslationServer.Translate("(None)"));
			foreach (var s in allShaders.Values)
			{
				_optDeathShader.AddItem(s.Name);
			}
		}
	}

	private void SelectActiveShaders(Dictionary<string, CustomShaderConfig> allShaders)
	{
		int spawnIdx = 0;
		int deathIdx = 0;
		int sIdx = 1;

		foreach (var s in allShaders)
		{
			if (string.Equals(s.Key, _spawnShader, StringComparison.OrdinalIgnoreCase)) spawnIdx = sIdx;
			if (string.Equals(s.Key, _deathShader, StringComparison.OrdinalIgnoreCase)) deathIdx = sIdx;
			sIdx++;
		}

		if (_optSpawnShader != null) _optSpawnShader.Selected = spawnIdx;
		if (_optDeathShader != null) _optDeathShader.Selected = deathIdx;
	}

	private void RefreshProceduralAnimationDropdown(string selectedKey)
	{
		if (_optProceduralAnim == null) return;
		_optProceduralAnim.Clear();
		_optProceduralAnim.AddItem(TranslationServer.Translate("(None)"));

		var animConfigs = ProceduralAnimationManager.LoadAllConfigs();
		int selectedIdx = 0;
		int idx = 1;
		foreach (var a in animConfigs)
		{
			_optProceduralAnim.AddItem(a.Value.Name);
			if (string.Equals(a.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
			{
				selectedIdx = idx;
			}

			idx++;
		}

		_optProceduralAnim.Selected = selectedIdx;
	}
}
