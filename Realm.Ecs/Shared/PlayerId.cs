namespace Realm.Ecs;

/// <summary>
/// Identifies a player slot in the running match by its zero-based index.
/// </summary>
public readonly unsafe record struct PlayerId(int Value)
{
	/// <summary>
	/// Implicitly converts a 32-bit integer slot index into a <see cref="PlayerId"/>.
	/// </summary>
	/// <param name="value">The zero-based player slot index.</param>
	public static implicit operator PlayerId(int value) => new(value);

	/// <summary>
	/// Explicitly converts a <see cref="PlayerId"/> into a 32-bit integer slot index.
	/// </summary>
	/// <param name="id">The player identifier.</param>
	public static explicit operator int(PlayerId id) => id.Value;

	/// <summary>
	/// Gets an invalid or unassigned player slot identifier.
	/// </summary>
	public static readonly PlayerId Invalid = new(-1);

	/// <summary>
	/// Gets a value indicating whether this identifier represents a valid player slot index.
	/// </summary>
	public bool IsValid => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS;

	/// <summary>
	/// Gets the zero-based slot index of this player.
	/// </summary>
	public int Index => Value;

	/// <summary>
	/// Gets the zero-based slot index of this player (alias for <see cref="Index"/>).
	/// </summary>
	public int Id => Value;

	/// <summary>
	/// Formats the player identifier as a string.
	/// </summary>
	/// <returns>A string representation of the player slot index.</returns>
	public override string ToString() => Value.ToString();

	/// <summary>
	/// Gets or sets a value indicating whether this player slot is controlled by a computer AI.
	/// </summary>
	public bool IsComputer
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS && EcsSharedMemory.GetComponent<ScriptPlayer>(ComponentIds.ScriptPlayer, Value)->IsComputer;
		set => HandleCommands.Current?.SetPlayerComputerControlled(Value, value);
	}

	/// <summary>
	/// Gets or sets the team index that this player belongs to.
	/// </summary>
	public int Team
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<ScriptPlayer>(ComponentIds.ScriptPlayer, Value)->Team : 0;
		set => HandleCommands.Current?.SetPlayerTeam(Value, value);
	}

	/// <summary>
	/// Gets or sets the current gold resource amount for this player.
	/// </summary>
	public float Gold
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<PlayerResources>(ComponentIds.PlayerResources, Value + 1)->Gold : 0f;
		set => HandleCommands.Current?.SetPlayerGold(Value, value);
	}

	/// <summary>
	/// Gets or sets the current wood resource amount for this player.
	/// </summary>
	public float Wood
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<PlayerResources>(ComponentIds.PlayerResources, Value + 1)->Wood : 0f;
		set => HandleCommands.Current?.SetPlayerWood(Value, value);
	}

	/// <summary>
	/// Gets or sets the current stone resource amount for this player.
	/// </summary>
	public float Stone
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<PlayerResources>(ComponentIds.PlayerResources, Value + 1)->Stone : 0f;
		set => HandleCommands.Current?.SetPlayerStone(Value, value);
	}

	/// <summary>
	/// Gets or sets the maximum population supply capacity for this player.
	/// </summary>
	public int MaxPopulation
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<PlayerPopulation>(ComponentIds.PlayerPopulation, Value + 1)->Max : 0;
		set => HandleCommands.Current?.SetPlayerMaxPopulation(Value, value);
	}

	/// <summary>
	/// Gets or sets the total number of enemy unit kills credited to this player.
	/// </summary>
	public int Kills
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<ScriptPlayer>(ComponentIds.ScriptPlayer, Value)->KillCount : 0;
		set => HandleCommands.Current?.SetPlayerKills(Value, value);
	}
}
