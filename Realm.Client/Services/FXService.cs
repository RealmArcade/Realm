using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Shared.Textures;

namespace Realm.Client.Services;

public class FXService
{
	private readonly Dictionary<string, string> _resolvedPathCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Texture2D?> _textureCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, (SpriteFrames Frames, int FrameWidth)> _spriteFramesCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Stack<AnimatedSprite3D> _spritePool = new();
	private int _activeSpritesCount = 0;
	private const int MaxActiveSprites = 64;

	private static SphereMesh? _sharedHealSphereMesh;
	private static StandardMaterial3D? _sharedHealOrbMaterial;
	private readonly Stack<MeshInstance3D> _healOrbPool = new();

	private static TorusMesh? _sharedTargetIndicatorMesh;
	private readonly Stack<(MeshInstance3D Mesh, StandardMaterial3D Material)> _targetIndicatorPool = new();
	private int _activeTargetIndicatorsCount = 0;
	private const int MaxActiveTargetIndicators = 24;

	public List<MinimapPing> ActivePings { get; set; } = new();

	public void ClearActivePings()
	{
		ActivePings.Clear();
	}

	public FXService()
	{
	}

	public void SpawnAbilityEffect(Node3D parent, AbilityDefinition def, Vector3 position, float scale = 1.0f)
	{
		if (parent == null || !GodotObject.IsInstanceValid(parent) || def == null) return;

		Color indicatorColor = def.Healing > 0f ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.9f, 0.3f, 0.1f);

		if (!string.IsNullOrEmpty(def.VisualEffect))
		{
			string visual = def.VisualEffect;
			if (visual.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
			    visual.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
			    visual.Contains('/'))
			{
				SpawnSpritesheetEffect(parent, visual, position + new Vector3(0, 0.5f, 0), 4, 4, 0.04f, scale * 6f);
			}
			else
			{
				SpawnSpritesheetEffect(parent, $"Assets/vfx/{visual}_sheet.png", position + new Vector3(0, 0.5f, 0), 4, 4, 0.04f, scale * 6f);
			}
		}

