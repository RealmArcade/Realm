namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents a queue of pending visual effect requests.
/// </summary>
public record struct VFXQueue(List<VFXRequest> Requests);
