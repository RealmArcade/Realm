using Realm.Shared.ModelOptimization;

namespace Realm.Shared;

public class GlbPlayerColorOptions
{
	public string ChromaKey { get; set; } = "#FF00FF";
	public string TargetHex { get => ChromaKey; set => ChromaKey = value; }
	public bool AutoCorrectChromaKey { get; set; } = true;
	public float CoreThreshold { get; set; } = 0.88f;
	public float FringeThreshold { get; set; } = 0.80f;
	public int MinClusterFaces { get; set; } = 10;
	public int DilationRadius { get; set; } = 3;
	public float CreaseAngleDegrees { get; set; } = GlbMeshSmoother.DefaultCreaseAngleDegrees;
}