using System.Numerics;
using Realm.Ecs;
using Realm.MapAPI;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	public event Action<UnitId>? OnUnitCreated;

	public event Action<UnitId, UnitId>? OnUnitDied;

	public event Action<UnitId, UnitId, float>? OnUnitDamaged;

	public event Action<UnitId, string, Vector3>? OnSpellCast;

	public event Action<string, UnitId, int>? OnPlayerChatMessage;

	public event Action<UnitId>? OnUnitSelected;

	public event Action<UnitId, string>? OnConsumableChanged;

	public event Action<UnitId, UnitId>? OnUnitAttacked;

	public event Action<LootItemId, UnitId>? OnLootItemCollected;

	public event Action<int>? OnTimerExpired;

	public event Action<UnitId, int>? OnUnitEnterZone;

	public event Action<int>? OnPlayerLeft;

	public event Action<UnitId, string>? OnItemSold;

	public event Action<UnitId>? OnConstructionFinished;

	public event Action<UnitId, string, Vector3>? OnUnitOrdered;

	public event Action<int, string, string, Vector3, string>? OnBotCustomActionExecuted;
}
