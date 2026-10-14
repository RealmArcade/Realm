namespace Realm.Shared.Animation;

public class GlbAutoRiggerOptions
{
	public bool NoFingers { get; set; } = true;
	public bool UseNormals { get; set; } = true;
	public bool WeightPostprocess { get; set; } = true;
	public Action<string>? LogCallback { get; set; }
}