using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;

namespace Realm.Client.UI.MapEditor;

public partial class TemplateManagerDialog
{
	private void LoadPreviewForObject(ObjectItemInfo item)
	{
		_currentPreviewTemplateID = item.TemplateID;
		_currentShaderConfig = null;
		ClearPreviewProjectile();
		ClearPreviewNodes();

		string cat = item.Category?.ToLowerInvariant() ?? "";
		switch (cat)
		{
			case "terrain": LoadTerrainPreview(item); break;
			case "decals": LoadDecalPreview(item); break;
			case "upgrades" or "items": LoadItemUpgradePreview(item); break;
			case "spritesheets": LoadSpritesheetPreview(item); break;
			case "shaders" or "shader": LoadShaderPreview(item); break;
			case "weapons": LoadWeaponPreview(item); break;
			case "vfx": LoadVfxPreview(item); break;
			default:
				if (!string.IsNullOrEmpty(item.ModelPath)) LoadModelPreview(item);
				else if (!string.IsNullOrEmpty(item.IconPath)) LoadIconPreview(item);
				else ShowDefaultPreview();
				break;
		}
	}

	private void ClearPreviewNodes()
	{
		if (_currentModelRoot != null && GodotObject.IsInstanceValid(_currentModelRoot))
		{
			foreach (Node child in _currentModelRoot.GetChildren())
			{
				_currentModelRoot.RemoveChild(child);
				child.QueueFree();
			}
		}

		if (_vfxSprite != null && GodotObject.IsInstanceValid(_vfxSprite))
		{
			_vfxSprite.Visible = false;
			_vfxSprite.Stop();
		}

		if (_tooltipPreviewContainer != null)
		{
			foreach (Node child in _tooltipPreviewContainer.GetChildren()) child.QueueFree();
			_tooltipPreviewContainer.Visible = false;
		}
	}

