using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum HealthBarMode
{
	Hidden = 0,
	Visible = 1,
	Damaged = 2
}

public enum GameLanguage
{
	English = 0,
	Spanish = 1,
	French = 2,
	German = 3,
	Portuguese = 4,
	Russian = 5,
	Chinese = 6,
	Japanese = 7,
	Arabic = 8,
	Hindi = 9
}

public enum GraphicsQuality
{
	Low = 0,
	Medium = 1,
	High = 2,
	Ultra = 3
}

public enum WindowMode
{
	Fullscreen = 0,
	Windowed = 1,
	Borderless = 2
}

public static class GameLanguageExtensions
{
	public static string ToLocaleCode(this GameLanguage language) => language switch
	{
		GameLanguage.English => "en",
		GameLanguage.Spanish => "es",
		GameLanguage.French => "fr",
		GameLanguage.German => "de",
		GameLanguage.Portuguese => "pt",
		GameLanguage.Russian => "ru",
		GameLanguage.Chinese => "zh",
		GameLanguage.Japanese => "ja",
		GameLanguage.Arabic => "ar",
		GameLanguage.Hindi => "hi",
		_ => "en"
	};

	public static GameLanguage ParseGameLanguage(string code) => code?.ToLowerInvariant() switch
	{
		"en" or "english" => GameLanguage.English,
		"es" or "spanish" => GameLanguage.Spanish,
		"fr" or "french" => GameLanguage.French,
		"de" or "german" => GameLanguage.German,
		"pt" or "portuguese" => GameLanguage.Portuguese,
		"ru" or "russian" => GameLanguage.Russian,
		"zh" or "chinese" => GameLanguage.Chinese,
		"ja" or "japanese" => GameLanguage.Japanese,
		"ar" or "arabic" => GameLanguage.Arabic,
		"hi" or "hindi" => GameLanguage.Hindi,
		_ => GameLanguage.English
	};
}

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
			QualityIdx = AutoDetectQuality();
			if (Resolutions != null && Resolutions.Count > 0)
			{
				WindowedResolutionWidth = Resolutions[0].X;
				WindowedResolutionHeight = Resolutions[0].Y;
			}
			Save();
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

				if (Resolutions != null && Resolutions.Count > 0)
				{
					if (WindowedResolutionWidth > 0 && WindowedResolutionHeight > 0)
					{
						int matchIndex = Resolutions.FindIndex(r => r.X == WindowedResolutionWidth && r.Y == WindowedResolutionHeight);
						if (matchIndex >= 0)
						{
							ResolutionIdx = matchIndex;
						}
						else
						{
							ResolutionIdx = Math.Clamp(ResolutionIdx, 0, Resolutions.Count - 1);
							WindowedResolutionWidth = Resolutions[ResolutionIdx].X;
							WindowedResolutionHeight = Resolutions[ResolutionIdx].Y;
						}
					}
					else
					{
						ResolutionIdx = Math.Clamp(ResolutionIdx, 0, Resolutions.Count - 1);
						WindowedResolutionWidth = Resolutions[ResolutionIdx].X;
						WindowedResolutionHeight = Resolutions[ResolutionIdx].Y;
					}
				}
			}
		}
		catch (System.Exception e)
		{
			GD.PrintErr($"Failed to deserialize settings: {e.Message}");
			ResetToDefaults();
		}
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

		var viewport = contextNode.GetViewport();
		if (viewport != null && GodotObject.IsInstanceValid(viewport))
		{
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
					viewport.UseTaa = QualityIdx == GraphicsQuality.Ultra;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
					break;
			}

			viewport.Scaling3DScale = 1.0f;
			viewport.Msaa3D = Viewport.Msaa.Disabled;
			viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
		}

		WorldEnvironment worldEnv = null;
		DirectionalLight3D light = null;
		Window root = null;

		var tree = contextNode.GetTree();
		if (tree != null && GodotObject.IsInstanceValid(tree))
		{
			root = tree.Root;
			if (root != null && GodotObject.IsInstanceValid(root))
			{
				worldEnv = FindNodeInTree<WorldEnvironment>(root);
				light = FindNodeInTree<DirectionalLight3D>(root);
			}
		}

		if (worldEnv != null && GodotObject.IsInstanceValid(worldEnv) && worldEnv.Environment != null)
		{
			var env = worldEnv.Environment;
			if (!env.IsLocalToScene())
			{
				env = (Godot.Environment)env.Duplicate();
				worldEnv.Environment = env;
			}

			ApplyEnvironmentQuality(env, QualityIdx);
		}

		if (light != null && GodotObject.IsInstanceValid(light))
		{
			ApplyDirectionalLightQuality(light, QualityIdx);
		}

		var terrain = (root != null ? FindNodeInTree<RuntimeTerrain>(root) : null) ?? RuntimeTerrain.Instance;
		if (terrain != null && GodotObject.IsInstanceValid(terrain))
		{
			terrain.ApplyQualitySettings((int)QualityIdx);
		}

		var gameHost = (root != null ? FindNodeInTree<GameHost>(root) : null) ?? GameHost.Instance;
		if (gameHost != null && GodotObject.IsInstanceValid(gameHost))
		{
			if (DisableDayNightLighting)
			{
				gameHost.EnvironmentService?.UpdateDayNightVisuals(gameHost, 0f);
			}
			else
			{
				if (gameHost.EcsWorld != null && gameHost.EcsWorld.IsAlive(gameHost.WorldEntity) && gameHost.EcsWorld.Has<Realm.Ecs.Components.Core.WorldState>(gameHost.WorldEntity))
				{
					var state = gameHost.EcsWorld.Get<Realm.Ecs.Components.Core.WorldState>(gameHost.WorldEntity);
					float progress = state.TimeOfDayTimer / GameHost.TimeOfDayCycleDuration;
					gameHost.EnvironmentService?.UpdateDayNightVisuals(gameHost, progress);
				}
				else
				{
					gameHost.EnvironmentService?.UpdateDayNightVisuals(gameHost, 0f);
				}
			}
		}
	}

	public static void ApplyEnvironmentQuality(Godot.Environment env, GraphicsQuality quality = GraphicsQuality.High)
	{
		if (env == null || !GodotObject.IsInstanceValid(env)) return;

		env.TonemapMode = Godot.Environment.ToneMapper.Agx;
		env.AdjustmentEnabled = true;
		env.SsaoEnabled = quality > GraphicsQuality.Low;
		env.SsilEnabled = quality >= GraphicsQuality.High;
		env.SsrEnabled = quality == GraphicsQuality.Ultra;
		env.SdfgiEnabled = quality == GraphicsQuality.Ultra;
		env.FogEnabled = true;
		env.GlowEnabled = quality > GraphicsQuality.Low;
	}

	public static void ApplyDirectionalLightQuality(DirectionalLight3D light, GraphicsQuality quality = GraphicsQuality.High)
	{
		if (light == null || !GodotObject.IsInstanceValid(light)) return;

		bool editorDisabled = GameHost.Instance != null && GameHost.Instance.IsMapEditorMode && GameHost.Instance.EditorDisableShadows;
		light.ShadowEnabled = !GameSettings.DisableShadows && !editorDisabled && light.LightEnergy > 0.05f;
		if (!light.ShadowEnabled) {
			return;
		}

		light.DirectionalShadowMaxDistance = 200.0f;
		if (quality == GraphicsQuality.Low)
		{
			light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal;
		}
		else if (quality == GraphicsQuality.Medium)
		{
			light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal;
		}
		else if (quality == GraphicsQuality.High)
		{
			light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
		}
		else
		{
			light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
		}
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
		if (!System.OperatingSystem.IsWindows())
		{
			return 8.0f;
		}

		try
		{
			var guid = IID_IDXGIFactory1;
			if (CreateDXGIFactory1(ref guid, out System.IntPtr pFactory) != 0 || pFactory == System.IntPtr.Zero)
			{
				return 8.0f;
			}

			ulong maxDedicatedVramBytes = 0;
			uint adapterIndex = 0;

			unsafe
			{
				void** factoryVtbl = *(void***)pFactory;
				var enumAdapters1 = (delegate* unmanaged[Stdcall]<System.IntPtr, uint, out System.IntPtr, int>)factoryVtbl[12];
				var releaseFactory = (delegate* unmanaged[Stdcall]<System.IntPtr, uint>)factoryVtbl[2];

				while (enumAdapters1(pFactory, adapterIndex, out System.IntPtr pAdapter) == 0 && pAdapter != System.IntPtr.Zero)
				{
					void** adapterVtbl = *(void***)pAdapter;
					var getDesc1 = (delegate* unmanaged[Stdcall]<System.IntPtr, out DXGI_ADAPTER_DESC1, int>)adapterVtbl[10];
					var releaseAdapter = (delegate* unmanaged[Stdcall]<System.IntPtr, uint>)adapterVtbl[2];

					if (getDesc1(pAdapter, out DXGI_ADAPTER_DESC1 desc) == 0)
					{
						const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
						if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) == 0)
						{
							ulong vram = (ulong)desc.DedicatedVideoMemory;
							if (vram > maxDedicatedVramBytes)
							{
								maxDedicatedVramBytes = vram;
							}
						}
					}

					releaseAdapter(pAdapter);
					adapterIndex++;
				}

				releaseFactory(pFactory);
			}

			if (maxDedicatedVramBytes > 0)
			{
				return (float)(maxDedicatedVramBytes / (1024.0 * 1024.0 * 1024.0));
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Failed to query GPU VRAM via DXGI: {ex.Message}");
		}

		return 8.0f;
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
