using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     Every kind of request a map script can make of a screen. The numbers are part of the wire protocol, so a kind
///     keeps its value once a build ships it.
/// </summary>
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
	TooltipTextSet
}

/// <summary>
///     One request from the authority's scripting surface, addressed to a player or to everyone. It carries keys and
///     numbers rather than anything the authority holds, so a client can act on it without knowing anything the
///     authority has not told it.
/// </summary>
[MemoryPackable]
public partial struct PresentationMessage
{
	public byte Kind;

	/// <summary>The player this is addressed to, or nothing when every player should see it.</summary>
	public int PlayerIndex;

	/// <summary>The unit, resource node or text handle the request is about.</summary>
	public int Handle;

	/// <summary>What to show: an effect id, an environment preset, an animation, a leaderboard board.</summary>
	public string Key;

	public string Text;
	public string Text2;
	public string Text3;

	/// <summary>The columns of a summary table row, or a table's headers.</summary>
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
