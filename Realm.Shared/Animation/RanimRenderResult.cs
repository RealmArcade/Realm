namespace Realm.Shared.Animation;

public class RanimRenderResult
{
	public List<RanimRenderFrame> Frames { get; set; } = new();
	public float Duration { get; set; }
	public float EffectiveFps { get; set; }
	public int TotalSourceFrames { get; set; }
	public int ModulusStep { get; set; } = 1;
}