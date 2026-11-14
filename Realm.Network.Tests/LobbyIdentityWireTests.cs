using MemoryPack;
using NUnit.Framework;
using Realm.Shared.Network;

namespace Realm.Network.Tests;

/// <summary>
///     The Steam id a seat states has to survive the two hops that carry it from the client that states it to the seat that
///     reads it: the hello it is sent in, and the lobby state the authority publishes. A field appended to either frame is
///     only useful if the wire still binds it after the fields that were already there.
/// </summary>
[TestFixture]
public class LobbyIdentityWireTests
{
	private const ulong SteamId = 76561198000000009;

	[Test]
	public void TheHelloCarriesTheSteamIdAlongsideTheIdentityItAlreadyStated()
	{
		var hello = new ClientHello
		{
			Build = "v0.0.6",
			TokenProof = "proof",
			WireTableHash = "hash",
			MapHash = "map",
			Language = "en",
			PlayerName = "Player",
			AccountId = 42,
			SessionToken = "secret",
			AdminProof = "admin",
			SteamId = SteamId
		};

		ClientHello read = MemoryPackSerializer.Deserialize<ClientHello>(MemoryPackSerializer.Serialize(hello));

		Assert.That(read.SteamId, Is.EqualTo(SteamId));
		Assert.That(read.PlayerName, Is.EqualTo("Player"));
		Assert.That(read.AccountId, Is.EqualTo(42));
		Assert.That(read.SessionToken, Is.EqualTo("secret"));
	}

	[Test]
	public void ThePublishedSeatCarriesTheSteamIdAlongsideTheSeatItAlreadyDescribed()
	{
		var state = new LobbyStateMessage
		{
			Seats = new[]
			{
				new LobbySeat
				{
					PeerId = 2,
					Slot = 1,
					AccountId = 42,
					Name = "Friend",
					SteamId = SteamId,
					Difficulty = 0,
					ColorRgba = 0x11223344u
				}
			},
			MapName = "melee",
			Revision = 7
		};

		LobbyStateMessage read = MemoryPackSerializer.Deserialize<LobbyStateMessage>(MemoryPackSerializer.Serialize(state));

		Assert.That(read.Seats, Has.Length.EqualTo(1));
		Assert.That(read.Seats[0].SteamId, Is.EqualTo(SteamId));
		Assert.That(read.Seats[0].Name, Is.EqualTo("Friend"));
		Assert.That(read.Seats[0].ColorRgba, Is.EqualTo(0x11223344u));
		Assert.That(read.Revision, Is.EqualTo(7));
	}
}
