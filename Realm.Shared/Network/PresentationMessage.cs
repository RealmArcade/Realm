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

	CastSound
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