	private void ShowDefaultPreview()
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;
	}

	private void LoadTerrainPreview(ObjectItemInfo item)
	{
		PreviewSubViewport.GetParent<Control>().Visible = false;
		_previewAudioContainer.Visible = false;
		_preview2DContainer.Visible = true;

		_preview2DImage.CustomMinimumSize = new Vector2(140, 140);
		string texPath = !string.IsNullOrEmpty(item.ModelPath) ? item.ModelPath : item.TemplateID;
		_preview2DImage.Texture = LoadTexture2D(texPath, "textures");
		_lblPreview2DInfo.Text = item.Name;
	}

	private void LoadDecalPreview(ObjectItemInfo item)
	{
		PreviewSubViewport.GetParent<Control>().Visible = false;
		_previewAudioContainer.Visible = false;
		_preview2DContainer.Visible = true;

		_preview2DImage.CustomMinimumSize = new Vector2(140, 140);
		Texture2D? decalTex = GameContext.Host?.LoadDecalTexture(item.TemplateID) ?? LoadTexture2D(item.ModelPath, "decals");
		_preview2DImage.Texture = decalTex;
		_lblPreview2DInfo.Text = item.Name;
	}

	private void LoadItemUpgradePreview(ObjectItemInfo item)
	{
		PreviewSubViewport.GetParent<Control>().Visible = false;
		_previewAudioContainer.Visible = false;
		_preview2DContainer.Visible = true;

		string tooltipText = !string.IsNullOrWhiteSpace(item.Description)
			? item.Description
			: $"<b>{item.Name}</b>\n<color=#888888>{item.TemplateID}</color>";

		Control tooltipWidget = RichTooltip.Create(tooltipText);
		_tooltipPreviewContainer.AddChild(tooltipWidget);
		_tooltipPreviewContainer.Visible = true;

		_preview2DImage.CustomMinimumSize = new Vector2(56, 56);
		string iconPath = !string.IsNullOrEmpty(item.IconPath) ? item.IconPath : item.ModelPath;
		_preview2DImage.Texture = LoadTexture2D(iconPath, "icons");
		_lblPreview2DInfo.Text = item.Name;
	}

	private bool TryGetSpritesheetMeta(string wsPath, string templateID, out VfxMetadata? sheetMeta)
	{
		sheetMeta = null;
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) && meta?.VfxSpritesheets != null)
		{
			if (meta.VfxSpritesheets.TryGetValue(templateID, out var sm) && sm != null)
			{
				sheetMeta = sm;
				return true;
			}
		}

		return false;
	}

	private void ResolveSpritesheetMeta(string wsPath, ObjectItemInfo item, out int columns, out int rows, out float fps, out string spritePath)
	{
		columns = 4;
		rows = 4;
		fps = 20.0f;
		spritePath = item.TemplateID;

		if (!string.IsNullOrEmpty(item.ModelPath))
		{
			spritePath = item.ModelPath;
		}

		if (!TryGetSpritesheetMeta(wsPath, item.TemplateID, out var sheetMeta)) return;
		if (sheetMeta == null) return;

		if (sheetMeta.Columns > 0) columns = sheetMeta.Columns;
		if (sheetMeta.Rows > 0) rows = sheetMeta.Rows;
		if (sheetMeta.Fps > 0) fps = sheetMeta.Fps;

		if (!string.IsNullOrEmpty(sheetMeta.TexturePath))
		{
			spritePath = sheetMeta.TexturePath;
		}
	}

	private void LoadSpritesheetPreview(ObjectItemInfo item)
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;

		string wsPath = GameContext.EditorService._editingDirectory;

		ResolveSpritesheetMeta(wsPath, item, out int columns, out int rows, out float fps, out string spritePath);

		Texture2D? sheetTex = LoadTexture2D(spritePath, "vfx");

		if (sheetTex == null) return;
		if (_vfxSprite == null) return;
		if (!GodotObject.IsInstanceValid(_vfxSprite)) return;

		int frameWidth = sheetTex.GetWidth() / columns;
		int frameHeight = sheetTex.GetHeight() / rows;

		if (frameWidth <= 0) return;
		if (frameHeight <= 0) return;

		ApplySpritesheetFrames(sheetTex, columns, rows, fps, frameWidth, frameHeight);
	}

	private void ApplySpritesheetFrames(Texture2D sheetTex, int columns, int rows, float fps, int frameWidth, int frameHeight)
	{
		int totalFrames = columns * rows;
		var frames = new SpriteFrames();
		frames.AddAnimation("play");
		frames.SetAnimationLoopMode("play", SpriteFrames.LoopMode.Linear);
		frames.SetAnimationSpeed("play", fps);

		for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
		{
			int c = frameIndex % columns;
			int r = frameIndex / columns;
			var atlasFrame = new AtlasTexture();
			atlasFrame.Atlas = sheetTex;
			atlasFrame.Region = new Rect2(c * frameWidth, r * frameHeight, frameWidth, frameHeight);
			frames.AddFrame("play", atlasFrame);
		}

		_vfxSprite.SpriteFrames = frames;
		_vfxSprite.Animation = "play";
		_vfxSprite.Position = Vector3.Zero;
		_vfxSprite.PixelSize = 2.0f / Math.Max(frameWidth, frameHeight);
		_vfxSprite.Visible = true;
		_vfxSprite.Play("play");
	}

	private void LoadShaderPreview(ObjectItemInfo item)
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;

		string meshKey = !string.IsNullOrEmpty(_selectedPreviewMesh) ? _selectedPreviewMesh : (_availablePreviewMeshes.Count > 0 ? _availablePreviewMeshes[0] : "");
		Node3D? loadedNode3D = GetShaderPreviewMesh(meshKey);

		if (loadedNode3D != null)
		{
			_currentModelRoot.AddChild(loadedNode3D);
			AnimationRetargetingService.TryApplyRiggedIdlePose(loadedNode3D, meshKey);

			_currentShaderConfig = SpawnDeathShaderManager.GetShaderConfig(item.TemplateID);
			_shaderAnimTime = 0.0f;
			_shaderAnimForward = true;
			if (_currentShaderConfig != null)
			{
				SpawnDeathShaderManager.ApplyShaderPreview(loadedNode3D, _currentShaderConfig, 0.0f);
			}

			CenterAndFrameNode(loadedNode3D);
		}
	}

	private Node3D GetFallbackMesh(string meshKey)
	{
		var meshInst = new MeshInstance3D();
		meshInst.Name = "MeshInst_MeshInstance3D";
		if (meshKey.Contains("Capsule"))
		{
			meshInst.Mesh = new CapsuleMesh { Radius = 0.5f, Height = 1.8f };
			meshInst.Position = new Vector3(0, 0.9f, 0);
		}
		else
		{
			meshInst.Mesh = new BoxMesh { Size = new Vector3(2f, 2f, 2f) };
			meshInst.Position = new Vector3(0, 1.0f, 0);
		}

		meshInst.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.7f, 0.5f) };
		return meshInst;
	}

	private string? TryFindModelPath(string wsPath, string meshKey)
	{
		string[] subDirs = { "units", "buildings", "resources", "props", "projectiles", "characters", "items", "attachments", "weapons" };
		foreach (var sub in subDirs)
		{
			string p = Path.Combine(wsPath, "Assets", "models", sub, meshKey);
			if (File.Exists(p)) return p;
		}

		string modelsDir = Path.Combine(wsPath, "Assets", "models");
		if (Directory.Exists(modelsDir))
		{
			var files = Directory.GetFiles(modelsDir, meshKey, SearchOption.AllDirectories);
			if (files.Length > 0 && files[0].EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				return files[0];
			}
		}

		return null;
	}

	private Node3D? TryLoadRMeshOrGlb(string modelPath, string meshKey)
	{
		if (string.IsNullOrEmpty(modelPath) || !modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || !File.Exists(modelPath))
			return null;

		var loaded = ModelCache.GetModel(modelPath) ?? ModelCache.GetModel(meshKey);
		if (loaded is Node3D n) return n;
		if (loaded != null) loaded.QueueFree();

		byte[] rmeshBytes = File.ReadAllBytes(modelPath);
			var node = ModelCache.TryGenerateNodeFromRmeshOrGlbBytes(rmeshBytes);
			if (node is Node3D n3d) return n3d;
			if (node != null) node.QueueFree();

		return null;
	}

	private Node3D? GetShaderPreviewMesh(string meshKey)
	{
		if (meshKey.StartsWith("(")) return GetFallbackMesh(meshKey);
		if (string.IsNullOrEmpty(meshKey)) return null;

		string wsPath = GameContext.EditorService._editingDirectory;
		string? modelPath = TryFindModelPath(wsPath, meshKey);

		return modelPath != null ? TryLoadRMeshOrGlb(modelPath, meshKey) : null;
	}

	private void LoadWeaponPreview(ObjectItemInfo item)
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;

		string wsPath = GameContext.EditorService._editingDirectory;
		WeaponMetadata? weaponMeta = null;
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) && meta?.Templates?.Weapons != null)
		{
			weaponMeta = meta.Templates.Weapons.FirstOrDefault(w => string.Equals(w.TemplateID, item.TemplateID, StringComparison.OrdinalIgnoreCase));
		}

		weaponMeta ??= new WeaponMetadata
		{
			TemplateID = item.TemplateID,
			Name = item.Name,
			ProjectileModelPath = item.ModelPath
		};

		RestartPreviewWeaponProjectile(weaponMeta);
	}

	private void LoadVfxPreview(ObjectItemInfo item)
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;

		string wsPath = GameContext.EditorService._editingDirectory;
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) && meta?.Templates?.Vfx != null)
		{
			var vfxConfig = meta.Templates.Vfx.FirstOrDefault(v => string.Equals(v.VfxId, item.TemplateID, StringComparison.OrdinalIgnoreCase));
			if (vfxConfig != null)
			{
				_previewVfxInstance = new ProceduralVfxInstance3D { IsPreview = true };
				_currentModelRoot.AddChild(_previewVfxInstance);
				_previewVfxInstance.Initialize(vfxConfig);
			}
		}
	}

	private void LoadModelPreview(ObjectItemInfo item)
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;

		Node? mesh = null;
		try
		{
			mesh = ModelCache.GetModel(item.ModelPath);
			if (mesh != null)
			{
				_currentModelRoot.AddChild(mesh);
				float s = item.Scale > 0 ? item.Scale : 1.0f;
				_currentModelRoot.Scale = new Vector3(s, s, s);
			}
		}
		catch
		{
			if (mesh != null && mesh.GetParent() == null)
			{
				mesh.QueueFree();
			}
		}
	}

	private void LoadIconPreview(ObjectItemInfo item)
	{
		PreviewSubViewport.GetParent<Control>().Visible = false;
		_previewAudioContainer.Visible = false;
		_preview2DContainer.Visible = true;

		_preview2DImage.CustomMinimumSize = new Vector2(100, 100);
		_preview2DImage.Texture = LoadTexture2D(item.IconPath, "icons");
		_lblPreview2DInfo.Text = item.Name;
	}

	private void RestartPreviewWeaponProjectile(WeaponMetadata weapon)
	{
		ClearPreviewProjectile();
		if (PreviewSubViewport == null) return;

		_previewProjectile = new VisualProjectile3D();
		_previewProjectile.Name = "PreviewProjectile_VisualProjectile3D";
		PreviewSubViewport.AddChild(_previewProjectile);

		Vector3 startPos = new Vector3(-2.8f, 0.5f, 0f);
		Vector3 targetPos = new Vector3(2.8f, 0.5f, 0f);

		_previewProjectile.Initialize(weapon, startPos, targetPos, default, (proj) =>
		{
			if (Visible && PreviewSubViewport != null && _currentCategory == "weapons" && string.Equals(_currentPreviewTemplateID, weapon.TemplateID, StringComparison.OrdinalIgnoreCase))
			{
				Callable.From(() => RestartPreviewWeaponProjectile(weapon)).CallDeferred();
			}
		});
	}

	private void ClearPreviewProjectile()
	{
		if (_previewProjectile != null && GodotObject.IsInstanceValid(_previewProjectile))
		{
			_previewProjectile.QueueFree();
			_previewProjectile = null;
		}
	}


	private static readonly string[] _textureSubfolders = { "icons", "vfx", "vfx_radial", "vfx_vertical", "decals", "textures" };

	private Texture2D? LoadTexture2D(string? assetPath, string defaultSubFolder = "textures")
	{
		if (string.IsNullOrWhiteSpace(assetPath)) return null;

		var loaded = RtexIconLoader.Load(assetPath);
		if (loaded != null) return loaded;

		string wsPath = GameContext.EditorService._editingDirectory;
		string? diskPath = MapAssetHelper.FindAssetOnDisk(wsPath, defaultSubFolder, assetPath);

		if (string.IsNullOrEmpty(diskPath))
		{
			foreach (var folder in _textureSubfolders)
			{
				diskPath = MapAssetHelper.FindAssetOnDisk(wsPath, folder, assetPath);
				if (!string.IsNullOrEmpty(diskPath)) break;
			}
		}

		if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
		{
			return RtexIconLoader.Load(diskPath);
		}

		return null;
	}

	private void ToggleAudioPlayback()
	{
		if (_audioPlayer.Playing)
		{
			_audioPlayer.Stop();
			_btnAudioPlay.Text = $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("Play");
		}
		else if (_audioPlayer.Stream != null)
		{
			_audioPlayer.Play();
			_btnAudioPlay.Text = $"{UnicodeIcons.PAUSE} " + TranslationServer.Translate("Pause");
		}
	}

	private void OnAudioFinished()
	{
		_btnAudioPlay.Text = $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("Play");
	}
}
