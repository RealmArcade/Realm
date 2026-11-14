using MemoryPack;

namespace Realm.Shared.Network;

public enum PresentationKind : byte
{
	PlayerMessage,
	BroadcastMessage,
	PlayerFeedbackText,
	FeedbackText,
	FloatingText,
	MinimapPing,
	TargetIndicator,
	PlayerDefeated,
	PlayerVictory,
	VisualEffect,
	BarrageVolley,
	GroundRing,
	BurstSphere,
	TerrainLine,
	PersistentAura,
	Projectile,
	StaticTextCreated,
	StaticTextUpdated,
	StaticTextColor,
	StaticTextVisible,
	StaticTextDestroyed,
	LeaderboardVisible,
	LeaderboardValue,
	LeaderboardCleared,
	LeaderboardBoard,
	LeaderboardBoardCleared,
	LeaderboardBoardGroup,
	LeaderboardBoardRow,
	LeaderboardRow,
	SummaryTableVisible,
	SummaryTableHeaders,
	SummaryTableRow,
	SummaryTableCleared,
	CountdownStarted,
	CountdownStopped,
	CountdownLabel,
	CameraPanned,
	CameraShaken,
	CameraBoundsSet,
	CameraBoundsReset,
	HudElementVisible,
	SelectionChanged,
	WarningSound,
	ClickSound,
	BuildingPlacementStarted,
	TimeOfDaySet,
	DayNightCycleSet,
	EnvironmentPresetSet,
	EnvironmentPresetTransitioned,
	WeatherSet,
	UnitColored,
	UnitAnimationSet,
	UnitHandAttachmentSet,
	UnitMeshImpulse,
	ResourceMeshImpulse,
	AbilityRegistered,
	PlayerAbilityRegistered,
	AbilityInstantSet,
	AbilityIconSet,
	AbilityTooltipSet,
	AbilityHotkeySet,
	AbilityGridPositionSet,
	ItemTooltipSet,
	TooltipNumberSet,
	TooltipTextSet,

	PlayerKicked,

	TechLevelSet,

	CastSound,

	InstructionsSectionAdded,
	ResourceNodeDepleted,
	ResourceNodeAmountChanged,
	UnitAnimationPulse,

	/// <summary>
	///     Asks the screen of one player to unlock a Steam achievement the map named. The identifier is the map's own,
	///     and the screen namespaces it before Steam sees it.
	/// </summary>
	SteamAchievementUnlocked,

	/// <summary>Asks the screen of one player to show how far they are toward an achievement Steam tracks as progress.</summary>
	SteamAchievementProgress,

	/// <summary>Asks the screen of one player to store an integer Steam stat.</summary>
	SteamStatIntSet,

	/// <summary>Asks the screen of one player to add to an integer Steam stat and store the result.</summary>
	SteamStatIntAdd,

	/// <summary>Asks the screen of one player to store a floating point Steam stat.</summary>
	SteamStatFloatSet,

	/// <summary>
	///     Asks the screen of one player to upload the cloud files this map keeps for them, which it answers with a
	///     <see cref="NetworkFrameKind.SteamCloudUpload" /> frame carrying the request's handle.
	/// </summary>
	SteamCloudFilesRequested,

	/// <summary>Asks the screen of one player to write a cloud file for this map.</summary>
	SteamCloudFileWritten,

	/// <summary>Asks the screen of one player to delete a cloud file for this map.</summary>
	SteamCloudFileDeleted
}

[MemoryPackable]
public partial struct PresentationMessage
{
	public int Sequence;

	public const int StateDescriptionSequence = -1;

	public byte Kind;

	public int PlayerIndex;

	public int Handle;

	public string Key;

	public string Text;
	public string Text2;
	public string Text3;

	public string[]? Values;

	public NetworkVector3 Position;
	public NetworkVector3 Secondary;
	public NetworkVector3 Color;
	public int Count;
	public int SecondCount;
	public float Value;
	public float Radius;
	public float Duration;
	public bool Flag;
}
