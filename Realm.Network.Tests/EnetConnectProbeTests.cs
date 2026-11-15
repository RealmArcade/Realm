using System;
using NUnit.Framework;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     Pins the two datagrams the reachability check depends on to the ones a real ENet client and the game's own authority
///     exchange, because a byte out of place is a probe that either never answers or never recognises the answer.
/// </summary>
[TestFixture]
public class EnetConnectProbeTests
{
	private const string CapturedOpeningPacket = "8FFF08B282FF00010000FF000000057000010000000000FF0000000000000000000013880000000200000002090C5A3C34D5597E";

	private const string CapturedVerifyConnect = "80002E0883FF0001000000010000057000010000000000FF0000000000000000000013880000000200000002090C5A3C";

	[Test]
	public void TheOpeningPacketIsTheOneAnEnetClientSends()
	{
		var datagram = new byte[EnetConnectProbe.ConnectDatagramBytes];
		int written = EnetConnectProbe.WriteConnect(datagram, 0x090C5A3Cu, 0x34D5597Eu, 0x08B2);

		Assert.That(written, Is.EqualTo(EnetConnectProbe.ConnectDatagramBytes));
		Assert.That(Convert.ToHexString(datagram), Is.EqualTo(CapturedOpeningPacket));
	}

	[Test]
	public void ATooSmallBufferCarriesNoPacket()
	{
		var datagram = new byte[EnetConnectProbe.ConnectDatagramBytes - 1];
		Assert.That(EnetConnectProbe.WriteConnect(datagram, 1, 2, 3), Is.Zero);
	}

	[Test]
	public void TheAnswerTheAuthoritySendsIsRecognisedWithItsTicket()
	{
		byte[] reply = Convert.FromHexString(CapturedVerifyConnect);

		bool recognised = EnetConnectProbe.TryReadVerifyConnect(reply, out EnetConnectReply parsed);

		Assert.That(recognised, Is.True);
		Assert.That(parsed.ConnectId, Is.EqualTo(0x090C5A3Cu));
		Assert.That(parsed.PeerId, Is.EqualTo(0));
		Assert.That(parsed.IncomingSessionId, Is.EqualTo(0));
		Assert.That(parsed.OutgoingSessionId, Is.EqualTo(1));
	}

	[Test]
	public void AProbeRecognisesTheOpeningPacketItWrote()
	{
		var datagram = new byte[EnetConnectProbe.ConnectDatagramBytes];
		EnetConnectProbe.WriteConnect(datagram, 0xDEADBEEFu, 0x12345678u, 0x0011);

		bool recognised = EnetConnectProbe.TryReadConnect(datagram, out uint connectId);

		Assert.That(recognised, Is.True);
		Assert.That(connectId, Is.EqualTo(0xDEADBEEFu));
	}

	[Test]
	public void APunchDatagramIsNeitherAProbeNorAnAnswer()
	{
		byte[] punch = "PUNCH"u8.ToArray();
		byte[] openingPacket = Convert.FromHexString(CapturedOpeningPacket);

		Assert.That(EnetConnectProbe.TryReadConnect(punch, out _), Is.False);
		Assert.That(EnetConnectProbe.TryReadVerifyConnect(punch, out _), Is.False);
		Assert.That(EnetConnectProbe.TryReadVerifyConnect(openingPacket, out _), Is.False);
	}
}
