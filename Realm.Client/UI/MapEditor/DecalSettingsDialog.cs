using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class DecalSettingsDialog : FloatingDialogBase
{
	private const string MultiplierSuffix = "x";

	public DecalSettingsDialog() { }

	private string _decalKey = "";
	private string _texturePath = "";
	private float _brightness = 1.0f;
	private Color _tint = Colors.White;
	private float _contrast = 1.0f;
	private float _saturation = 1.0f;
	private float _opacity = 1.0f;
	private float _albedoMix = 1.0f;
	private float _normalStrength = 0.0f;
	private float _roughness = 1.0f;
	private float _metallic = 0.0f;
	private string _blendMode = "Mix";

	private bool _animateOpacity = false;
	private float _opacityPulseSpeed = 1.0f;
	private float _minOpacity = 0.2f;
	private float _maxOpacity = 1.0f;

	private bool _animateEmission = false;
	private float _emissionPulseSpeed = 1.0f;
	private float _minEmission = 0.0f;
	private float _maxEmission = 2.0f;

	private bool _animateScale = false;
	private float _scalePulseSpeed = 1.0f;
	private float _minScaleRatio = 0.8f;
	private float _maxScaleRatio = 1.2f;

	private float _upperFade = 0.3f;
	private float _lowerFade = 0.3f;

	private VfxPlacementMode _placementMode = VfxPlacementMode.Free;
	private float _projectionSize = 0.0f;
	private float _projectionHeight = 0.0f;
	private float _groundOffset = 0.05f;
	private float _normalFade = 0.0f;
	private bool _distanceFadeEnabled = false;
	private float _distanceFadeBegin = 10.0f;
	private float _distanceFadeEnd = 30.0f;

	private bool _isSyncingControls = false;
	private string _slug = "";
	private Label _lblObjectTypePrefix = null!;
	private LineEdit _txtSlug = null!;
	private LineEdit _txtTexturePath = null!;
	private Action<string> _setTexturePathValue = null!;
	private CheckBox _chkAnimateOpacity = null!;
	private HSlider _sldOpacitySpeed = null!;
	private Label _lblOpacitySpeed = null!;
	private HSlider _sldMinOpacity = null!;
	private Label _lblMinOpacity = null!;
	private HSlider _sldMaxOpacity = null!;
	private Label _lblMaxOpacity = null!;

	private CheckBox _chkAnimateEmission = null!;
	private HSlider _sldEmissionSpeed = null!;
	private Label _lblEmissionSpeed = null!;
	private HSlider _sldMinEmission = null!;
	private Label _lblMinEmission = null!;
	private HSlider _sldMaxEmission = null!;
	private Label _lblMaxEmission = null!;

	private CheckBox _chkAnimateScale = null!;
	private HSlider _sldScaleSpeed = null!;
	private Label _lblScaleSpeed = null!;
	private HSlider _sldMinScale = null!;
	private Label _lblMinScale = null!;
	private HSlider _sldMaxScale = null!;
	private Label _lblMaxScale = null!;

	private HSlider _sldUpperFade = null!;
	private Label _lblUpperFade = null!;
	private HSlider _sldLowerFade = null!;
	private Label _lblLowerFade = null!;

	private OptionButton _optPlacementMode = null!;
	private HSlider _sldProjectionSize = null!;
	private Label _lblProjectionSize = null!;
	private HSlider _sldProjectionHeight = null!;
	private Label _lblProjectionHeight = null!;
	private HSlider _sldGroundOffset = null!;
	private Label _lblGroundOffset = null!;
	private HSlider _sldNormalFade = null!;
	private Label _lblNormalFade = null!;
	private CheckBox _chkDistanceFade = null!;
	private HSlider _sldDistanceFadeBegin = null!;
	private Label _lblDistanceFadeBegin = null!;
	private HSlider _sldDistanceFadeEnd = null!;
	private Label _lblDistanceFadeEnd = null!;

	private double _previewAnimTime = 0.0;


	private readonly Dictionary<ulong, DecalNodeState> _originalDecalStates = new();

	private DecalSnapshot _initialSnapshot = null!;
	private string? _initialTemplateJson = null;
	private bool _templateExistedInitially = false;
	private Action<JsonObject> _onApplied = null!;

	private Label _lblDecalName = null!;
	private TextureRect _previewRect = null!;
	private Texture2D? _baseTexture;

	private HSlider _sldBrightness = null!;
	private Label _lblBrightness = null!;
	private ColorPickerButton _btnTint = null!;
	private HSlider _sldContrast = null!;
	private Label _lblContrast = null!;
	private HSlider _sldSaturation = null!;
	private Label _lblSaturation = null!;
	private HSlider _sldOpacity = null!;
	private Label _lblOpacity = null!;
	private HSlider _sldAlbedoMix = null!;
	private Label _lblAlbedoMix = null!;
	private HSlider _sldNormalStrength = null!;
	private Label _lblNormalStrength = null!;
	private HSlider _sldRoughness = null!;
	private Label _lblRoughness = null!;
	private HSlider _sldMetallic = null!;
	private Label _lblMetallic = null!;
	private OptionButton _optBlendMode = null!;

	public DecalSettingsDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Decal Rendering & Blending Properties"), new Vector2(480, 720))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(600);
		var contentVBox = CreateExpandingContentColumn();
		scrollBody.AddChild(contentVBox);

		_lblDecalName = new Label();
		_lblDecalName.Name = "LblDecalName_Label";
		_lblDecalName.AddThemeFontSizeOverride("font_size", 12);
		_lblDecalName.AddThemeColorOverride("font_color", new Color(0.95f, 0.82f, 0.55f));
		_lblDecalName.HorizontalAlignment = HorizontalAlignment.Center;
		contentVBox.AddChild(_lblDecalName);

		BuildPreviewSection(contentVBox);
		BuildTextureAssetSection(contentVBox);
		BuildColorLightingSection(contentVBox);
		BuildBlendingMaterialSection(contentVBox);
		BuildPlacementSection(contentVBox);
		BuildPropertyAnimationSection(contentVBox);
	}


	private void ReloadBaseTexture()
	{
		string texKey = !string.IsNullOrWhiteSpace(_texturePath)
			? _texturePath
			: (!string.IsNullOrWhiteSpace(_decalKey) && _decalKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? _decalKey : string.Empty);

		if (string.IsNullOrWhiteSpace(texKey))
		{
			_baseTexture = null;
			return;
		}

		_baseTexture = GameContext.Host?.LoadDecalTexture(texKey);
		if (_baseTexture != null) return;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string filename = Path.GetFileName(texKey);
		string baseKey = Path.GetFileNameWithoutExtension(texKey);

		string[] candidates = new[]
		{
			Path.Combine(wsPath, "Assets", "decals", texKey),
			Path.Combine(wsPath, "Assets", "decals", filename),
			Path.Combine(wsPath, "Assets", "decals", $"{baseKey}.rtex"),
			Path.Combine(wsPath, "Assets", $"{baseKey}.rtex"),
			Path.Combine(wsPath, texKey)
		};

		foreach (var p in candidates)
		{
			if (TryLoadRtexTexture(p)) break;
		}
	}

	private bool TryLoadRtexTexture(string path)
	{
		if (!File.Exists(path) || !path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) return false;

		byte[] rtexBytes = File.ReadAllBytes(path);
		byte[]? webpBytes = RtexFile.GetLayer(rtexBytes, 0);
		if (webpBytes == null || webpBytes.Length == 0) return false;

		var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		if (img.LoadWebpFromBuffer(webpBytes) == Error.Ok || img.LoadPngFromBuffer(webpBytes) == Error.Ok)
		{
			_baseTexture = ImageTexture.CreateFromImage(img);
			return true;
		}

		return false;
	}

	public static JsonObject ResolveDecalMetadata(string decalKey, JsonObject? providedData = null)
	{
		var result = new JsonObject();
		if (providedData != null)
		{
			foreach (var kvp in providedData)
			{
				result[kvp.Key] = kvp.Value?.DeepClone();
			}
		}

		ResolveFromMetadataJson(decalKey, result);
		ResolveFromRtexFile(decalKey, result);
		ResolveFromInWorldDecal(decalKey, result);

		return result;
	}

	private static void ResolveFromMetadataJson(string decalKey, JsonObject result)
	{
		try
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata?.Decals == null) return;

			string key = Path.GetFileName(decalKey);
			string baseKey = Path.GetFileNameWithoutExtension(decalKey);

			DecalMetadata? foundMeta = null;
			string[] candidates = new[] { decalKey, key, baseKey, $"{baseKey}.rtex", $"{baseKey}.png", $"{baseKey}.webp" };

			foreach (var candidate in candidates)
			{
				if (metadata.Decals.TryGetValue(candidate, out var meta))
				{
					foundMeta = meta;
					break;
				}
			}

			if (foundMeta == null) return;

			MergeMetadataJson(foundMeta, result);
		}
		catch
		{
		}
	}

	private static void MergeMetadataJson(DecalMetadata foundMeta, JsonObject result)
	{
		var jsonMeta = JsonNode.Parse(JsonSerializer.Serialize(foundMeta)) as JsonObject;
		if (jsonMeta == null) return;

		foreach (var kvp in jsonMeta)
		{
			if (!result.ContainsKey(kvp.Key))
			{
				result[kvp.Key] = kvp.Value?.DeepClone();
			}
		}
	}

	private static void ResolveFromRtexFile(string decalKey, JsonObject result)
	{
		if (result.ContainsKey("brightness") || result.ContainsKey("tint")) return;

		try
		{
			string targetTexture = GetTargetTexturePath(decalKey, result);
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

			ResolveRtexCandidates(wsPath, targetTexture, result);
		}
		catch
		{
		}
	}

	private static string GetTargetTexturePath(string decalKey, JsonObject result)
	{
		if (result.TryGetPropertyValue("texture_path", out var tpNode) && !string.IsNullOrWhiteSpace(tpNode?.ToString()))
		{
			return tpNode.ToString()!;
		}

		if (result.TryGetPropertyValue("TexturePath", out var tpNode2) && !string.IsNullOrWhiteSpace(tpNode2?.ToString()))
		{
			return tpNode2.ToString()!;
		}

		return decalKey;
	}

	private static void ResolveRtexCandidates(string wsPath, string targetTexture, JsonObject result)
	{
		string filename = Path.GetFileName(targetTexture);
		string baseKey = Path.GetFileNameWithoutExtension(targetTexture);
		string[] candidates = new[]
		{
			Path.Combine(wsPath, "Assets", "decals", targetTexture),
			Path.Combine(wsPath, "Assets", "decals", filename),
			Path.Combine(wsPath, "Assets", "decals", $"{baseKey}.rtex"),
			Path.Combine(wsPath, "Assets", $"{baseKey}.rtex")
		};

		foreach (var path in candidates)
		{
			if (TryResolveRtexFile(path, result)) break;
		}
	}

	private static bool TryResolveRtexFile(string path, JsonObject result)
	{
		if (!File.Exists(path) || !path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) return false;

		byte[] bytes = File.ReadAllBytes(path);
		if (!RtexFile.IsRtexBytes(bytes)) return false;

		var (customJson, _, _) = RtexFile.Parse(bytes);
		if (string.IsNullOrEmpty(customJson)) return false;

		var rtexMeta = JsonNode.Parse(customJson)?.AsObject();
		if (rtexMeta == null) return false;

		foreach (var kvp in rtexMeta)
		{
			if (!result.ContainsKey(kvp.Key))
			{
				result[kvp.Key] = kvp.Value?.DeepClone();
			}
		}

		return true;
	}

	private static void ResolveFromInWorldDecal(string decalKey, JsonObject result)
	{
		if (result.ContainsKey("brightness")) return;
		if (result.ContainsKey("tint")) return;
		if (GameContext.Host == null) return;
		if (GameContext.Host.AllDecals == null) return;

		string baseKey = Path.GetFileNameWithoutExtension(decalKey);
		foreach (var d in GameContext.Host.AllDecals)
		{
			if (TryMatchAndApplyDecal(d, decalKey, baseKey, result))
			{
				break;
			}
		}
	}

	private static bool TryMatchAndApplyDecal(Decal d, string decalKey, string baseKey, JsonObject result)
	{
		if (d == null) return false;
		if (!GodotObject.IsInstanceValid(d)) return false;
		if (!(d is Decal3D d3d)) return false;

		string dId = d3d.DecalId;
		string dBase = Path.GetFileNameWithoutExtension(dId);

		if (dId.Equals(decalKey, StringComparison.OrdinalIgnoreCase))
		{
			ApplyDecalStateToResult(d, result);
			return true;
		}

		if (dBase.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
		{
			ApplyDecalStateToResult(d, result);
			return true;
		}

		return false;
	}

	private static void ApplyDecalStateToResult(Decal d, JsonObject result)
	{
		result["albedo_mix"] = d.AlbedoMix;
		result["tint"] = $"#{d.Modulate.ToHtml(false)}";
		result["opacity"] = d.Modulate.A;
		result["normal_strength"] = (d.TextureNormal != null) ? 1.0f : 0.0f;
		result["roughness"] = 1.0f;
		result["metallic"] = 0.0f;
		result["upper_fade"] = d.UpperFade;
		result["lower_fade"] = d.LowerFade;
		result["normal_fade"] = d.NormalFade;
		result["distance_fade_enabled"] = d.DistanceFadeEnabled;
		result["distance_fade_begin"] = d.DistanceFadeBegin;
		result["distance_fade_end"] = d.DistanceFadeBegin + d.DistanceFadeLength;
		result["projection_size"] = d.Size.X;
		result["projection_height"] = d.Size.Z;

		if (d is Decal3D placementDecal)
		{
			result["PlacementMode"] = (int)(placementDecal.GroundProjected ? VfxPlacementMode.GroundProjected : VfxPlacementMode.Free);
			result["ground_offset"] = placementDecal.GroundOffset;
		}

		if (d.TextureEmission != null)
		{
			result["blend_mode"] = d.AlbedoMix <= 0.01f ? "Additive" : "Screen";
		}
		else
		{
			result["blend_mode"] = "Mix";
		}

		if (d is Decal3D d3dAnim)
		{
			result["animate_opacity"] = d3dAnim.Animation.AnimateOpacity;
			result["opacity_pulse_speed"] = d3dAnim.Animation.OpacityPulseSpeed;
			result["min_opacity"] = d3dAnim.Animation.MinOpacity;
			result["max_opacity"] = d3dAnim.Animation.MaxOpacity;
			result["animate_emission"] = d3dAnim.Animation.AnimateEmission;
			result["emission_pulse_speed"] = d3dAnim.Animation.EmissionPulseSpeed;
			result["min_emission"] = d3dAnim.Animation.MinEmission;
			result["max_emission"] = d3dAnim.Animation.MaxEmission;
			result["animate_scale"] = d3dAnim.Animation.AnimateScale;
			result["scale_pulse_speed"] = d3dAnim.Animation.ScalePulseSpeed;
			result["min_scale_ratio"] = d3dAnim.Animation.MinScaleRatio;
			result["max_scale_ratio"] = d3dAnim.Animation.MaxScaleRatio;
		}
	}

	public void OpenForDecal(string decalKey, JsonObject decalData, Action<JsonObject> onApplied)
	{
		var (_, parsedSlug) = TemplateIDHelper.ParseTemplateID(decalKey);
		_slug = !string.IsNullOrWhiteSpace(parsedSlug) ? TemplateIDHelper.ToSnakeCase(parsedSlug) : TemplateIDHelper.ToSnakeCase(decalKey);
		_decalKey = TemplateIDHelper.NormalizeTemplateID("decal", _slug);
		_onApplied = onApplied;
		_lblDecalName.Text = TranslationServer.Translate("TemplateID:") + " " + _decalKey;

		var resolvedData = ResolveDecalMetadata(decalKey, decalData);

		_texturePath = ResolveTexturePath(decalKey, resolvedData);

		_brightness = GetFloat(resolvedData, "brightness", 1.0f);
		_contrast = GetFloat(resolvedData, "contrast", 1.0f);
		_saturation = GetFloat(resolvedData, "saturation", 1.0f);
		_opacity = GetFloat(resolvedData, "opacity", 1.0f);
		_albedoMix = GetFloat(resolvedData, "albedo_mix", 1.0f);
		_normalStrength = GetFloat(resolvedData, "normal_strength", 0.0f);
		_roughness = GetFloat(resolvedData, "roughness", 1.0f);
		_metallic = GetFloat(resolvedData, "metallic", 0.0f);
		_blendMode = GetString(resolvedData, "blend_mode", "Mix");

		_animateOpacity = GetBool(resolvedData, "animate_opacity", false);
		_opacityPulseSpeed = GetFloat(resolvedData, "opacity_pulse_speed", 1.0f);
		_minOpacity = GetFloat(resolvedData, "min_opacity", 0.2f);
		_maxOpacity = GetFloat(resolvedData, "max_opacity", 1.0f);

		_animateEmission = GetBool(resolvedData, "animate_emission", false);
		_emissionPulseSpeed = GetFloat(resolvedData, "emission_pulse_speed", 1.0f);
		_minEmission = GetFloat(resolvedData, "min_emission", 0.0f);
		_maxEmission = GetFloat(resolvedData, "max_emission", 2.0f);

		_animateScale = GetBool(resolvedData, "animate_scale", false);
		_scalePulseSpeed = GetFloat(resolvedData, "scale_pulse_speed", 1.0f);
		_minScaleRatio = GetFloat(resolvedData, "min_scale_ratio", 0.8f);
		_maxScaleRatio = GetFloat(resolvedData, "max_scale_ratio", 1.2f);

		_upperFade = GetFloat(resolvedData, "upper_fade", 0.3f);
		_lowerFade = GetFloat(resolvedData, "lower_fade", 0.3f);

		_placementMode = GetPlacementMode(resolvedData, "PlacementMode", "placement_mode", VfxPlacementMode.Free);
		_projectionSize = GetFloat(resolvedData, "projection_size", 0.0f);
		_projectionHeight = GetFloat(resolvedData, "projection_height", 0.0f);
		_groundOffset = GetFloat(resolvedData, "ground_offset", 0.05f);
		_normalFade = GetFloat(resolvedData, "normal_fade", 0.0f);
		_distanceFadeEnabled = GetBool(resolvedData, "distance_fade_enabled", false);
		_distanceFadeBegin = GetFloat(resolvedData, "distance_fade_begin", 10.0f);
		_distanceFadeEnd = GetFloat(resolvedData, "distance_fade_end", 30.0f);

		_tint = Colors.White;
		if (resolvedData.TryGetPropertyValue("tint", out var tNode) && tNode != null)
		{
			string tStr = tNode.ToString();
			if (tStr.StartsWith("#")) _tint = Color.FromHtml(tStr);
		}

		TrackOriginalDecalStates(decalKey);
		TakeInitialSnapshot();

		SyncControlsWithValues();
		ReloadBaseTexture();
		UpdateLivePreviewAndWorld();
		OpenDialog();
	}

	protected override string GetRandomizeTemplateSlug()
	{
		return !string.IsNullOrEmpty(_decalKey) ? _decalKey : $"decal/{_slug}";
	}

	protected override void OnAssetsRandomized()
	{
		string targetId = GetRandomizeTemplateSlug();
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		var metadata = MapFileService.LoadMetadata(wsPath);
		if (metadata?.Decals != null)
		{
			var (_, curSlug) = TemplateIDHelper.ParseTemplateID(targetId);
			Realm.Shared.Metadata.DecalMetadata? foundMeta = null;
			if (metadata.Decals.TryGetValue(targetId, out foundMeta) ||
			    (!string.IsNullOrEmpty(curSlug) && metadata.Decals.TryGetValue(curSlug, out foundMeta)) ||
			    (!string.IsNullOrEmpty(_texturePath) && metadata.Decals.TryGetValue(_texturePath, out foundMeta)))
			{
				if (foundMeta != null && !string.IsNullOrEmpty(foundMeta.TexturePath))
				{
					_texturePath = foundMeta.TexturePath;
				}
			}
		}

		SyncControlsWithValues();
		ReloadBaseTexture();
		UpdateLivePreviewAndWorld();
	}

	private string ResolveTexturePath(string decalKey, JsonObject resolvedData)
	{
		string rawTexturePath = GetRawTexturePath(resolvedData);

		if (string.IsNullOrWhiteSpace(rawTexturePath) && decalKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			rawTexturePath = decalKey;
		}

		if (string.IsNullOrWhiteSpace(rawTexturePath))
		{
			rawTexturePath = ResolveFallbackTexturePath();
		}

		return rawTexturePath ?? string.Empty;
	}

	private string GetRawTexturePath(JsonObject resolvedData)
	{
		if (resolvedData.TryGetPropertyValue("texture_path", out var tpNode) && !string.IsNullOrWhiteSpace(tpNode?.ToString()))
		{
			return tpNode.ToString()!;
		}

		if (resolvedData.TryGetPropertyValue("TexturePath", out var tpNode2) && !string.IsNullOrWhiteSpace(tpNode2?.ToString()))
		{
			return tpNode2.ToString()!;
		}

		return string.Empty;
	}

	private string ResolveFallbackTexturePath()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string candidate = $"{_slug}.rtex";

		if (File.Exists(Path.Combine(wsPath, "Assets", "decals", candidate)))
		{
			return candidate;
		}

		var candidates = ScanAvailableAssets("decals", true);
		string? candidateMatch = candidates.FirstOrDefault(c => string.Equals(c, candidate, StringComparison.OrdinalIgnoreCase))
		                        ?? candidates.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), _slug, StringComparison.OrdinalIgnoreCase));

		return !string.IsNullOrEmpty(candidateMatch) ? candidateMatch : string.Empty;
	}

	private float GetFloat(JsonObject data, string key, float defaultVal)
	{
		return data.TryGetPropertyValue(key, out var node) && float.TryParse(node?.ToString(), out float val) ? val : defaultVal;
	}

	private bool GetBool(JsonObject data, string key, bool defaultVal)
	{
		return data.TryGetPropertyValue(key, out var node) && bool.TryParse(node?.ToString(), out bool val) ? val : defaultVal;
	}

	private string GetString(JsonObject data, string key, string defaultVal)
	{
		return data.TryGetPropertyValue(key, out var node) ? node?.ToString() ?? defaultVal : defaultVal;
	}

	private VfxPlacementMode GetPlacementMode(JsonObject data, string key, string alternateKey, VfxPlacementMode defaultVal)
	{
		foreach (string candidateKey in new[] { key, alternateKey })
		{
			if (!data.TryGetPropertyValue(candidateKey, out var node) || node == null) continue;

			string raw = node.ToString();
			if (Enum.TryParse<VfxPlacementMode>(raw, true, out var parsedMode))
			{
				return parsedMode;
			}

			if (int.TryParse(raw, out int numericMode) && Enum.IsDefined(typeof(VfxPlacementMode), numericMode))
			{
				return (VfxPlacementMode)numericMode;
			}
		}

		return defaultVal;
	}


	private void SyncControlsWithValues()
	{
		_isSyncingControls = true;
		try
		{
			SyncBaseControls();
			SyncMaterialControls();
			SyncAnimationControls();
			SyncFadeControls();
			SyncPlacementControls();
		}
		finally
		{
			_isSyncingControls = false;
		}
	}

	private void SyncBaseControls()
	{
		if (_txtSlug != null) _txtSlug.Text = _slug;
		_setTexturePathValue?.Invoke(_texturePath);

		SyncSlider(_sldBrightness, _lblBrightness, _brightness, "F2");
		if (_lblBrightness != null) _lblBrightness.Text += MultiplierSuffix;

		if (_btnTint != null) _btnTint.Color = _tint;

		SyncSlider(_sldContrast, _lblContrast, _contrast, "F2");
		if (_lblContrast != null) _lblContrast.Text += MultiplierSuffix;

		SyncSlider(_sldSaturation, _lblSaturation, _saturation, "F2");
		if (_lblSaturation != null) _lblSaturation.Text += MultiplierSuffix;

		SyncSlider(_sldOpacity, _lblOpacity, _opacity, "F2");
	}

	private void SyncSlider(HSlider? slider, Label? label, float value, string format)
	{
		if (slider != null) slider.Value = value;
		if (label != null) label.Text = value.ToString(format);
	}

	private void SyncMaterialControls()
	{
		SyncSlider(_sldAlbedoMix, _lblAlbedoMix, _albedoMix, "F2");

		SyncSlider(_sldNormalStrength, _lblNormalStrength, _normalStrength, "F2");
		if (_lblNormalStrength != null) _lblNormalStrength.Text += MultiplierSuffix;

		SyncSlider(_sldRoughness, _lblRoughness, _roughness, "F2");
		SyncSlider(_sldMetallic, _lblMetallic, _metallic, "F2");

		SyncBlendModeControl();
	}

	private void SyncBlendModeControl()
	{
		if (_optBlendMode == null) return;

		_optBlendMode.Selected = _blendMode switch
		{
			"Additive" => 1,
			"Multiply" => 2,
			"Screen" => 3,
			_ => 0
		};
	}

	private void SyncAnimationControls()
	{
		SyncOpacityAnimationControls();
		SyncEmissionAnimationControls();
		SyncScaleAnimationControls();
	}

	private void SyncOpacityAnimationControls()
	{
		if (_chkAnimateOpacity != null) _chkAnimateOpacity.ButtonPressed = _animateOpacity;

		SyncSlider(_sldOpacitySpeed, _lblOpacitySpeed, _opacityPulseSpeed, "F1");
		if (_lblOpacitySpeed != null) _lblOpacitySpeed.Text += MultiplierSuffix;

		SyncSlider(_sldMinOpacity, _lblMinOpacity, _minOpacity, "F2");
		SyncSlider(_sldMaxOpacity, _lblMaxOpacity, _maxOpacity, "F2");
	}

	private void SyncEmissionAnimationControls()
	{
		if (_chkAnimateEmission != null) _chkAnimateEmission.ButtonPressed = _animateEmission;

		SyncSlider(_sldEmissionSpeed, _lblEmissionSpeed, _emissionPulseSpeed, "F1");
		if (_lblEmissionSpeed != null) _lblEmissionSpeed.Text += MultiplierSuffix;

		SyncSlider(_sldMinEmission, _lblMinEmission, _minEmission, "F1");
		SyncSlider(_sldMaxEmission, _lblMaxEmission, _maxEmission, "F1");
	}

	private void SyncScaleAnimationControls()
	{
		if (_chkAnimateScale != null) _chkAnimateScale.ButtonPressed = _animateScale;

		SyncSlider(_sldScaleSpeed, _lblScaleSpeed, _scalePulseSpeed, "F1");
		if (_lblScaleSpeed != null) _lblScaleSpeed.Text += MultiplierSuffix;

		SyncSlider(_sldMinScale, _lblMinScale, _minScaleRatio, "F2");
		if (_lblMinScale != null) _lblMinScale.Text += MultiplierSuffix;

		SyncSlider(_sldMaxScale, _lblMaxScale, _maxScaleRatio, "F2");
		if (_lblMaxScale != null) _lblMaxScale.Text += MultiplierSuffix;
	}

	private void SyncFadeControls()
	{
		if (_sldUpperFade != null)
		{
			_sldUpperFade.Value = _upperFade;
			_lblUpperFade.Text = $"{_upperFade:F2}";
		}

		if (_sldLowerFade != null)
		{
			_sldLowerFade.Value = _lowerFade;
			_lblLowerFade.Text = $"{_lowerFade:F2}";
		}
	}

	private void SyncPlacementControls()
	{
		if (_optPlacementMode != null)
		{
			_optPlacementMode.Selected = _placementMode == VfxPlacementMode.GroundProjected ? 1 : 0;
		}

		SyncSlider(_sldProjectionSize, _lblProjectionSize, _projectionSize, "F1");
		SyncSlider(_sldProjectionHeight, _lblProjectionHeight, _projectionHeight, "F1");
		SyncSlider(_sldGroundOffset, _lblGroundOffset, _groundOffset, "F2");
		SyncSlider(_sldNormalFade, _lblNormalFade, _normalFade, "F2");

		if (_chkDistanceFade != null) _chkDistanceFade.ButtonPressed = _distanceFadeEnabled;
		SyncSlider(_sldDistanceFadeBegin, _lblDistanceFadeBegin, _distanceFadeBegin, "F0");
		SyncSlider(_sldDistanceFadeEnd, _lblDistanceFadeEnd, _distanceFadeEnd, "F0");
	}


}
