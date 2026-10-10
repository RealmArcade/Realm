using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Realm.Client.Core;

namespace Realm.Client.UI;

public static class GameSettings
{
	private const string SettingsPath = "user://settings.json";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	public static readonly Vector2I DefaultFallbackResolution = new Vector2I(1920, 1080);

	public static int ResolutionIdx { get; set; } = 0;
	public static int WindowedResolutionWidth { get; set; } = 0;
	public static int WindowedResolutionHeight { get; set; } = 0;
	public static List<Vector2I> Resolutions { get; private set; }
	public static GraphicsQuality QualityIdx { get; set; } = GraphicsQuality.High;
	public static WindowMode WindowModeIdx { get; set; } = WindowMode.Fullscreen;
	public static bool Vsync { get; set; } = true;
	public static bool VsyncIdx
	{
		get => Vsync;
		set => Vsync = value;
	}

	public static float MasterVolume { get; set; } = 80f;
	public static float MusicVolume { get; set; } = 70f;
	public static float SfxVolume { get; set; } = 90f;
	public static float VoiceVolume { get; set; } = 60f;

	public static float ScrollSpeed { get; set; } = 50f;
	public static float MouseSens { get; set; } = 40f;
	public static float HudScale { get; set; } = 100f; // 100% default
	public static HealthBarMode ShowHealthBars { get; set; } = HealthBarMode.Damaged;
	public static GameLanguage Language { get; set; } = GameLanguage.English;
	public static bool DisplayFps { get; set; } = false;
	public static bool RecordReplays { get; set; } = false;
	public static bool SeedMapFiles { get; set; } = false;
	public static bool DisableShadows { get; set; } = false;
	public static bool DisableDayNightLighting { get; set; } = false;
	public static bool FloatingCombatText { get; set; } = true;
	public static string LastOpenedFolder { get; set; } = string.Empty;

	public static int GetSafeScreenIndex()
	{
		int screen = DisplayServer.WindowGetCurrentScreen();
		if (screen < 0 || screen >= DisplayServer.GetScreenCount())
		{
			return 0;
		}
		return screen;
	}

	public static void ResetToDefaults()
	{
		ResolutionIdx = 0;
		if (Resolutions != null && Resolutions.Count > 0)
		{
			WindowedResolutionWidth = Resolutions[0].X;
			WindowedResolutionHeight = Resolutions[0].Y;
		}
		else
		{
			WindowedResolutionWidth = DefaultFallbackResolution.X;
			WindowedResolutionHeight = DefaultFallbackResolution.Y;
		}
		QualityIdx = AutoDetectQuality();
		WindowModeIdx = WindowMode.Fullscreen;
		Vsync = true;
		DisableShadows = false;
		DisableDayNightLighting = false;
		FloatingCombatText = true;
		MasterVolume = 80f;
		MusicVolume = 70f;
		SfxVolume = 90f;
		VoiceVolume = 60f;
		ScrollSpeed = 50f;
		MouseSens = 40f;
		HudScale = 100f;
		ShowHealthBars = HealthBarMode.Damaged;
		Language = GameLanguage.English;
		DisplayFps = false;
		RecordReplays = false;
		SeedMapFiles = false;
	}

	public static void InitializeResolutions()
	{
		int currentScreen = GetSafeScreenIndex();
		Vector2I screenSize = DisplayServer.ScreenGetSize(currentScreen);
		if (screenSize.X <= 0 || screenSize.Y <= 0)
		{
			screenSize = DefaultFallbackResolution;
		}
		var standardRes = new List<Vector2I>
		{
			new Vector2I(3840, 2160),
			new Vector2I(3440, 1440),
			new Vector2I(2560, 1600),
			new Vector2I(2560, 1440),
			new Vector2I(2560, 1080),
			new Vector2I(1920, 1200),
			new Vector2I(1920, 1080),
			new Vector2I(1680, 1050),
			new Vector2I(1600, 900),
			new Vector2I(1440, 900),
			new Vector2I(1366, 768),
			new Vector2I(1280, 800),
			new Vector2I(1280, 720)
		};

		var available = new List<Vector2I>();
		foreach (var res in standardRes)
		{
			if (res.X <= screenSize.X && res.Y <= screenSize.Y)
			{
				available.Add(res);
			}
		}

		if (!available.Contains(screenSize))
		{
			available.Add(screenSize);
		}

		available.Sort((a, b) =>
		{
			long areaA = (long)a.X * a.Y;
			long areaB = (long)b.X * b.Y;
			if (areaA != areaB) return areaB.CompareTo(areaA);
			return b.X.CompareTo(a.X);
		});

		Resolutions = available;
	}

