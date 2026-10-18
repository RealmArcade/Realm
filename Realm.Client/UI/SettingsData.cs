namespace Realm.Client.UI;

public class SettingsData
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