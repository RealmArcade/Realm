namespace Realm.Client.Network;

public class DiagnosticResult
{
	public float MinRtt { get; set; }
	public float MaxRtt { get; set; }
	public float AvgRtt { get; set; }
	public float Jitter { get; set; }
	public float LossPercentage { get; set; }
	public int MaxConsecutiveLoss { get; set; }
}