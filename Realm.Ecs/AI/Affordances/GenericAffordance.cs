using System.Numerics;
using Arch.Core;

namespace Realm.Ecs.AI.Affordances;

/// <summary>
/// Represents a generic candidate action discovered by the AI action scanner.
/// </summary>
public struct GenericAffordance
{
	public Entity SourceEntity;
	public CommandIntent Intent;
	public Entity TargetEntity;
	public Vector3 TargetPosition;
	public string PayloadId;
	public float[] FeatureVector;

	public GenericAffordance(Entity sourceEntity, CommandIntent intent, Entity targetEntity, Vector3 targetPosition, string payloadId, float[] featureVector)
	{
		SourceEntity = sourceEntity;
		Intent = intent;
		TargetEntity = targetEntity;
		TargetPosition = targetPosition;
		PayloadId = payloadId;
		FeatureVector = featureVector;
	}
}