using System.Numerics;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;

namespace Realm.Ecs.AI.Genres;

public class CustomGenreProvider : IAiGenreProvider
{
	public string GenreName => "custom";
	public int FeatureCount { get; set; } = 8;
	public IReadOnlyList<string> FeatureNames { get; set; } = Array.Empty<string>();

	public CustomAffordanceScannerDelegate? CustomScanner { get; set; }
	public CustomActionExecutorDelegate? CustomExecutor { get; set; }
	public float[] DefaultWeights { get; set; } = Array.Empty<float>();

	public float[] GetDefaultWeights()
	{
		if (DefaultWeights != null && DefaultWeights.Length == FeatureCount)
		{
			return (float[])DefaultWeights.Clone();
		}

		var weights = new float[FeatureCount];
		for (int i = 0; i < FeatureCount; i++)
		{
			weights[i] = 0.5f;
		}

		return weights;
	}

	public BotProfile CreateDefaultProfile(string mapName)
	{
		return new BotProfile
		{
			Genre = "custom",
			MapName = mapName,
			Weights = GetDefaultWeights()
		};
	}

	// IAiGenreProvider contract: the custom genre is configured through CustomScanner and CustomExecutor instead.
	public void ConfigureFromParameters(IReadOnlyDictionary<string, string> parameters)
	{
	}

	public void ScanAffordances(EcsWorld world, int playerIndex, List<GenericAffordance> destinationList, object? customContext = null)
	{
		destinationList.Clear();
		CustomScanner?.Invoke(world, playerIndex, destinationList, customContext);
	}

	public void ExecuteAction(EcsWorld world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback = null)
	{
		if (CustomExecutor != null)
		{
			CustomExecutor(world, playerIndex, aff, customActionCallback);
		}
		else
		{
			customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
		}
	}
}