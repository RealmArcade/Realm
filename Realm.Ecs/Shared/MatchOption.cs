namespace Realm.Ecs;

/// <summary>
///     A parameter a lobby's host may set for the match it is about to start. Every option names a value the game's own
///     formulas already read, so a host changes how much of something a match allows without selecting a different ruleset.
/// </summary>
public enum MatchOption : int
{
	/// <summary>How many players the lobby seats, which is also the ceiling on the seats a map fills.</summary>
	MaximumPlayers = 1,

	/// <summary>How many building options a draft offers at once.</summary>
	MaximumBuildingDraftCount = 2,

	/// <summary>How many unique instances of one building a player may own.</summary>
	MaximumUniqueBuildingInstances = 3,

	/// <summary>Whether vision is limited to sight ranges, and which of the two shroud modes applies.</summary>
	ShroudMode = 4,

	/// <summary>Whether every player draws the same heroes and the same building options.</summary>
	MirroredAutoDraft = 5,

	/// <summary>Whether a drafted building stays in the deck instead of leaving it when taken.</summary>
	OpenPoolDrafting = 6,

	/// <summary>The percentage every player's capacity ceiling is multiplied by.</summary>
	CapacityMultiplierPercent = 7,

	/// <summary>The percentage every player's maximum charges is multiplied by.</summary>
	ChargeMultiplierPercent = 8,

	/// <summary>How many buildings a player may construct and hold, which is the player's building supply.</summary>
	MaximumBuildingSupply = 9
}

/// <summary>
///     How much of the world a match shows. A lobby's host may override the mode the map states, and
///     <see cref="FromMap" /> is the absence of an override rather than a mode of its own.
/// </summary>
public enum MatchShroudMode : int
{
	/// <summary>Leaves the mode to the map, which is what a lobby that was not told anything plays.</summary>
	FromMap = 0,

	/// <summary>Shows the whole board to every player.</summary>
	Revealed = 1,

	/// <summary>Shows what a player's units can see, on ground the player has not walked.</summary>
	VisionShroud = 2,

	/// <summary>Shows what a player's units can see, on ground the player has not explored.</summary>
	ExplorationShroud = 3
}