		SpawnTargetIndicator(parent, position, indicatorColor);
	}

	public void SpawnSpritesheetEffect(Node parent, string texturePath, Vector3 worldPosition, int columns, int rows, float secondsPerFrame, float sizeInWorldUnits)
	{
		if (parent == null || !GodotObject.IsInstanceValid(parent)) return;
		if (_activeSpritesCount >= MaxActiveSprites) return;

		string fullPath = ResolveTexturePathCached(texturePath);
		string framesKey = $"{fullPath}|{columns}|{rows}|{secondsPerFrame:F4}";

		if (!_spriteFramesCache.TryGetValue(framesKey, out var cachedFrames))
		{
			Texture2D? texture = LoadTextureCached(fullPath);
			if (texture == null) return;

			int totalFrames = columns * rows;
			var frames = new SpriteFrames();
			frames.AddAnimation("play");
			frames.SetAnimationLoopMode("play", SpriteFrames.LoopMode.None);
			frames.SetAnimationSpeed("play", 1.0f / secondsPerFrame);

			int frameWidth = Math.Max(1, texture.GetWidth() / columns);
			int frameHeight = Math.Max(1, texture.GetHeight() / rows);

			for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
			{
				int col = frameIndex % columns;
				int row = frameIndex / columns;
				var atlasFrame = new AtlasTexture
				{
					Atlas = texture,
					Region = new Rect2(col * frameWidth, row * frameHeight, frameWidth, frameHeight)
				};
				frames.AddFrame("play", atlasFrame);
			}

			cachedFrames = (frames, frameWidth);
			_spriteFramesCache[framesKey] = cachedFrames;
		}

		var sprite = RentAnimatedSprite(parent);
		sprite.SpriteFrames = cachedFrames.Frames;
		sprite.Animation = "play";
		sprite.Frame = 0;
		sprite.Position = worldPosition;
		sprite.PixelSize = sizeInWorldUnits / cachedFrames.FrameWidth;
		sprite.Play("play");
	}

	private AnimatedSprite3D RentAnimatedSprite(Node parent)
	{
		AnimatedSprite3D? sprite = null;
		while (_spritePool.Count > 0)
		{
			var cand = _spritePool.Pop();
			if (cand != null && GodotObject.IsInstanceValid(cand))
			{
				sprite = cand;
				break;
			}
		}

		if (sprite == null)
		{
			sprite = new AnimatedSprite3D
			{
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				Transparent = true,
				AlphaCut = SpriteBase3D.AlphaCutMode.Disabled
			};
			var capturedSprite = sprite;
			sprite.AnimationFinished += () => ReturnAnimatedSprite(capturedSprite);
		}

		_activeSpritesCount++;
		sprite.Visible = true;
		if (sprite.GetParent() != parent)
		{
			if (sprite.GetParent() != null)
			{
				sprite.GetParent().RemoveChild(sprite);
			}
			parent.AddChild(sprite);
		}

		return sprite;
	}

	private void ReturnAnimatedSprite(AnimatedSprite3D sprite)
	{
		_activeSpritesCount = Math.Max(0, _activeSpritesCount - 1);
		if (sprite == null || !GodotObject.IsInstanceValid(sprite)) return;
		sprite.Stop();
		sprite.Visible = false;
		_spritePool.Push(sprite);
	}

	private string ResolveTexturePathCached(string texturePath)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string key = $"{wsPath}|{texturePath}";
		if (_resolvedPathCache.TryGetValue(key, out var cached))
		{
			return cached;
		}

		string resolved = ResolveTexturePath(texturePath, wsPath);
		_resolvedPathCache[key] = resolved;
		return resolved;
	}

	private static string ResolveTexturePath(string texturePath, string wsPath)
	{
		if (texturePath.StartsWith("res://") || File.Exists(texturePath))
			return texturePath;

		string fullPath = Path.Combine(wsPath, texturePath.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "vfx", Path.GetFileName(texturePath));
		if (File.Exists(fullPath)) return fullPath;

		string rtexCandidate = Path.ChangeExtension(fullPath, ".rtex");
		if (File.Exists(rtexCandidate)) return rtexCandidate;

		return fullPath;
	}

	private Texture2D? LoadTextureCached(string fullPath)
	{
		if (_textureCache.TryGetValue(fullPath, out var cached) && (cached == null || GodotObject.IsInstanceValid(cached)))
		{
			return cached;
		}

		var loaded = LoadTexture(fullPath);
		_textureCache[fullPath] = loaded;
		return loaded;
	}

	private static Texture2D? LoadTexture(string fullPath)
	{
		if (fullPath.StartsWith("res://"))
		{
			return GD.Load<Texture2D>(fullPath);
		}

		if (!File.Exists(fullPath))
			return null;

		Image? img = null;
		if (fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			img = LoadRtexImage(fullPath);
		}
		else
		{
			img = Image.LoadFromFile(fullPath);
		}

		if (img == null) return null;

		if (!img.HasMipmaps())
		{
			img.GenerateMipmaps();
		}

		return ImageTexture.CreateFromImage(img);
	}

	private static Image? LoadRtexImage(string fullPath)
	{
		byte[] rtexBytes = File.ReadAllBytes(fullPath);
		byte[]? webpBytes = RtexFile.GetLayer(rtexBytes, 0);
		if (webpBytes == null || webpBytes.Length == 0) return null;

		var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok)
		{
			img.LoadPngFromBuffer(webpBytes);
		}

		return img;
	}

	public void FlashDamageUnit(Unit3D unit)
	{
		if (unit == null || !GodotObject.IsInstanceValid(unit) || !unit.Visible || unit.IsFlashingScale) return;
		unit.IsFlashingScale = true;
		var tween = unit.CreateTween();
		unit.Scale = new Vector3(0.8f, 1.3f, 0.8f);
		tween.TweenProperty(unit, "scale", new Vector3(1.0f, 1.0f, 1.0f), 0.25f);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(unit)) unit.IsFlashingScale = false;
		}));
	}

	public void SpawnHealVisualEffect(Node3D parent, Vector3 start, Vector3 target)
	{
		if (parent == null || !GodotObject.IsInstanceValid(parent)) return;

		_sharedHealSphereMesh ??= new SphereMesh { Radius = 0.15f, Height = 0.3f };
		_sharedHealOrbMaterial ??= new StandardMaterial3D
		{
			AlbedoColor = new Color(0.2f, 0.9f, 0.3f),
			EmissionEnabled = true,
			Emission = new Color(0.1f, 0.8f, 0.2f)
		};

		MeshInstance3D? orb = null;
		while (_healOrbPool.Count > 0)
		{
			var cand = _healOrbPool.Pop();
			if (cand != null && GodotObject.IsInstanceValid(cand))
			{
				orb = cand;
				break;
			}
		}

		if (orb == null)
		{
			orb = new MeshInstance3D
			{
				Mesh = _sharedHealSphereMesh,
				MaterialOverride = _sharedHealOrbMaterial,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				GIMode = GeometryInstance3D.GIModeEnum.Disabled
			};
		}

		if (orb.GetParent() != parent)
		{
			if (orb.GetParent() != null) orb.GetParent().RemoveChild(orb);
			parent.AddChild(orb);
		}

		orb.Visible = true;
		orb.Position = start + new Vector3(0, 1.5f, 0);

		Vector3 targetPos = target + new Vector3(0, 1.2f, 0);

		var capturedOrb = orb;
		var tween = parent.CreateTween();
		float travelTime = Mathf.Clamp(start.DistanceTo(target) / 25f, 0.2f, 0.8f);
		tween.TweenProperty(capturedOrb, "global_position", targetPos, travelTime);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(capturedOrb))
			{
				capturedOrb.Visible = false;
				_healOrbPool.Push(capturedOrb);
			}
		}));
	}

	public void FlashHealUnit(Unit3D unit)
	{
		if (unit == null || !GodotObject.IsInstanceValid(unit) || !unit.Visible || unit.IsFlashingScale) return;
		unit.IsFlashingScale = true;
		var tween = unit.CreateTween();
		unit.Scale = new Vector3(0.9f, 1.25f, 0.9f);
		tween.TweenProperty(unit, "scale", new Vector3(1.0f, 1.0f, 1.0f), 0.25f);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(unit)) unit.IsFlashingScale = false;
		}));
	}

	public void SpawnPing3DEffect(Node3D parent, Vector3 position)
	{
		var meshInstance = new MeshInstance3D();
		meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		meshInstance.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
		var torusMesh = new TorusMesh();
		torusMesh.InnerRadius = 2.0f;
		torusMesh.OuterRadius = 2.4f;
		meshInstance.Mesh = torusMesh;
		meshInstance.Position = position + new Vector3(0, 0.1f, 0);

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(1.0f, 0.1f, 0.1f, 0.8f);
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		material.DisableReceiveShadows = true;
		material.EmissionEnabled = true;
		material.Emission = new Color(1.0f, 0.1f, 0.1f);
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		meshInstance.MaterialOverride = material;

		parent.AddChild(meshInstance);

		var tween = parent.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(meshInstance, "scale", new Vector3(4f, 0.1f, 4f), 0.8f);
		tween.TweenProperty(material, "albedo_color:a", 0.0f, 0.8f);
		tween.TweenProperty(material, "emission:a", 0.0f, 0.8f);
		tween.Chain().TweenCallback(Callable.From(meshInstance.QueueFree));
	}

	public void SpawnWeaponProjectile(Node3D parent, Vector3 start, Vector3 target, string weaponId = null, Entity targetEntity = default)
	{
		WeaponMetadata weaponMeta = null;
		bool hasWeapon = false;
		if (!string.IsNullOrEmpty(weaponId) && GameHost.WeaponRegistry.TryGetValue(weaponId, out var registeredWeapon))
		{
			weaponMeta = registeredWeapon;
			hasWeapon = true;
		}
		else if (GameHost.WeaponRegistry.TryGetValue("arrow", out var defaultArrow))
		{
			weaponMeta = defaultArrow;
			hasWeapon = true;
		}

		var projectile = VisualProjectilePool.Rent(parent);

		Vector3 spawnPos = start + new Vector3(0, 1.2f, 0);
		Vector3 targetPos = target + new Vector3(0, 1.2f, 0);

		if (hasWeapon)
		{
			projectile.Initialize(weaponMeta, spawnPos, targetPos, targetEntity, p => VisualProjectilePool.Return(p));
		}
		else
		{
			projectile.Initialize("arrow", spawnPos, targetPos, targetEntity, p => VisualProjectilePool.Return(p));
		}
	}

	public void SpawnArrowProjectile(Node3D parent, Vector3 start, Vector3 target)
	{
		SpawnWeaponProjectile(parent, start, target, "arrow");
	}

	private readonly Stack<Label3D> _floatingLabelPool = new();
	private int _activeFloatingLabelsCount = 0;
	private const int MaxActiveFloatingLabels = 48;

	private Label3D RentFloatingLabel(Node3D parent)
	{
		Label3D label = null;
		while (_floatingLabelPool.Count > 0)
		{
			var cand = _floatingLabelPool.Pop();
			if (cand != null && GodotObject.IsInstanceValid(cand))
			{
				label = cand;
				break;
			}
		}

		if (label == null)
		{
			label = new Label3D();
			label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		}

		_activeFloatingLabelsCount++;
		label.Visible = true;
		if (label.GetParent() != parent)
		{
			if (label.GetParent() != null)
			{
				label.GetParent().RemoveChild(label);
			}
			parent.AddChild(label);
		}

		return label;
	}

	private void ReturnFloatingLabel(Label3D label)
	{
		_activeFloatingLabelsCount = Math.Max(0, _activeFloatingLabelsCount - 1);
		if (label == null || !GodotObject.IsInstanceValid(label)) return;
		label.Visible = false;
		_floatingLabelPool.Push(label);
	}

	public void SpawnDamageNumber(Node3D parent, Vector3 worldPosition, float amount, bool isCritical = false)
	{
		if (!GameSettings.FloatingCombatText) return;
		if (parent == null || !GodotObject.IsInstanceValid(parent)) return;
		if (_activeFloatingLabelsCount >= MaxActiveFloatingLabels) return;

		int amountInt = (int)Math.Round(amount);
		if (amountInt <= 0) return;

		bool isHeavyOrCrit = isCritical || amount >= 75f;

		var label = RentFloatingLabel(parent);
		label.Text = isHeavyOrCrit ? $"CRIT! -{amountInt}" : $"-{amountInt}";
		label.Modulate = isHeavyOrCrit ? new Color(1.0f, 0.85f, 0.2f, 1.0f) : new Color(1.0f, 0.25f, 0.25f, 1.0f);
		label.OutlineModulate = Colors.Black;
		label.OutlineSize = isHeavyOrCrit ? 14 : 10;
		label.FontSize = isHeavyOrCrit ? 52 : 38;

		float offsetX = (float)GD.RandRange(-0.25, 0.25);
		float offsetZ = (float)GD.RandRange(-0.25, 0.25);
		label.Position = worldPosition + new Vector3(offsetX, 1.8f, offsetZ);

		var tween = parent.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(label, "position", label.Position + new Vector3(0, 1.6f, 0), 1.0f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(label, "modulate:a", 0.0f, 1.0f)
			.SetTrans(Tween.TransitionType.Linear);
		tween.Chain().TweenCallback(Callable.From(() => ReturnFloatingLabel(label)));
	}

	public void SpawnHealNumber(Node3D parent, Vector3 worldPosition, float amount)
	{
		if (!GameSettings.FloatingCombatText) return;
		if (parent == null || !GodotObject.IsInstanceValid(parent)) return;
		if (_activeFloatingLabelsCount >= MaxActiveFloatingLabels) return;

		int amountInt = (int)Math.Round(amount);
		if (amountInt <= 0) return;

		var label = RentFloatingLabel(parent);
		label.Text = $"+{amountInt}";
		label.Modulate = new Color(0.2f, 0.95f, 0.3f, 1.0f);
		label.OutlineModulate = Colors.Black;
		label.OutlineSize = 10;
		label.FontSize = 38;

		float offsetX = (float)GD.RandRange(-0.25, 0.25);
		float offsetZ = (float)GD.RandRange(-0.25, 0.25);
		label.Position = worldPosition + new Vector3(offsetX, 1.8f, offsetZ);

		var tween = parent.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(label, "position", label.Position + new Vector3(0, 1.6f, 0), 1.0f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(label, "modulate:a", 0.0f, 1.0f)
			.SetTrans(Tween.TransitionType.Linear);
		tween.Chain().TweenCallback(Callable.From(() => ReturnFloatingLabel(label)));
	}

	public void SpawnTargetIndicator(Node3D parent, Vector3 position, Color color)
	{
		if (parent == null || !GodotObject.IsInstanceValid(parent)) return;
		if (_activeTargetIndicatorsCount >= MaxActiveTargetIndicators) return;

		_sharedTargetIndicatorMesh ??= new TorusMesh { InnerRadius = 0.8f, OuterRadius = 1.0f };

		MeshInstance3D? meshInstance = null;
		StandardMaterial3D? material = null;
		while (_targetIndicatorPool.Count > 0)
		{
			var cand = _targetIndicatorPool.Pop();
			if (cand.Mesh != null && GodotObject.IsInstanceValid(cand.Mesh) && cand.Material != null && GodotObject.IsInstanceValid(cand.Material))
			{
				meshInstance = cand.Mesh;
				material = cand.Material;
				break;
			}
		}

		if (meshInstance == null || material == null)
		{
			material = new StandardMaterial3D
			{
				EmissionEnabled = true,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				DisableReceiveShadows = true
			};
			meshInstance = new MeshInstance3D
			{
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				GIMode = GeometryInstance3D.GIModeEnum.Disabled,
				Mesh = _sharedTargetIndicatorMesh,
				MaterialOverride = material
			};
		}

		_activeTargetIndicatorsCount++;
		material.AlbedoColor = color;
		material.Emission = color;
		meshInstance.Scale = Vector3.One;
		meshInstance.Position = position + new Vector3(0, 0.05f, 0);
		meshInstance.Visible = true;

		if (meshInstance.GetParent() != parent)
		{
			if (meshInstance.GetParent() != null) meshInstance.GetParent().RemoveChild(meshInstance);
			parent.AddChild(meshInstance);
		}

		var capturedMesh = meshInstance;
		var capturedMat = material;
		var tween = parent.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(capturedMesh, "scale", new Vector3(1.6f, 0.1f, 1.6f), 0.35f);
		tween.TweenProperty(capturedMat, "albedo_color:a", 0.0f, 0.35f);
		tween.TweenProperty(capturedMat, "emission:a", 0.0f, 0.35f);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			_activeTargetIndicatorsCount = Math.Max(0, _activeTargetIndicatorsCount - 1);
			if (GodotObject.IsInstanceValid(capturedMesh))
			{
				capturedMesh.Visible = false;
				_targetIndicatorPool.Push((capturedMesh, capturedMat));
			}
		}));
	}

	public void AddMinimapPing(Node3D parent, List<MinimapPing> activePings, Vector3 position)
	{
		activePings.Add(new MinimapPing
		{
			WorldPos = position,
			LifeTime = 0f,
			MaxLifeTime = 3.0f
		});

		SpawnPing3DEffect(parent, position);

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"[ALERT PING] Signal at: {position.X:F0}, {position.Z:F0}", new Color(1.0f, 0.1f, 0.2f));
			UIManager.Instance?.PlayClickSound();
		}
	}
}