using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Holds network synchronization, snapshot sequences, command sequencing, and latency tracking state.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct NetworkState
	{
		public int NextCommandId;
		public float CommandSendTimer;
		public ulong LastSnapshotReceivedTime;
		public int LastAppliedSnapshotSequence;
		public int LastReceivedBaselineSeq;
		public byte HasReceivedInitialBaselineByte;

		public bool HasReceivedInitialBaseline
		{
			get => HasReceivedInitialBaselineByte != 0;
			set => HasReceivedInitialBaselineByte = value ? (byte)1 : (byte)0;
		}

		public int SnapshotSequence;
		public int LocalPeerId;

		public NetworkState(
			int nextCommandId,
			float commandSendTimer,
			ulong lastSnapshotReceivedTime,
			int lastAppliedSnapshotSequence,
			int lastReceivedBaselineSeq,
			bool hasReceivedInitialBaseline,
			int snapshotSequence,
			int localPeerId)
		{
			NextCommandId = nextCommandId;
			CommandSendTimer = commandSendTimer;
			LastSnapshotReceivedTime = lastSnapshotReceivedTime;
			LastAppliedSnapshotSequence = lastAppliedSnapshotSequence;
			LastReceivedBaselineSeq = lastReceivedBaselineSeq;
			HasReceivedInitialBaselineByte = hasReceivedInitialBaseline ? (byte)1 : (byte)0;
			SnapshotSequence = snapshotSequence;
			LocalPeerId = localPeerId;
		}
	}
}