	static GameSettings()
	{
		Load();
	}

	public static void Load()
	{
		if (Resolutions == null)
		{
			InitializeResolutions();
		}
		if (!FileAccess.FileExists(SettingsPath))
		{
			SetDefaultLoadSettings();
			return;
		}

		using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read);
		if (file == null) return;

		string json = file.GetAsText();
		try
		{
			var data = JsonSerializer.Deserialize<SettingsData>(json, JsonOptions);
			if (data != null)
			{
				ApplySettingsData(data);
			}
		}
		catch (System.Exception e)
		{
			GD.PrintErr($"Failed to deserialize settings: {e.Message}");
			ResetToDefaults();
		}
	}

	private static void SetDefaultLoadSettings()
	{
		QualityIdx = AutoDetectQuality();
		if (Resolutions != null && Resolutions.Count > 0)
		{
			WindowedResolutionWidth = Resolutions[0].X;
			WindowedResolutionHeight = Resolutions[0].Y;
		}
		Save();
	}

	private static void ApplySettingsData(SettingsData data)
	{
		ResolutionIdx = data.ResolutionIdx;
		WindowedResolutionWidth = data.WindowedResolutionWidth;
		WindowedResolutionHeight = data.WindowedResolutionHeight;
		QualityIdx = data.QualityIdx;
		WindowModeIdx = data.WindowModeIdx;
		Vsync = data.Vsync;
		MasterVolume = data.MasterVolume;
		MusicVolume = data.MusicVolume;
		SfxVolume = data.SfxVolume;
		VoiceVolume = data.VoiceVolume;
		ScrollSpeed = data.ScrollSpeed;
		MouseSens = data.MouseSens;
		HudScale = data.HudScale;
		Language = data.Language;
		DisplayFps = data.DisplayFps;
		RecordReplays = data.RecordReplays;
		SeedMapFiles = data.SeedMapFiles;
		DisableShadows = data.DisableShadows;
		DisableDayNightLighting = data.DisableDayNightLighting;
		FloatingCombatText = data.FloatingCombatText;
		ShowHealthBars = data.ShowHealthBars;
		LastOpenedFolder = data.LastOpenedFolder ?? string.Empty;

		ValidateResolution();
	}

	private static void ValidateResolution()
	{
		if (Resolutions == null || Resolutions.Count == 0) return;

		if (WindowedResolutionWidth > 0 && WindowedResolutionHeight > 0)
		{
			int matchIndex = Resolutions.FindIndex(r => r.X == WindowedResolutionWidth && r.Y == WindowedResolutionHeight);
			if (matchIndex >= 0)
			{
				ResolutionIdx = matchIndex;
				return;
			}
		}

		ResolutionIdx = Math.Clamp(ResolutionIdx, 0, Resolutions.Count - 1);
		WindowedResolutionWidth = Resolutions[ResolutionIdx].X;
		WindowedResolutionHeight = Resolutions[ResolutionIdx].Y;
	}

	public static void Save()
	{
		var data = new SettingsData
		{
			ResolutionIdx = ResolutionIdx,
			WindowedResolutionWidth = WindowedResolutionWidth,
			WindowedResolutionHeight = WindowedResolutionHeight,
			QualityIdx = QualityIdx,
			WindowModeIdx = WindowModeIdx,
			Vsync = Vsync,
			MasterVolume = MasterVolume,
			MusicVolume = MusicVolume,
			SfxVolume = SfxVolume,
			VoiceVolume = VoiceVolume,
			ScrollSpeed = ScrollSpeed,
			MouseSens = MouseSens,
			HudScale = HudScale,
			ShowHealthBars = ShowHealthBars,
			Language = Language,
			DisplayFps = DisplayFps,
			RecordReplays = RecordReplays,
			SeedMapFiles = SeedMapFiles,
			DisableShadows = DisableShadows,
			DisableDayNightLighting = DisableDayNightLighting,
			FloatingCombatText = FloatingCombatText,
			LastOpenedFolder = LastOpenedFolder
		};

		string json = JsonSerializer.Serialize(data, JsonOptions);
		using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
		if (file != null)
		{
			file.StoreString(json);
		}
	}

	public static void ApplyGraphicsSettings(Node contextNode)
	{
		if (contextNode == null || !GodotObject.IsInstanceValid(contextNode)) return;

		ApplyViewportQuality(contextNode.GetViewport());

		var tree = contextNode.GetTree();
		var root = (tree != null && GodotObject.IsInstanceValid(tree)) ? tree.Root : null;

		ApplyWorldEnvironment(root);
		ApplyTerrainQuality(root);
		ApplyGameHostSettings(root);
	}

	private static void ApplyViewportQuality(Viewport viewport)
	{
		if (viewport == null || !GodotObject.IsInstanceValid(viewport)) return;

		switch (QualityIdx)
		{
			case GraphicsQuality.Low:
				viewport.PositionalShadowAtlasSize = 512;
				viewport.UseTaa = false;
				viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
				break;
			case GraphicsQuality.Medium:
				viewport.PositionalShadowAtlasSize = 1024;
				viewport.UseTaa = false;
				viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
				break;
			case GraphicsQuality.High:
				viewport.PositionalShadowAtlasSize = 2048;
				viewport.UseTaa = false;
				viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
				break;
			case GraphicsQuality.Ultra:
				viewport.PositionalShadowAtlasSize = 4096;
				viewport.UseTaa = true;
				viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
				break;
		}

		viewport.Scaling3DScale = 1.0f;
		viewport.Msaa3D = Viewport.Msaa.Disabled;
		viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
	}

	private static void ApplyWorldEnvironment(Window root)
	{
		if (root == null || !GodotObject.IsInstanceValid(root)) return;

		var worldEnv = FindNodeInTree<WorldEnvironment>(root);
		if (worldEnv != null && GodotObject.IsInstanceValid(worldEnv) && worldEnv.Environment != null)
		{
			var env = worldEnv.Environment;
			if (!env.IsLocalToScene())
			{
				env = (global::Godot.Environment)env.Duplicate();
				worldEnv.Environment = env;
			}
			ApplyEnvironmentQuality(env, QualityIdx);
		}

		var light = FindNodeInTree<DirectionalLight3D>(root);
		if (light != null && GodotObject.IsInstanceValid(light))
		{
			ApplyDirectionalLightQuality(light, QualityIdx);
		}
	}

	private static void ApplyTerrainQuality(Window root)
	{
		var terrain = (root != null ? FindNodeInTree<Realm.Client.RuntimeTerrain>(root) : null) ?? Realm.Client.RuntimeTerrain.Instance;
		if (terrain != null && GodotObject.IsInstanceValid(terrain))
		{
			terrain.ApplyQualitySettings((int)QualityIdx);
		}
	}

	private static void ApplyGameHostSettings(Window root)
	{
		GameHost gameHost = GetValidGameHost(root);
		if (gameHost == null) return;
		
		var envService = gameHost.EnvironmentService;
		if (envService == null) return;

		if (DisableDayNightLighting)
		{
			envService.UpdateDayNightVisuals(gameHost, 0f);
			return;
		}

		float progress = GetTimeOfDayProgress(gameHost);
		envService.UpdateDayNightVisuals(gameHost, progress);
	}

	private static GameHost GetValidGameHost(Window root)
	{
		GameHost host = null;
		if (root != null)
		{
			host = FindNodeInTree<GameHost>(root);
		}
		
		if (host == null)
		{
			host = Realm.Client.Core.GameHost.Instance;
		}

		if (host == null) return null;
		if (!GodotObject.IsInstanceValid(host)) return null;
		return host;
	}

	private static float GetTimeOfDayProgress(GameHost gameHost)
	{
		if (gameHost.EcsWorld == null) return 0f;
		if (!gameHost.EcsWorld.IsAlive(gameHost.WorldEntity)) return 0f;
		if (!gameHost.EcsWorld.Has<Realm.Ecs.Components.Core.WorldState>(gameHost.WorldEntity)) return 0f;
		
		var state = gameHost.EcsWorld.Get<Realm.Ecs.Components.Core.WorldState>(gameHost.WorldEntity);
		return state.TimeOfDayTimer / Realm.Client.Core.GameHost.TimeOfDayCycleDuration;
	}

	public static void ApplyEnvironmentQuality(global::Godot.Environment env, GraphicsQuality quality = GraphicsQuality.High)
	{
		if (env == null || !GodotObject.IsInstanceValid(env)) return;

		env.TonemapMode = global::Godot.Environment.ToneMapper.Agx;
		env.AdjustmentEnabled = true;
		env.SsaoEnabled = quality > GraphicsQuality.Low;
		env.SsilEnabled = quality >= GraphicsQuality.High;
		env.SsrEnabled = quality == GraphicsQuality.Ultra;
		env.SdfgiEnabled = quality == GraphicsQuality.Ultra;
		env.FogEnabled = true;
		env.GlowEnabled = quality > GraphicsQuality.Low;
	}

	private static readonly Dictionary<GraphicsQuality, DirectionalLight3D.ShadowMode> _shadowModeMap = new()
	{
		{ GraphicsQuality.Low, DirectionalLight3D.ShadowMode.Orthogonal },
		{ GraphicsQuality.Medium, DirectionalLight3D.ShadowMode.Orthogonal },
		{ GraphicsQuality.High, DirectionalLight3D.ShadowMode.Parallel2Splits },
		{ GraphicsQuality.Ultra, DirectionalLight3D.ShadowMode.Parallel4Splits }
	};

	private static DirectionalLight3D.ShadowMode GetShadowMode(GraphicsQuality quality)
	{
		return _shadowModeMap.GetValueOrDefault(quality, DirectionalLight3D.ShadowMode.Parallel4Splits);
	}

	public static void ApplyDirectionalLightQuality(DirectionalLight3D light, GraphicsQuality quality = GraphicsQuality.High)
	{
		if (!IsValidLight(light)) return;

		light.ShadowEnabled = ShouldEnableShadows(light);
		if (!light.ShadowEnabled) return;

		light.DirectionalShadowMaxDistance = 200.0f;
		light.DirectionalShadowMode = GetShadowMode(quality);
	}

	private static bool IsValidLight(DirectionalLight3D light)
	{
		if (light == null) return false;
		if (!GodotObject.IsInstanceValid(light)) return false;
		return true;
	}

	private static bool ShouldEnableShadows(DirectionalLight3D light)
	{
		if (GameSettings.DisableShadows) return false;
		if (light.LightEnergy <= 0.05f) return false;
		if (IsEditorShadowsDisabled()) return false;
		return true;
	}

	private static bool IsEditorShadowsDisabled()
	{
		GameHost host = Realm.Client.Core.GameHost.Instance;
		if (host == null) return false;
		if (!host.IsMapEditorMode) return false;
		if (!host.EditorDisableShadows) return false;
		return true;
	}

	private static T FindNodeInTree<T>(Node parent) where T : Node
	{
		if (parent is T t) return t;
		int childCount = parent.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			var found = FindNodeInTree<T>(parent.GetChild(i));
			if (found != null) return found;
		}
		return null;
	}

	private class SettingsData
	{
		public int ResolutionIdx { get; set; } = 0;
		public int WindowedResolutionWidth { get; set; } = 0;
		public int WindowedResolutionHeight { get; set; } = 0;
		public GraphicsQuality QualityIdx { get; set; } = GraphicsQuality.High;
		public WindowMode WindowModeIdx { get; set; } = WindowMode.Fullscreen;
		public bool Vsync { get; set; } = true;
		public float MasterVolume { get; set; } = 80f;
		public float MusicVolume { get; set; } = 70f;
		public float SfxVolume { get; set; } = 90f;
		public float VoiceVolume { get; set; } = 60f;
		public float ScrollSpeed { get; set; } = 50f;
		public float MouseSens { get; set; } = 40f;
		public float HudScale { get; set; } = 100f;
		public HealthBarMode ShowHealthBars { get; set; } = HealthBarMode.Damaged;
		public GameLanguage Language { get; set; } = GameLanguage.English;
		public bool DisplayFps { get; set; } = false;
		public bool RecordReplays { get; set; } = false;
		public bool SeedMapFiles { get; set; } = false;
		public bool DisableShadows { get; set; } = false;
		public bool DisableDayNightLighting { get; set; } = false;
		public bool FloatingCombatText { get; set; } = true;
		public string? LastOpenedFolder { get; set; }
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DXGI_ADAPTER_DESC1
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Description;
		public uint VendorId;
		public uint DeviceId;
		public uint SubSysId;
		public uint Revision;
		public nuint DedicatedVideoMemory;
		public nuint DedicatedSystemMemory;
		public nuint SharedSystemMemory;
		public long AdapterLuid;
		public uint Flags;
	}

	private static readonly System.Guid IID_IDXGIFactory1 = new System.Guid("770aae78-f26f-4dba-a829-253c83d1b387");

	[DllImport("dxgi.dll")]
	private static extern int CreateDXGIFactory1([In] ref System.Guid riid, out System.IntPtr ppFactory);

	public static float GetGpuVramGb()
	{
		if (!System.OperatingSystem.IsWindows()) return 8.0f;

		try
		{
			var guid = IID_IDXGIFactory1;
			if (CreateDXGIFactory1(ref guid, out System.IntPtr pFactory) != 0 || pFactory == System.IntPtr.Zero)
				return 8.0f;

			ulong maxDedicatedVramBytes = GetMaxVramFromFactory(pFactory);

			if (maxDedicatedVramBytes > 0)
				return (float)(maxDedicatedVramBytes / (1024.0 * 1024.0 * 1024.0));
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Failed to query GPU VRAM via DXGI: {ex.Message}");
		}

		return 8.0f;
	}

	private static unsafe ulong GetMaxVramFromFactory(System.IntPtr pFactory)
	{
		ulong maxDedicatedVramBytes = 0;
		uint adapterIndex = 0;
		void** factoryVtbl = *(void***)pFactory;
		var enumAdapters1 = (delegate* unmanaged[Stdcall]<System.IntPtr, uint, out System.IntPtr, int>)factoryVtbl[12];
		var releaseFactory = (delegate* unmanaged[Stdcall]<System.IntPtr, uint>)factoryVtbl[2];

		while (enumAdapters1(pFactory, adapterIndex, out System.IntPtr pAdapter) == 0 && pAdapter != System.IntPtr.Zero)
		{
			ulong vram = GetVramFromAdapter(pAdapter);
			if (vram > maxDedicatedVramBytes) maxDedicatedVramBytes = vram;
			adapterIndex++;
		}

		releaseFactory(pFactory);
		return maxDedicatedVramBytes;
	}

	private static unsafe ulong GetVramFromAdapter(System.IntPtr pAdapter)
	{
		void** adapterVtbl = *(void***)pAdapter;
		var getDesc1 = (delegate* unmanaged[Stdcall]<System.IntPtr, out DXGI_ADAPTER_DESC1, int>)adapterVtbl[10];
		var releaseAdapter = (delegate* unmanaged[Stdcall]<System.IntPtr, uint>)adapterVtbl[2];

		ulong vram = 0;
		if (getDesc1(pAdapter, out DXGI_ADAPTER_DESC1 desc) == 0)
		{
			const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
			if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) == 0)
			{
				vram = (ulong)desc.DedicatedVideoMemory;
			}
		}

		releaseAdapter(pAdapter);
		return vram;
	}

	public static GraphicsQuality AutoDetectQuality()
	{
		float vramGb = GetGpuVramGb();
		if (vramGb <= 3.0f)
		{
			return GraphicsQuality.Low;
		}
		if (vramGb <= 6.0f)
		{
			return GraphicsQuality.Medium;
		}
		return GraphicsQuality.High;
	}
}