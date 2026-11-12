using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Shared.Maps;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Walks a match that is started from a roster file alone, with no lobby to carry the host's settings: the options the
///     host configured are written beside the seats, read back by the authority that plays the match, and reach the script
///     as the draw size and vision mode the host stated.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MatchRosterOptionTests
{
	private static readonly Vector3 FirstSite = new(-50f, 0f, -50f);

	private string _workDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_workDirectory = Path.Combine(Path.GetTempPath(), $"realm-roster-options-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_workDirectory);
	}

	[TearDown]
	public void TearDown()
	{
		try
		{
			Directory.Delete(_workDirectory, recursive: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}

	[Test]
	public void TheRosterStatesTheOptionsTheHostConfigured()
	{
		string path = Path.Combine(_workDirectory, $"match{MatchRosterFile.FileSuffix}");
		var options = new MatchOptionSet();
		options.Set(MatchOption.MaximumBuildingDraftCount, 2);
		options.Set(MatchOption.ShroudMode, (int)MatchShroudMode.ExplorationShroud);

		MatchRosterFile.Write(path, new[] { new MatchRosterSeat(1, 1001, "Host", 1, "Team 1", false) }, options);

		Assert.That(MatchRosterFile.TryRead(path, out MatchRosterSeat[] seats, out MatchOptionSet read, out string error), Is.True, error);
		Assert.That(seats.Length, Is.EqualTo(1), "The seat the host wrote is read back.");
		Assert.That(read.Get(MatchOption.MaximumBuildingDraftCount), Is.EqualTo(2), "The draw size the host configured survives the file.");
		Assert.That(read.Get(MatchOption.ShroudMode), Is.EqualTo((int)MatchShroudMode.ExplorationShroud), "The vision mode the host configured survives the file.");
	}

	[Test]
	public void ARosterThatStatesNoOptionsLeavesEveryOptionToTheMap()
	{
		string path = Path.Combine(_workDirectory, $"default{MatchRosterFile.FileSuffix}");

		MatchRosterFile.Write(path, new[] { new MatchRosterSeat(1, 1001, "Host", 1, "Team 1", false) });

		Assert.That(MatchRosterFile.TryRead(path, out _, out MatchOptionSet read, out string error), Is.True, error);
		Assert.That(read.IsEmpty, Is.True, "A roster written without settings states none, so the map's own defaults apply.");
	}

	[Test]
	public void ARosterThatOmitsTheOptionLineIsRefused()
	{
		string path = Path.Combine(_workDirectory, $"stale{MatchRosterFile.FileSuffix}");
		File.WriteAllText(path, "realm-match-roster 5\n1\t1001\tHost\t1\tTeam 1\t0\t2\tVale\n");

		Assert.That(
			MatchRosterFile.TryRead(path, out _, out _, out string error),
			Is.False,
			"A roster that does not state its options is refused rather than played with the map's defaults, so a file written by an older build is never mistaken for one that configured nothing.");
		Assert.That(error, Does.Contain("second line"), "The refusal names the line it is missing.");
	}

	[Test]
	public void ARosterStatingAnOptionTheMatchCouldNotPlayIsRefused()
	{
		string path = Path.Combine(_workDirectory, $"impossible{MatchRosterFile.FileSuffix}");
		File.WriteAllText(path, "realm-match-roster 5\noptions\t2=99\n1\t1001\tHost\t1\tTeam 1\t0\t2\tVale\n");

		Assert.That(
			MatchRosterFile.TryRead(path, out _, out _, out string error),
			Is.False,
			"A setting no match could play is refused rather than clamped into a number the host never asked for.");
		Assert.That(error, Does.Contain("could not play"), "The refusal says the value is outside what the option allows.");
	}

	[Test]
	public void AMatchStartedFromARosterAlonePlaysTheOptionsItStates()
	{
		MeleeTestMaps.RequireMaps();

		string path = Path.Combine(_workDirectory, $"match{MatchRosterFile.FileSuffix}");
		var options = new MatchOptionSet();
		options.Set(MatchOption.MaximumBuildingDraftCount, 1);
		options.Set(MatchOption.ShroudMode, (int)MatchShroudMode.Revealed);
		MatchRosterFile.Write(path, new[] { new MatchRosterSeat(1, 1001, "Vale", 1, "Team 1", false), new MatchRosterSeat(2, 1002, "Sable", 2, "Team 2", false) }, options);

		Assert.That(MatchRosterFile.TryRead(path, out _, out MatchOptionSet read, out string error), Is.True, error);

		using MeleeMatch match = MeleeMatch.StartDuel(matchOptions: read);

		Assert.That(match.Api.GetMatchOption(MatchOption.MaximumBuildingDraftCount), Is.EqualTo(1), "The script reads the draw size the roster states.");
		Assert.That(match.Simulation.Map.Simulation.ShroudDisabled, Is.True, "The vision mode the roster states stands over the one the map states.");

		DraftOffer offer = match.BeginConstruction(1, FirstSite);
		Assert.That(offer.AbilityIds.Length, Is.EqualTo(1), "The draw the roster configured offers exactly one building.");
	}
}
