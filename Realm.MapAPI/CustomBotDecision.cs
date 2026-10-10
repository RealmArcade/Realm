using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Represents a map-defined custom candidate action submitted to the AI utility decision loop.
/// </summary>
public class CustomBotDecision
{
	/// <summary>
	/// Gets or sets the unique action identifier.
	/// </summary>
	public string ActionId { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the command intent name (e.g., "Build", "Cast", "Transact", "Interact").
	/// </summary>
	public string Intent { get; set; } = "Interact";

	/// <summary>
	/// Gets or sets the feature vector evaluated against the AI weights.
	/// </summary>
	public float[] FeatureVector { get; set; } = Array.Empty<float>();

	/// <summary>
	/// Gets or sets the target world position associated with this action.
	/// </summary>
	public Vector3 TargetPosition { get; set; } = Vector3.Zero;

	/// <summary>
	/// Gets or sets arbitrary payload data or arguments passed to the execution callback.
	/// </summary>
	public string Payload { get; set; } = string.Empty;
}