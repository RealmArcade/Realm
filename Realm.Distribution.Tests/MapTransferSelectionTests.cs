using System.Collections.Generic;
using NUnit.Framework;
using Realm.Shared.Network;

namespace Realm.Distribution.Tests;

/// <summary>
///     A joiner announces the files it already holds so the lobby leaves them out of its bundles, which means a screen
///     holding the whole map has to announce all of it: a screen that announces nothing because it is missing nothing
///     tells the lobby to send everything, which is the case these hold.
/// </summary>
[TestFixture]
public class MapTransferSelectionTests
{
	private static readonly string[] AssetKeys = { "aaa.rtex", "bbb.rtex", "ccc.raud", "aaa.rtex" };

	[Test]
	public void AHeldFileIsNeverAskedFor()
	{
		List<string> held = MapTransferSelection.Held(AssetKeys, new List<string> { "bbb.rtex", "ccc.raud" });

		Assert.That(held, Is.EqualTo(new[] { "aaa.rtex" }), "the files the archive answered as absent are not held, and a key listed twice is stated once");
	}

	[Test]
	public void AScreenHoldingTheWholeMapAnnouncesAllOfIt()
	{
		List<string> held = MapTransferSelection.Held(AssetKeys, new List<string>());

		Assert.That(held, Is.EquivalentTo(new[] { "aaa.rtex", "bbb.rtex", "ccc.raud" }));
	}

	[Test]
	public void AScreenHoldingNothingAnnouncesNothing()
	{
		List<string> held = MapTransferSelection.Held(AssetKeys, AssetKeys);

		Assert.That(held, Is.Empty);
	}
}
