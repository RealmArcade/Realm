using System.Numerics;

namespace Realm.Ecs.AI.Affordances;

/// <summary>
/// Represents a generic candidate action discovered by the AI action scanner, addressed by its action id when it
/// executes and by its payload id when a decision policy pins it.
/// </summary>
public struct GenericAffordance
{
	public Entity SourceEntity;
	public CommandIntent Intent;
	public Entity TargetEntity;
	public Vector3 TargetPosition;
	public string ActionId;
	public string PayloadId;
	public float[] FeatureVector;

	public GenericAffordance(Entity sourceEntity, CommandIntent intent, Entity targetEntity, Vector3 targetPosition, string actionId, float[] featureVector)
	{
		SourceEntity = sourceEntity;
		Intent = intent;
		TargetEntity = targetEntity;
		TargetPosition = targetPosition;
		ActionId = actionId;
		PayloadId = actionId;
		FeatureVector = featureVector;
	}

	public GenericAffordance(Entity sourceEntity, CommandIntent intent, Entity targetEntity, Vector3 targetPosition, string actionId, string payloadId, float[] featureVector)
	{
		SourceEntity = sourceEntity;
		Intent = intent;
		TargetEntity = targetEntity;
		TargetPosition = targetPosition;
		ActionId = actionId;
		PayloadId = payloadId;
		FeatureVector = featureVector;
	}
}
