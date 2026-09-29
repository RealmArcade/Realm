using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Represents a safe interface for resource nodes (such as gold mines, trees, or rocks) within a map.
/// </summary>
public interface IResourceNode
{
    /// <summary>
    /// Gets the type of resource provided by this node (e.g., "gold", "wood", "stone").
    /// </summary>
    string ResourceType { get; }

    /// <summary>
    /// Gets the position of the resource node in 3D world space.
    /// </summary>
    Vector3 Position { get; }

    /// <summary>
    /// Gets or sets the remaining amount of resource in this node.
    /// </summary>
    float ResourceAmount { get; set; }

    /// <summary>
    /// Gets a value indicating whether the resource node has been depleted.
    /// </summary>
    bool IsDepleted { get; }

    /// <summary>
    /// Triggers a transient procedural mesh deformation or shake impulse on this resource node.
    /// </summary>
    /// <param name="strength">The peak strength of the impulse deformation.</param>
    /// <param name="duration">The duration of the impulse oscillation in seconds.</param>
    /// <param name="frequency">The oscillation frequency in Hz.</param>
    void TriggerMeshImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f);
}
