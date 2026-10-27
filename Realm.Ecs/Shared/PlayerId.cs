namespace Realm.Ecs;

/// <summary>
/// Identifies a player slot in the running match by its zero-based index.
/// </summary>
public readonly unsafe record struct PlayerId(int Value)
{
	public static implicit operator PlayerId(int value) => new(value);

	public static explicit operator int(PlayerId id) => id.Value;

	public static readonly PlayerId Invalid = new(-1);

	public bool IsValid => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS;

	public int Index => Value;

	public int Id => Value;

	public override string ToString() => Value.ToString();

	public bool IsComputer
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS && EcsSharedMemory.GetComponent<ScriptPlayer>(ComponentIds.ScriptPlayer, Value)->IsComputer;
		set => HandleCommands.Current?.SetPlayerComputerControlled(Value, value);
	}

	public int Team
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<ScriptPlayer>(ComponentIds.ScriptPlayer, Value)->Team : 0;
		set => HandleCommands.Current?.SetPlayerTeam(Value, value);
	}

	public float Gold
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<PlayerResources>(ComponentIds.PlayerResources, Value + 1)->Gold : 0f;
		set => HandleCommands.Current?.SetPlayerGold(Value, value);
	}

	public float Wood
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<PlayerResources>(ComponentIds.PlayerResources, Value + 1)->Wood : 0f;
		set => HandleCommands.Current?.SetPlayerWood(Value, value);
	}

	public float Stone
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<PlayerResources>(ComponentIds.PlayerResources, Value + 1)->Stone : 0f;
		set => HandleCommands.Current?.SetPlayerStone(Value, value);
	}

	public int MaxPopulation
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<PlayerPopulation>(ComponentIds.PlayerPopulation, Value + 1)->Max : 0;
		set => HandleCommands.Current?.SetPlayerMaxPopulation(Value, value);
	}

	public int Kills
	{
		get => Value >= 0 && Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<ScriptPlayer>(ComponentIds.ScriptPlayer, Value)->KillCount : 0;
		set => HandleCommands.Current?.SetPlayerKills(Value, value);
	}
}
