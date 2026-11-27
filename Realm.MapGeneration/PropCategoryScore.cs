namespace Realm.MapGeneration;

public class PropCategoryScore
{
	public int OriginalCount { get; set; }

	public int GeneratedCount { get; set; }

	public int Matched { get; set; }

	public float Precision => GeneratedCount > 0 ? Matched / (float)GeneratedCount : 0f;

	public float Recall => OriginalCount > 0 ? Matched / (float)OriginalCount : 0f;

	public float F1 => Precision + Recall > 0f ? 2f * Precision * Recall / (Precision + Recall) : 0f;
}
