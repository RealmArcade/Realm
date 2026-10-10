using MemoryPack;
using System.Collections.Generic;

namespace Realm.Client;

/// <summary>Command sent from a client to the server representing a player action.</summary>
[MemoryPackable]
public partial struct NetworkCommand
{
	public int CommandId;
	public string CommandType;
	public List<int> UnitEntityIds;
	public NetworkVector3 TargetPosition;
	public int TargetEntityId;
	public string ArgString;
}