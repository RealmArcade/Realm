namespace Realm.Ecs;

/// <summary>
///     One row of a lobby's match settings, named so a map can say which rows its board is played with. The first nine
///     members mirror <see cref="MatchOption" /> value for value, because those rows are the match options themselves; the
///     last two are the lobby switches a map states through their own orders rather than through an option.
/// </summary>
public enum LobbySetting : int
{
	/// <summary>How many players the lobby seats, which is <see cref="MatchOption.MaximumPlayers" />.</summary>
	MaximumPlayers = 1,

	/// <summary>How many building options a draft offers at once.</summary>
	MaximumBuildingDraftCount = 2,

	/// <summary>How many unique instances of one building a player may own.</summary>
	MaximumUniqueBuildingInstances = 3,

	/// <summary>Whether vision is limited, and which shroud mode applies.</summary>
	ShroudMode = 4,

	/// <summary>Whether every player draws the same heroes and building options.</summary>
	MirroredAutoDraft = 5,

	/// <summary>Whether a drafted building stays in the deck instead of leaving it.</summary>
	OpenPoolDrafting = 6,

	/// <summary>The percentage every player's capacity ceiling is multiplied by.</summary>
	CapacityMultiplierPercent = 7,

	/// <summary>The percentage every player's stored production mana is multiplied by.</summary>
	ProductionMultiplierPercent = 8,

	/// <summary>How many buildings a player may construct and hold.</summary>
	MaximumBuildingSupply = 9,

	/// <summary>Whether a team's players sit in consecutive player slots in the match.</summary>
	TeamsTogether = 10,

	/// <summary>Whether players who join late watch for a delay before they see the board.</summary>
	SpectatorDelay = 11
}

/// <summary>Reads and writes a set of settings as the bit mask a lobby publishes.</summary>
public static class LobbySettings
{
	/// <summary>The highest member of <see cref="LobbySetting" />, which is also how many bits the mask uses.</summary>
	public const int HighestSetting = (int)LobbySetting.SpectatorDelay;

	/// <summary>Marks one setting hidden, or shows it again.</summary>
	/// <param name="mask">The mask being changed.</param>
	/// <param name="setting">The setting to change.</param>
	/// <param name="hidden">Whether the setting is hidden.</param>
	/// <returns>The mask with the change applied.</returns>
	public static int With(int mask, LobbySetting setting, bool hidden)
	{
		int bit = 1 << (int)setting;
		return hidden ? mask | bit : mask & ~bit;
	}

	/// <summary>Answers whether one setting is hidden in a published mask.</summary>
	/// <param name="mask">The published mask.</param>
	/// <param name="setting">The setting being read.</param>
	/// <returns>Whether the lobby hides the setting from its host.</returns>
	public static bool IsHidden(int mask, LobbySetting setting) => (mask & (1 << (int)setting)) != 0;

	/// <summary>
	///     Answers whether the setting a match option states is hidden, which is the rule a host's order is measured by.
	/// </summary>
	/// <param name="mask">The published mask.</param>
	/// <param name="option">The match option being read.</param>
	/// <returns>Whether the lobby hides the option.</returns>
	public static bool IsHidden(int mask, MatchOption option) => IsHidden(mask, (LobbySetting)(int)option);
}
