using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;


/// <summary>
///     Tag indicating the entity represents a player.
/// </summary>
[TagDefinition("Player", "Player", "Marks the entity as a player in the game.")]
public readonly record struct Player;