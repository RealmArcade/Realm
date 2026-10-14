namespace Realm.Ecs.AI.Simulation;

public class SimulationMatchResult
{
	public int WinnerPlayerIndex { get; set; } = -1;
	public int TotalTicksExecuted { get; set; }
	public float MatchDurationSeconds { get; set; }
	public int Player0UnitsBuilt { get; set; }
	public int Player1UnitsBuilt { get; set; }
}