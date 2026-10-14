namespace Realm.Client.Network;

public struct GameStabilitySummary
{
	public float AvgTickMs { get; set; }
	public float MedianTickMs { get; set; }
	public float MaxTickMs { get; set; }
	public float AvgApiMs { get; set; }
	public float MedianApiMs { get; set; }
	public float MaxApiMs { get; set; }
}