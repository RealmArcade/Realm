using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Shared.Maps;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Walks a match that is started from a roster file alone, with no lobby to carry the host's settings: the settings
///     the host configured and the shroud mode it chose are written beside the seats, read back by the authority that plays
///     the match, and reach the script as the draw size and vision mode the host stated.
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
		var settings = new LobbySettingSet();
		settings.Set(MeleeLobbySettings.DraftOptions, 2);

		MatchRosterFile.Write(path, new[] { new MatchRosterSeat(1, 1001, "Host", 1, "Team 1", false) }, MatchShroudMode.Full, settings);

		Assert.That(MatchRosterFile.TryRead(path, out MatchRosterSeat[] seats, out MatchShroudMode shroudMode, out LobbySettingSet read, out string error), Is.True, error);
		Assert.That(seats.Length, Is.EqualTo(1), "The seat the host wrote is read back.");
		Assert.That(read.Get(MeleeLobbySettings.DraftOptions), Is.EqualTo(2), "The draw size the host configured survives the file.");
		Assert.That(shroudMode, Is.EqualTo(MatchShroudMode.Full), "The shroud mode the host configured survives the file.");
	}

	[Test]
	public void ARosterThatStatesNoOptionsLeavesEveryOptionToTheMap()
	{
		string path = Path.Combine(_workDirectory, $"default{MatchRosterFile.FileSuffix}");

		MatchRosterFile.Write(path, new[] { new MatchRosterSeat(1, 1001, "Host", 1, "Team 1", false) });

		Assert.That(MatchRosterFile.TryRead(path, out _, out MatchShroudMode shroudMode, out LobbySettingSet read, out string error), Is.True, error);
		Assert.That(read.IsEmpty, Is.True, "A roster written without settings states none, so the map's own defaults apply.");
		Assert.That(shroudMode, Is.EqualTo(MatchShroudMode.FromMap), "A roster that states no shroud mode leaves it to the map.");
	}

	[Test]
	public void ARosterThatOmitsTheOptionLineIsRefused()
	{
		string path = Path.Combine(_workDirectory, $"stale{MatchRosterFile.FileSuffix}");
		File.WriteAllText(path, "realm-match-roster 6\nshroud\t0\n1\t1001\tHost\t1\tTeam 1\t0\t2\tVale\n");

		Assert.That(
			MatchRosterFile.TryRead(path, out _, out _, out _, out string error),
			Is.False,
			"A roster that does not state its settings is refused rather than played with the map's defaults, so a file written by an older build is never mistaken for one that configured nothing.");
		Assert.That(error, Does.Contain("third line"), "The refusal names the line it is missing.");
	}

	[Test]
	public void ARosterStatingAnOptionThatIsNotAnIdentifierAndAValueIsRefused()
	{
		string path = Path.Combine(_workDirectory, $"impossible{MatchRosterFile.FileSuffix}");
		File.WriteAllText(path, "realm-match-roster 6\nshroud\t0\nsettings\t2-99\n1\t1001\tHost\t1\tTeam 1\t0\t2\tVale\n");

		Assert.That(
			MatchRosterFile.TryRead(path, out _, out _, out _, out string error),
			Is.False,
			"A setting entry that is not an identifier and a value is refused rather than read as a setting nobody stated.");
		Assert.That(error, Does.Contain("not an identifier and a value"), "The refusal says what the entry was missing.");
	}

	[Test]
	public void AMatchStartedFromARosterAlonePlaysTheOptionsItStates()
	{
		MeleeTestMaps.RequireMaps();

		string path = Path.Combine(_workDirectory, $"match{MatchRosterFile.FileSuffix}");
		var settings = new LobbySettingSet();
		settings.Set(MeleeLobbySettings.DraftOptions, 1);
		MatchRosterFile.Write(path, new[] { new MatchRosterSeat(1, 1001, "Vale", 1, "Team 1", false), new MatchRosterSeat(2, 1002, "Sable", 2, "Team 2", false) }, MatchShroudMode.None, settings);

		Assert.That(MatchRosterFile.TryRead(path, out _, out MatchShroudMode readShroudMode, out LobbySettingSet read, out string error), Is.True, error);

		using MeleeMatch match = MeleeMatch.StartDuel(lobbySettings: read, shroudMode: readShroudMode);

		Assert.That(match.Api.GetLobbySetting(MeleeLobbySettings.DraftOptions), Is.EqualTo(1), "The script reads the draw size the roster states.");
		Assert.That(match.Simulation.Map.Simulation.ShroudDisabled, Is.True, "The shroud mode the roster states stands over the one the map states.");

		DraftOffer offer = match.BeginConstruction(1, FirstSite);
		Assert.That(offer.AbilityIds.Length, Is.EqualTo(1), "The draw the roster configured offers exactly one building.");
	}
}
