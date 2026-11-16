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
	public bool PositionalDoppler { get; set; } = false;
	public bool PositionalAttenuationFilter { get; set; } = true;
	public float ScrollSpeed { get; set; } = 50f;
	public float MouseSens { get; set; } = 40f;
	public float HudScale { get; set; } = 100f;
	public HealthBarMode ShowHealthBars { get; set; } = HealthBarMode.Damaged;
	public GameLanguage Language { get; set; } = GameLanguage.English;
	public bool DisplayFps { get; set; } = false;
	public bool DisplayPing { get; set; } = false;
	public bool RecordReplays { get; set; } = false;
	public bool SeedMapFiles { get; set; } = false;

	/// <summary>Whether this machine offers to carry other players' match traffic, which is off unless the player turned it on.</summary>
	public bool VolunteerRelay { get; set; } = false;

	/// <summary>How many other players' connections this machine carries at once while volunteering.</summary>
	public int VolunteerRelayMaximumConnections { get; set; } = 4;

	/// <summary>The most this machine forwards per second while volunteering, in bytes.</summary>
	public int VolunteerRelayBudgetBytesPerSecond { get; set; } = 512 * 1024;

	/// <summary>Whether a lobby this machine hosts may be carried through volunteers.</summary>
	public bool AcceptRelayedConnections { get; set; } = true;

	/// <summary>Whether a relayed connection may duplicate a datagram over a second path.</summary>
	public bool RelayDuplication { get; set; } = true;

	/// <summary>The most duplication one relayed connection may spend per second, in bytes.</summary>
	public int RelayDuplicationBudgetBytesPerSecond { get; set; } = 128 * 1024;
	public bool DisableShadows { get; set; } = false;
	public bool DisableDayNightLighting { get; set; } = false;
	public bool DisableWeatherEffects { get; set; } = false;
	public int FpsLimit { get; set; } = 0;
	public bool FloatingCombatText { get; set; } = true;
	public bool OccludedUnitOutlines { get; set; } = true;
	public string? LastOpenedFolder { get; set; }
	public string? LastHostedMapName { get; set; }
	public string? LastHostedMapVersion { get; set; }
	public string PlayerName { get; set; } = string.Empty;
}