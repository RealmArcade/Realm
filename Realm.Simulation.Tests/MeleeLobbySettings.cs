namespace Realm.Simulation.Tests;

/// <summary>
///     The identifiers MeleeRuleset declares its own match settings under, restated here because a map script's constants
///     live inside the compiled map rather than in this assembly. A test that sets one of them is stating the value a
///     lobby's host would have chosen, which is exactly what the rule set reads back when the match starts.
/// </summary>
public static class MeleeLobbySettings
{
	public const int DraftOptions = 1;

	public const int BuildingSupply = 2;

	public const int BuildingCopies = 3;

	public const int CapacityPercent = 4;

	public const int ProductionPercent = 5;

	public const int MirroredDraft = 6;

	public const int OpenPoolDrafting = 7;
}
