using System.Numerics;
using Realm.Ecs;

namespace Realm.MapAPI;

/// <summary>
/// Adds the map script surface of a resource node: what it yields and how it reacts on screen.
/// </summary>
/// <remarks>
/// The handle itself is a plain index owned by the shared simulation, so the simulation can name this subject
/// without the script surface. What a script reads answers straight out of the world the process is simulating,
/// and everything that changes the world routes through the authoritative host imports.
/// </remarks>
public static unsafe class ResourceNodeIdScriptExtensions
{
	extension(ResourceNodeId resourceNodeId)
	{
	/// <summary>
	/// Gets the resource category identifier provided by this node (e.g., "gold", "wood", "stone").
	/// </summary>
	public string ResourceType => GameAPI.Current.GetResourceNodeType(resourceNodeId);

	/// <summary>
	/// Gets the position of the resource node in 3D world space.
	/// </summary>
	public Vector3 Position => GameAPI.Current.GetResourceNodePosition(resourceNodeId);

	/// <summary>
	/// Gets a value indicating whether the resource node is fully depleted of resources.
	/// </summary>
	public bool IsDepleted => GameAPI.Current.IsResourceNodeDepleted(resourceNodeId);

	/// <summary>
	/// Triggers an elastic deformation mesh impulse visual effect on the resource node model.
	/// </summary>
	/// <param name="strength">The peak displacement amplitude of the impulse vibration.</param>
	/// <param name="duration">The total time in seconds over which the impulse decays.</param>
	/// <param name="frequency">The oscillatory speed of the displacement impulse.</param>
	public void TriggerMeshImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f) =>
		GameAPI.Current.TriggerResourceMeshImpulse(resourceNodeId, strength, duration, frequency);
	}
}
